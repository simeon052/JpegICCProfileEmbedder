using System;
using System.IO;
using System.Linq;

namespace JpegICCProfileEmbedder
{
    public class ICCProfileHandlerForJpeg
    {
        static byte[] SOI = { 0xFF, 0xD8 }; // Start of image segment(SOI)
        static byte[] App0Marker = { 0xFF, 0xE0 }; // APP0 marker
        static byte[] App1Marker = { 0xFF, 0xE1 }; // APP1 marker
        static byte[] App2Marker = { 0xFF, 0xE2 }; // APP2 marker
        static byte[] JFIFIdentify = { 0x4A, 0x46, 0x49, 0x46, 0x00 }; // "JFIF\0"
        static byte[] ICC_PROFILE_Identify = { 0x49, 0x43, 0x43, 0x5F, 0x50, 0x52, 0x4f, 0x46, 0x49, 0x4C, 0x45, 0x00, 0x01, 0x01 }; // "ICC_PROFILE\0"
        static int SOIandApp0SizeWithoutThumbnail = SOI.Length + App0Marker.Length + 16; // SOI + APP0 segment size without Thumbnail
        static int SegmentLengthSize = 2; // Segment size is 16 bytes

        //
        public static bool InsertICCProfileInJpegFile(string srcPath, string ICCProfilePath)
        {
            bool result = false;
            using (FileStream fsICCProfile = new FileStream(ICCProfilePath, FileMode.Open, FileAccess.Read))
            {
                var buffer = new byte[fsICCProfile.Length];
                fsICCProfile.Read(buffer, 0, buffer.Length);
                result = InsertICCProfileInJpegFile(srcPath, buffer);
            }
            return result;
        }

        /// <summary>
        /// memory上のICC Profileを指定のJpegファイルに埋め込む
        /// </summary>
        /// <param name="srcPath"></param>
        /// <param name="ICCProfileBuffer"></param>
        /// <returns></returns>
        public static bool InsertICCProfileInJpegFile(string srcPath, byte[] ICCProfileBuffer)
        {
            // 
            // https://en.wikipedia.org/wiki/JPEG_File_Interchange_Format
            //
            bool ret = false;

            using (FileStream fsJpegImage = new FileStream(srcPath, FileMode.Open, FileAccess.ReadWrite))
            using (MemoryStream msJpegImageWithoutHeader = new MemoryStream())
            {
                var originalFileSize = fsJpegImage.Length;
                var originalICCProfileSize = ICCProfileBuffer.Length;
                if (ICCProfileBuffer.Length > 0xFFFF) // HACK : サイズが、0xFFFFを超えた場合は、APP2 Segmentの分割が必要だが未対応
                {
                    throw new InvalidDataException($"ICCProfile is too large, this is not supported size right now.");
                }

                // Thumbnailが保存されていた時のために、App0の実際のサイズを取得する。
                byte[] App0 = new byte[SOIandApp0SizeWithoutThumbnail];
                fsJpegImage.Read(App0, 0, SOIandApp0SizeWithoutThumbnail);
                int App0HeaderSize;
                if (BitConverter.IsLittleEndian)
                {
                    App0HeaderSize = BitConverter.ToInt16(App0.Skip(SOI.Length + App0Marker.Length).Take(SegmentLengthSize).Reverse().ToArray(), 0); // Reverse is requried for endian
                }
                else
                {
                    App0HeaderSize = BitConverter.ToInt16(App0.Skip(SOI.Length + App0Marker.Length).Take(SegmentLengthSize).ToArray(), 0);
                }


                // Size領域, Identifyを加えた、App2 Segmentのサイズを計算する
                byte[] App2SegmentSizeBuffer = new byte[SegmentLengthSize];
                int App2SegmentSize = (int)(ICCProfileBuffer.Length + ICC_PROFILE_Identify.Length + SegmentLengthSize);
                if (BitConverter.IsLittleEndian)
                {
                    App2SegmentSizeBuffer[0] = BitConverter.GetBytes(App2SegmentSize)[1];
                    App2SegmentSizeBuffer[1] = BitConverter.GetBytes(App2SegmentSize)[0];
                }
                else
                {
                    App2SegmentSizeBuffer = BitConverter.GetBytes(App2SegmentSize);
                }

                fsJpegImage.Seek(0, SeekOrigin.Begin);

                // Memry Streamに保存する
                fsJpegImage.CopyTo(msJpegImageWithoutHeader);

                // Color profileを埋め込んだ後のファイルサイズにする。
                var expectedFileSize = fsJpegImage.Length + App2Marker.Length + SegmentLengthSize + ICC_PROFILE_Identify.Length + ICCProfileBuffer.Length;
                fsJpegImage.SetLength(expectedFileSize);

                // SOI + APP0は変更されていないので、そのままのこすため、Skip
                fsJpegImage.Seek(SOI.Length + App0Marker.Length + App0HeaderSize, SeekOrigin.Begin);
                // App2 Markerの書き込み
                fsJpegImage.Write(App2Marker, 0, App2Marker.Length);
                // App2 Segment sizeの書き込み
                fsJpegImage.Write(App2SegmentSizeBuffer, 0, SegmentLengthSize); // Set ICC profile size
                // ICC Profile Identifierの書き込み
                fsJpegImage.Write(ICC_PROFILE_Identify, 0, ICC_PROFILE_Identify.Length);
                // ICC Profileそのものの書き込み
                fsJpegImage.Write(ICCProfileBuffer, 0, (int)ICCProfileBuffer.Length);

                // MemoryにあるJpeg fileのSOI, App0以外の部分を書き込み
                var JpegImageDataLength = (int)(msJpegImageWithoutHeader.Length - (App0HeaderSize + SOI.Length + App0Marker.Length));
                fsJpegImage.Write(msJpegImageWithoutHeader.GetBuffer(), SOI.Length + App0Marker.Length + App0HeaderSize, JpegImageDataLength);

                System.Diagnostics.Debug.WriteLine($"Original file size : {originalFileSize}");
                System.Diagnostics.Debug.WriteLine($"ICC Profile file size : {originalICCProfileSize}");
                System.Diagnostics.Debug.WriteLine($"profile is embedded : {fsJpegImage.Length}");
                System.Diagnostics.Debug.WriteLine($"ICC profile Identify size : {ICC_PROFILE_Identify.Length}");
                System.Diagnostics.Debug.WriteLine($"App2 Marker size : {App2Marker.Length}");
                ret = true;
            }
            return ret;
        }

        /// <summary>
        /// Jpegファイルから、ICC Profileを探して、ファイルとして保存する。
        /// </summary>
        /// <param name="srcPath"></param>
        /// <param name="ICCProfilePath"></param>
        /// <returns></returns>
        public static bool RestoreICCProfileFromJpegFile(string srcPath, string ICCProfilePath)
        {
            using (var fsICCProfile = new FileStream(ICCProfilePath, FileMode.Create, FileAccess.Write))
            {
                var buffer = RestoreICCProfileFromJpegFile(srcPath);
                fsICCProfile.Write(buffer, 0, buffer.Length);
            }

            return true;
        }

        /// <summary>
        /// Jpegファイルから、ICC Profileを探して取り出す
        /// </summary>
        /// <param name="srcPath"></param>
        /// <returns></returns>
        public static byte[] RestoreICCProfileFromJpegFile(string srcPath)
        {
            //
            // https://en.wikipedia.org/wiki/JPEG_File_Interchange_Format
            //

            using (FileStream fsJpegImage = new FileStream(srcPath, FileMode.Open, FileAccess.ReadWrite))
            {
                var originalFileSize = fsJpegImage.Length;
                int b;
                byte[] ICCProfileBuffer = null;

                // TODO : 複数Segmentに分割されたICC Profileには未対応
                while((b = fsJpegImage.ReadByte()) > -1 )
                {
                    if(b == App2Marker[1]) // App2 segment Markerの2byte目が一致するまで一バイトごと読む
                    {
                        var bd = new Byte[ ICC_PROFILE_Identify.Length];
                        fsJpegImage.Seek(SegmentLengthSize, SeekOrigin.Current); // Segment lengthの格納場所を飛ばす
                        fsJpegImage.Read(bd, 0, bd.Length); // ICC ProfileのIdetifyを比較
                        if(System.Linq.Enumerable.SequenceEqual (bd.Take (ICC_PROFILE_Identify.Length), ICC_PROFILE_Identify))
                        {
                            System.Diagnostics.Debug.WriteLine($"ICC_PROFILE is found at {fsJpegImage.Position}");

                            // ICC Profileサイズの取得
                            const int ICCProfileSizeLength = sizeof(Int32);
                            var ICCProfileSizeBuffer = new byte[ICCProfileSizeLength];
                            fsJpegImage.Read(ICCProfileSizeBuffer, 0, ICCProfileSizeLength);
                            int ICCProfileSize = BitConverter.ToInt32(BitConverter.IsLittleEndian ? ICCProfileSizeBuffer.Reverse().ToArray() : ICCProfileSizeBuffer, 0);


                            // ICC Profileの取得
                            fsJpegImage.Seek(-ICCProfileSizeLength, SeekOrigin.Current);
                            ICCProfileBuffer = new byte[ICCProfileSize];
                            fsJpegImage.Read(ICCProfileBuffer, 0, ICCProfileSize);
                            return ICCProfileBuffer;
                        }
                        else
                        {
                            // App2でもICC_PROFILEのIdentifyが一致しなかったら、巻き戻して再検索
                            fsJpegImage.Seek(-(SegmentLengthSize + ICC_PROFILE_Identify.Length), SeekOrigin.Current);
                        }
                    }

                }

            }


            return null;
        }
    }
}
