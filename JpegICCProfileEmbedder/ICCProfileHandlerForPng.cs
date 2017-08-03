using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        static int IHDR_ChunkSize = 0x13; // 
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


        public static bool Insert(string srcPath, string ICCProfilePath)
        {
            bool result = false;
            using (FileStream fsICCProfile = new FileStream(ICCProfilePath, FileMode.Open, FileAccess.Read))
            {
                var buffer = new byte[fsICCProfile.Length];
                fsICCProfile.Read(buffer, 0, buffer.Length);
                result = Insert(srcPath, buffer);
            }
            return result;
        }

        /// <summary>
        /// memory上のICC Profileを指定のJpegファイルに埋め込む
        /// </summary>
        /// <param name="srcPath"></param>
        /// <param name="ICCProfileBuffer"></param>
        /// <returns></returns>
        public static bool Insert(string srcPath, byte[] ICCProfileBuffer)
        {
            // HACK : ICC Color Profileのサイズが、0xFFFFを超えた場合は、APP2 Segmentの分割が必要だが未対応
            bool ret = false;

            //using (FileStream fsJpegImage = new FileStream(srcPath, FileMode.Open, FileAccess.ReadWrite))
            //using (MemoryStream msJpegImageWithoutHeader = new MemoryStream())
            //{
            //    var originalFileSize = fsJpegImage.Length;
            //    var originalICCProfileSize = ICCProfileBuffer.Length;
            //    if (ICCProfileBuffer.Length > 0xFFFF)
            //    {
            //        throw new InvalidDataException($"ICCProfile is too large, this is not supported size right now.");
            //    }

            //    // Thumbnailが保存されていた時のために、App0の実際のサイズを取得する。
            //    byte[] App0 = new byte[SOIandApp0SizeWithoutThumbnail];
            //    fsJpegImage.Read(App0, 0, SOIandApp0SizeWithoutThumbnail);
            //    int App0HeaderSize;
            //    if (BitConverter.IsLittleEndian)
            //    {
            //        App0HeaderSize = BitConverter.ToInt16(App0.Skip(SOI.Length + App0Marker.Length).Take(SegmentLengthSize).Reverse().ToArray(), 0); // Reverse is requried for endian
            //    }
            //    else
            //    {
            //        App0HeaderSize = BitConverter.ToInt16(App0.Skip(SOI.Length + App0Marker.Length).Take(SegmentLengthSize).ToArray(), 0);
            //    }


            //    // Size領域, Identifyを加えた、App2 Segmentのサイズを計算する
            //    byte[] App2SegmentSizeBuffer = new byte[SegmentLengthSize];
            //    int App2SegmentSize = (int)(ICCProfileBuffer.Length + ICC_PROFILE_Identify.Length + SegmentLengthSize);
            //    if (BitConverter.IsLittleEndian)
            //    {
            //        App2SegmentSizeBuffer[0] = BitConverter.GetBytes(App2SegmentSize)[1];
            //        App2SegmentSizeBuffer[1] = BitConverter.GetBytes(App2SegmentSize)[0];
            //    }
            //    else
            //    {
            //        App2SegmentSizeBuffer = BitConverter.GetBytes(App2SegmentSize);
            //    }

            //    fsJpegImage.Seek(0, SeekOrigin.Begin);

            //    // Memry Streamに保存する
            //    fsJpegImage.CopyTo(msJpegImageWithoutHeader);

            //    // Color profileを埋め込んだ後のファイルサイズにする。
            //    var expectedFileSize = fsJpegImage.Length + App2Marker.Length + SegmentLengthSize + ICC_PROFILE_Identify.Length + ICCProfileBuffer.Length;
            //    fsJpegImage.SetLength(expectedFileSize);

            //    // SOI + APP0は変更されていないので、そのままのこすため、Skip
            //    fsJpegImage.Seek(SOI.Length + App0Marker.Length + App0HeaderSize, SeekOrigin.Begin);
            //    // App2 Markerの書き込み
            //    fsJpegImage.Write(App2Marker, 0, App2Marker.Length);
            //    // App2 Segment sizeの書き込み
            //    fsJpegImage.Write(App2SegmentSizeBuffer, 0, SegmentLengthSize); // Set ICC profile size
            //    // ICC Profile Identifierの書き込み
            //    fsJpegImage.Write(ICC_PROFILE_Identify, 0, ICC_PROFILE_Identify.Length);
            //    // ICC Profileそのものの書き込み
            //    fsJpegImage.Write(ICCProfileBuffer, 0, (int)ICCProfileBuffer.Length);

            //    // MemoryにあるJpeg fileのSOI, App0以外の部分を書き込み
            //    var JpegImageDataLength = (int)(msJpegImageWithoutHeader.Length - (App0HeaderSize + SOI.Length + App0Marker.Length));
            //    fsJpegImage.Write(msJpegImageWithoutHeader.GetBuffer(), SOI.Length + App0Marker.Length + App0HeaderSize, JpegImageDataLength);

            //    System.Diagnostics.Debug.WriteLine($"Original file size : {originalFileSize}");
            //    System.Diagnostics.Debug.WriteLine($"ICC Profile file size : {originalICCProfileSize}");
            //    System.Diagnostics.Debug.WriteLine($"profile is embedded : {fsJpegImage.Length}");
            //    System.Diagnostics.Debug.WriteLine($"ICC profile Identify size : {ICC_PROFILE_Identify.Length}");
            //    System.Diagnostics.Debug.WriteLine($"App2 Marker size : {App2Marker.Length}");
            //    ret = true;
            //}
            return ret;
        }



        /// <summary>
        /// PNG から指定のChunkを取り出す。
        /// </summary>
        /// <param name="srcPath">Source file</param>
        /// <param name="ChunkDataPath">Chunk Dataを保存するファイル</param>
        /// <returns>true/false</returns>
        public static bool Restore(string srcPath, string ChunkDataPath)
        {
            try
            {
                using (var fsICCProfile = new FileStream(ChunkDataPath, FileMode.Create, FileAccess.Write))
                {
                    var (data, type, crc, size) = Restore(srcPath);
                    fsICCProfile.Write(data, 0, data.Length);
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
        };

        /// <summary>
        /// PNG から指定のChunkを取り出す。
        /// </summary>
        /// <param name="srcPath">Source file</param>
        /// <param name="type">Chunk Data</param>
        /// <returns>ChunkData, ChunkType, ChunkCRC</returns>
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
                    if(b == chunk[0]) // iCCP 1st byte
                    {
                        var ChunkType = new Byte[ chunk.Length];
                        fsSrcImage.Seek(-1, SeekOrigin.Current); //iCCPの一バイト分もどる
                        fsSrcImage.Read(ChunkType, 0, ChunkType.Length); // ICC ProfileのIdetifyを比較
                        if(System.Linq.Enumerable.SequenceEqual (ChunkType.Take(ChunkTypeSize), chunk))
                        {
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
                            fsSrcImage.Seek(-(ChunkType.Length), SeekOrigin.Current);
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
