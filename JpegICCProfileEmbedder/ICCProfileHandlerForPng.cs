using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace JpegICCProfileEmbedder
{
    /// <summary>
    /// Jpeg内のICC Profile操作
    /// http://hp.vector.co.jp/authors/VA032610/operation/MessageList.htm
    /// https://en.wikipedia.org/wiki/JPEG_File_Interchange_Format
    /// 
    ///
    /// </summary>
    public class PngChunkHandler
    {
        public enum ChunkType
        {
            IHDR = 0,
            IDAT,
            sRGB,
            iCCP,
            pHYs,
            //            PLTE,
            //            IEND,
            //            tRNS,
            //            gAMA,
            //            tEXt,
        }

        static int PNG_SignatureSize = 0x08;
        static byte[] PNGSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }; // Png File Signature


        const int ChunkTypeSize = 4;
        const int ChunkLengthSize = 4;
        const int ChunkCRCSize = 4;

        static int IHDR_ChunkSize = 25; // 
        static byte[] IHDR = { 0x49, 0x48, 0x44, 0x52 }; // IHDR Chunk type

        static byte[] IDAT = { 0x49, 0x44, 0x41, 0x54 }; // IDAT Chunk type
        static byte[] iCCP = { 0x69, 0x43, 0x43, 0x50 }; // iCCP Chunk type
        static byte[] sRGB = { 0x73, 0x52, 0x47, 0x42 }; // sRGB Chunk type
        static byte[] pHYs = { 0x70, 0x48, 0x59, 0x73 }; // pHYs Chunk type


        public static (float, float)GetDPIformPHYS(string filePath)
        {
            var (data, type, crc, size) = PngChunkHandler.Restore(filePath, PngChunkHandler.ChunkType.pHYs);
            return PngChunkHandler.GetDPIformPHYS(data);
        }

        private static (float, float)GetDPIformPHYS(byte[] buf)
        {
            if(buf.Count() != 9)
            {
                throw new InvalidDataException();
            }

            float dpiX = 0;
            float dpiY = 0;

            int dotPerMeter_X = BitConverter.ToInt32(BitConverter.IsLittleEndian ? buf.Take(4).Reverse().ToArray() : buf.Take(4).ToArray(), 0);
            int dotPerMeter_Y = BitConverter.ToInt32(BitConverter.IsLittleEndian ? buf.Skip(4).Take(4).Reverse().ToArray() : buf.Skip(4).Take(4).ToArray(), 0);
            int unit = buf[8];
            System.Diagnostics.Debug.WriteLine($"{dotPerMeter_X} x {dotPerMeter_Y} {(unit == 1 ? "Dot/Meter" : "Unknown")}");

            if(unit != 1)
            {
                throw new InvalidDataException($"Unit is unknown.");
            }

            dpiX = DpmToDpi(dotPerMeter_X);
            dpiY = DpmToDpi(dotPerMeter_Y);

            System.Diagnostics.Debug.WriteLine($"{dpiX} x {dpiY} dot/Inch");

            return (dpiX, dpiY);
        }


        // 1メートル = 39.3701 inch
        static float DpmToDpi(int dpm)
        {
            return (float)(dpm) / 39.370113f;

        }

        static byte[] ICCProfile_Header = { 0x49, 0x43, 0x43, 0x20, 0x50, 0x72, 0x6F, 0x66, 0x69, 0x6C, 0x65, 0x00, 0x00 }; 
        public static bool Insert_ICCProfile(string srcPath, string ICCProfile)
        {
            bool result = false;
            using (FileStream fsChunkData = new FileStream(ICCProfile, FileMode.Open, FileAccess.Read))
            { 
                var chunkDataBuffer = new byte[fsChunkData.Length];
                fsChunkData.Read(chunkDataBuffer, 0, chunkDataBuffer.Length);

                var compressed = zCompress(chunkDataBuffer);

                var crc = Crc32(Enumerable.Concat(ICCProfile_Header, compressed).ToArray(), ChunkType.iCCP);
                // CRCの書き込み
                byte[] crcBuf = BitConverter.IsLittleEndian ? BitConverter.GetBytes(crc).Reverse().ToArray() : BitConverter.GetBytes(crc);
                result = Insert(srcPath, ChunkDic[ChunkType.iCCP], Enumerable.Concat(ICCProfile_Header, compressed).ToArray(), crcBuf);
            }
            return result;
        }



        #region https://www.rainorshine.asia/2016/06/05/post2696.html

        public class Adler32 : HashAlgorithm
        {
            private const uint BASE = 65521; /* largest prime smaller than 65536 */

            private uint s1;
            private uint s2;

            public Adler32()
            {
                base.HashSizeValue = 32;
                Initialize();
            }

            protected override void HashCore(byte[] array, int ibStart, int cbSize)
            {
                while (--cbSize >= 0) {
                    s1 = (s1 + array[ibStart++]) % BASE;
                    s2 = (s2 + s1) % BASE;
                }
            }

            protected override byte[] HashFinal()
            {
                HashValue = new byte[] {
                (byte)((s2 >> 8) & 0xFF),
                (byte)( s2       & 0xFF),
                (byte)((s1 >> 8) & 0xFF),
                (byte)( s1       & 0xFF)
            };
                return HashValue;
            }

            public override void Initialize()
            {
                s1 = 1;
                s2 = 0;
            }
        }

        public static byte[] zCompress(byte[] buf)
        {
            byte[] cbuf;
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionLevel.Optimal))
                {
                    ds.Write(buf, 0, buf.Length);
                }

                ms.Close();
                cbuf = ms.ToArray();
            }

            using (var ms = new MemoryStream())
            {
                var zlibHeader = new byte[] { 0x78, 0x9C }; // CMF(CM, CINFO), FLG
                ms.Write(zlibHeader, 0, zlibHeader.Length);

                ms.Write(cbuf, 0, cbuf.Length);

                var adler32 = new Adler32();
                var checksum = adler32.ComputeHash(buf);
                ms.Write(checksum, 0, checksum.Length);

                ms.Close();
                return ms.ToArray();
            }
        }
        #endregion

        public static bool Insert(string srcPath, string ChunkPath, ChunkType type = ChunkType.iCCP)
        {
            bool result = false;
            using (FileStream fsChunkData = new FileStream(ChunkPath, FileMode.Open, FileAccess.Read))
            {
                var chunkDataBuffer = new byte[fsChunkData.Length];
                fsChunkData.Read(chunkDataBuffer, 0, chunkDataBuffer.Length);

                var crc = Crc32(chunkDataBuffer, type);
                // CRCの書き込み
                byte[] crcBuf = BitConverter.IsLittleEndian ? BitConverter.GetBytes(crc).Reverse().ToArray() : BitConverter.GetBytes(crc);
                result = Insert(srcPath, ChunkDic[type], chunkDataBuffer, crcBuf);
            }
            return result;
        }

        /// <summary>
        ///  https://stackoverflow.com/questions/24082305/how-is-png-crc-calculated-exactly
        /// </summary>
        static uint[] crcTable;

        public static uint Crc32(byte[] stream, ChunkType type)
        {
            var chunkTypeCrc = Crc32(ChunkDic[type], 0, ChunkTypeSize, 0);

            return Crc32(stream, 0, stream.Length, chunkTypeCrc);
        }

        // Stores a running CRC (initialized with the CRC of "IDAT" string). When
        // you write this to the PNG, write as a big-endian value

        // Call this function with the compressed image bytes, 
        // passing in idatCrc as the last parameter
        private static uint Crc32(byte[] stream, int offset, int length, uint crc)
        {

            uint c;
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint n = 0; n <= 255; n++)
                {
                    c = n;
                    for (var k = 0; k <= 7; k++)
                    {
                        if ((c & 1) == 1)
                            c = 0xEDB88320 ^ ((c >> 1) & 0x7FFFFFFF);
                        else
                            c = ((c >> 1) & 0x7FFFFFFF);
                    }
                    crcTable[n] = c;
                }
            }
            c = crc ^ 0xffffffff;
            var endOffset = offset + length;
            for (var i = offset; i < endOffset; i++)
            {
                c = crcTable[(c ^ stream[i]) & 255] ^ ((c >> 8) & 0xFFFFFF);
            }
            return c ^ 0xffffffff;
        }

        /// <summary>
        /// memory上のChunkをIHDRの直後に埋め込む
        /// </summary>
        /// <param name="srcPath"></param>
        /// <param name="ChunkData"></param>
        /// <returns></returns>
        public static bool Insert(string srcPath, byte[] ChunkType, byte[] ChunkData, byte[] ChunkCRC)
        {
            bool ret = false;

            using (FileStream fsImage = new FileStream(srcPath, FileMode.Open, FileAccess.ReadWrite))
            using (MemoryStream msImageWithoutHeader = new MemoryStream())
            {
                var originalFileSize = fsImage.Length;
                var originalICCProfileSize = ChunkData.Length;
                fsImage.Seek(0, SeekOrigin.Begin);

                // Memry Streamに保存する
                fsImage.CopyTo(msImageWithoutHeader);

                // Chunkを埋め込んだ後のファイルサイズにする。
                var expectedFileSize = fsImage.Length + ChunkType.Length + ChunkData.Length + ChunkCRC.Length;
                fsImage.SetLength(expectedFileSize);

                // PNG Signature / IHDR Chunkをとばす
                fsImage.Seek(PNG_SignatureSize + IHDR_ChunkSize, SeekOrigin.Begin);

                var totalChunkLength = ChunkLengthSize + ChunkTypeSize + ChunkData.Length + ChunkCRCSize;

                // Chunk Data Lengthの書き込み
                byte[] lengthBuf = BitConverter.GetBytes((Int32)ChunkData.Length);
                fsImage.Write(BitConverter.IsLittleEndian ? lengthBuf.Reverse().ToArray() : lengthBuf, 0, (int)ChunkLengthSize);

                // ChunkTypeの書き込み
                fsImage.Write(ChunkType, 0, (int)ChunkType.Length);

                // ChunkDataの書き込み
                fsImage.Write(ChunkData, 0, (int)ChunkData.Length);

                // ChunkCRCの書き込み
                fsImage.Write(ChunkCRC, 0, (int)ChunkCRC.Length);

                // MemoryにあるIHDRより後のChunkを書き込み
                var remainImageDataLength = (int)(msImageWithoutHeader.Length - (PNG_SignatureSize + IHDR_ChunkSize));
                fsImage.Write(msImageWithoutHeader.GetBuffer(), PNG_SignatureSize + IHDR_ChunkSize, remainImageDataLength);

                ret = true;
            }
            return ret;
        }



        /// <summary>
        /// PNG から指定のChunk Data部分をファイルに書き出す。
        /// </summary>
        /// <param name="srcPath">Source file</param>
        /// <param name="ChunkDataPath">Chunk Dataを保存するファイル</param>
        /// <returns>true/false</returns>
        public static bool Restore(string srcPath, string ChunkDataPath)
        {
            try
            {
                using (var fsChunkData = new FileStream(ChunkDataPath, FileMode.Create, FileAccess.Write))
                {
                    var (data, type, crc, size) = Restore(srcPath);
                    fsChunkData.Write(data, 0, data.Length);
                }
            }
            catch(Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"{e.ToString()}");
                return false;
            }
            return true;
        }

        internal static Dictionary<ChunkType, byte[]> ChunkDic = new Dictionary<ChunkType, byte[]>()
        {
            {ChunkType.IHDR, IHDR },
            {ChunkType.IDAT, IDAT },
            {ChunkType.iCCP, iCCP },
            {ChunkType.sRGB, sRGB },
            {ChunkType.pHYs, pHYs }
            // TODO other chunk support
        };

        /// <summary>
        /// PNG から指定のChunkを取り出す。
        /// </summary>
        /// <param name="srcPath">Source file</param>
        /// <param name="type">Chunk Data</param>
        /// <returns>ChunkData, ChunkType, ChunkCRC, size</returns>
        public static (byte[], byte[], byte[], int) Restore(string srcPath, ChunkType type = ChunkType.iCCP)
        {
            byte[] chunk = ChunkDic[type];

            using (FileStream fsSrcImage = new FileStream(srcPath, FileMode.Open, FileAccess.ReadWrite))
            {
                var originalFileSize = fsSrcImage.Length;
                int b;

                // TODO : IDATは先頭のChunkのみ
                while((b = fsSrcImage.ReadByte()) > -1 )
                {
                    // System.Diagnostics.Debug.WriteLine($"{fsSrcImage.Position}");
                    if (b == chunk[0]) // iCCP 1st byte
                    {
                        var ChunkType = new Byte[ chunk.Length];
                        fsSrcImage.Seek(-1, SeekOrigin.Current); //一バイト分もどる
                        fsSrcImage.Read(ChunkType, 0, ChunkType.Length); // Chunk Typeを比較
                        if(System.Linq.Enumerable.SequenceEqual (ChunkType.Take(ChunkTypeSize), chunk))
                        {
                            // Chunk Typeが一致
                            System.Diagnostics.Debug.WriteLine($"Chunk is found at {fsSrcImage.Position}");

                            // Chunk Data Lengh取得
                            var ChunkDataLengthBuffer = new byte[ChunkLengthSize];
                            fsSrcImage.Seek(-(ChunkLengthSize + ChunkTypeSize), SeekOrigin.Current); // Length, Chunk Type分戻る
                            fsSrcImage.Read(ChunkDataLengthBuffer, 0, ChunkLengthSize);
                            int chunkDataSize = BitConverter.ToInt32(BitConverter.IsLittleEndian ? ChunkDataLengthBuffer.Reverse().ToArray() : ChunkDataLengthBuffer, 0);


                            // Chunk Dataの取得
                            fsSrcImage.Seek(ChunkTypeSize, SeekOrigin.Current);
                            var ChunkDataBuffer = new byte[chunkDataSize];
                            fsSrcImage.Read(ChunkDataBuffer, 0, chunkDataSize);

                            var ChunkCRCBuffer = new byte[ChunkCRCSize];
                            fsSrcImage.Read(ChunkCRCBuffer, 0, ChunkCRCSize);

                            return (ChunkDataBuffer, chunk, ChunkCRCBuffer, chunkDataSize + ChunkTypeSize + ChunkLengthSize + ChunkCRCSize);
                        }
                        else
                        {
                            // Chunk Typeが一致しなかったら、巻き戻して再検索
                            fsSrcImage.Seek(-(ChunkType.Length - 1), SeekOrigin.Current);
                        }
                    }else if(b == IDAT[0])
                    {
                        var iDatChunkType = new Byte[ChunkTypeSize];
                        fsSrcImage.Seek(-1, SeekOrigin.Current);
                        fsSrcImage.Read(iDatChunkType, 0, ChunkTypeSize);

                        if (System.Linq.Enumerable.SequenceEqual(iDatChunkType.Take(ChunkTypeSize), IDAT))
                        {
                            System.Diagnostics.Debug.WriteLine($"IDAT is found.");
                            switch (type)
                            {
                                case ChunkType.iCCP:
                                case ChunkType.sRGB:
                                case ChunkType.pHYs:
                                    // これらのChukは、IDATの前にあるはずなので、IDatが見つかったらそこで終了
                                    throw new ArgumentException($"{type} is not found, before IDAT.");
                                default:
                                    // Chunk Typeが一致しなかったら、巻き戻して再検索
                                    fsSrcImage.Seek(-(ChunkTypeSize), SeekOrigin.Current);
                                    break;
                            }
                        }

                    }
                }
            }
            System.Diagnostics.Debug.WriteLine($"Chunk isn't found.");
            return (null, null, null, 0);
        }
    }
}
