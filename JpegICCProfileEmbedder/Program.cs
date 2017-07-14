using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JpegICCProfileEmbedder
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Count() > 1)
            {
                 InsertICCProfileInJpegFile(args[0], args[1]);
                //                RestoreICCProfileFromJpegFile(args[0], args[1]);
            }
        }

        static byte[] SOI = { 0xFF, 0xD8 }; // Start of image segment(SOI)
        static byte[] App0Marker = { 0xFF, 0xE0 }; // APP0 marker
        static byte[] App2Marker = { 0xFF, 0xE2 }; // APP2 marker
        static byte[] JFIFIdentify = { 0x4A, 0x46, 0x49, 0x46, 0x00 }; // "JFIF\0"
        static byte[] ICC_PROFILE_Identify = { 0x49, 0x43, 0x43, 0x5F, 0x50, 0x52, 0x4f , 0x46, 0x49, 0x4C, 0x45, 0x00, 0x01, 0x01 }; // "ICC_PROFILE\0"

        private static bool InsertICCProfileInJpegFile(string srcPath, string ICCProfilePath)
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

        private static bool InsertICCProfileInJpegFile(string srcPath, byte[] ICCProfileBuffer)
        {
            // 
            // https://en.wikipedia.org/wiki/JPEG_File_Interchange_Format
            //
            int SOIandApp0SizeWithoutThumbnail = SOI.Length + App0Marker.Length + 16; // SOI + APP0 segment size without Thumbnail
            const int SegmentLengthSize = 2;

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
                if (BitConverter.IsLittleEndian) {
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
#region RestoreICCProfileByNativeAPI
        private static bool RestoreICCProfileFromJpegFileByNativeApi(string srcPath, string ICCProfilePath)
        {
            using (var img = new Bitmap(srcPath))
            {
                const int tPropertTagICCProfile = 0x08773;
                if (img.PropertyIdList.Contains<int>(tPropertTagICCProfile))
                {
                    var iccproperty = img.GetPropertyItem(tPropertTagICCProfile); // PropertyTagICCProfile
                    if (iccproperty != null)
                    {
                        using (var fs = new FileStream(ICCProfilePath, FileMode.CreateNew, FileAccess.ReadWrite))
                        {
                            fs.Write(iccproperty.Value, 0, iccproperty.Len);
                        }
                    }
                    else
                    {
                        throw new InvalidDataException($"{srcPath} doesn't have a ICC color profile");
                    }
                }
            }
            return true;
        }
#endregion


    }
}
