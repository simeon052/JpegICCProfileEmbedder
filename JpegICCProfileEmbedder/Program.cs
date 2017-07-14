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
        static byte[] ICC_PROFILE_Identify = { 0x49, 0x43, 0x43, 0x5F, 0x50, 0x52, 0x4f , 0x46, 0x49, 0x4C, 0x45, 0x00, 0x01, 0x01}; // "ICC_PROFILE\0"

        private static bool InsertICCProfileInJpegFile(string srcPath, string ICCProfilePath)
        {
            // 
            // https://en.wikipedia.org/wiki/JPEG_File_Interchange_Format
            //
            int SOIandApp0SizeWithoutThumbnail = SOI.Length + App0Marker.Length + 16; // SOI + APP0 segment size without Thumbnail
            const int SegmentLengthSize = 2;

            bool ret = false;

            using (FileStream fsJpegImage = new FileStream(srcPath, FileMode.Open, FileAccess.ReadWrite))
            using (FileStream fsICCProfile = new FileStream(ICCProfilePath, FileMode.Open, FileAccess.ReadWrite))
            using (MemoryStream msJpegImageWithoutHeader = new MemoryStream())
            using (MemoryStream msICCProfile = new MemoryStream())
            {
                var originalFileSize = fsJpegImage.Length;
                var originalICCProfileSize = fsICCProfile.Length;
                if (fsICCProfile.Length > 0xFFFF) // HACK : サイズが、0xFFFFを超えた場合は、APP2 Segmentの分割が必要だが未対応
                {
                    throw new InvalidDataException($"{fsICCProfile.Name} is too large, this is not supported size right now.");
                }
                byte[] App0 = new byte[SOIandApp0SizeWithoutThumbnail];
                fsJpegImage.Read(App0, 0, SOIandApp0SizeWithoutThumbnail);
                var App0HeaderSize = BitConverter.ToInt16(App0.Skip(SOI.Length + App0Marker.Length).Take(SegmentLengthSize).Reverse().ToArray(), 0); // Reverse is requried for endian

                byte[] ICCProfileSizeBuffer = new byte[SegmentLengthSize];
                int ICCProfileSize = (int)(fsICCProfile.Length + ICC_PROFILE_Identify.Length);
                ICCProfileSizeBuffer[0] = BitConverter.GetBytes(ICCProfileSize)[1];
                ICCProfileSizeBuffer[1] = BitConverter.GetBytes(ICCProfileSize)[0];

                fsJpegImage.Seek(0, SeekOrigin.Begin);
                fsJpegImage.CopyTo(msJpegImageWithoutHeader);
                fsICCProfile.CopyTo(msICCProfile);
                var expectedFileSize = fsJpegImage.Length + App2Marker.Length + SegmentLengthSize + ICC_PROFILE_Identify.Length + fsICCProfile.Length;
                fsJpegImage.SetLength(expectedFileSize);

                fsJpegImage.Seek(SOI.Length + App0Marker.Length + App0HeaderSize, SeekOrigin.Begin);
                fsJpegImage.Write(App2Marker, 0, App2Marker.Length);
                fsJpegImage.Write(ICCProfileSizeBuffer, 0, SegmentLengthSize); // Set ICC profile size
                fsJpegImage.Write(ICC_PROFILE_Identify, 0, ICC_PROFILE_Identify.Length);
                fsJpegImage.Write(msICCProfile.GetBuffer(), 0, (int)msICCProfile.Length);
                var JpegImageDataLength = (int)(msJpegImageWithoutHeader.Length - (App0HeaderSize + SOI.Length + App0Marker.Length));
                fsJpegImage.Write(msJpegImageWithoutHeader.GetBuffer(), SOI.Length + App0Marker.Length + App0HeaderSize, JpegImageDataLength);

                System.Diagnostics.Debug.WriteLine($"{originalFileSize} + {originalICCProfileSize} = {originalFileSize + originalICCProfileSize} : {fsJpegImage.Length} - {expectedFileSize}");
                System.Diagnostics.Debug.WriteLine($"diff {(originalFileSize + originalICCProfileSize +  ICC_PROFILE_Identify.Length + SegmentLengthSize + App2Marker.Length) - originalFileSize}");
                System.Diagnostics.Debug.WriteLine($"diff {(originalFileSize + originalICCProfileSize + ICC_PROFILE_Identify.Length + SegmentLengthSize + App2Marker.Length) - fsJpegImage.Length}");
                ret = true;
            }
            return ret;
        }
 
       private static bool RestoreICCProfileFromJpegFile(string srcPath, string ICCProfilePath)
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

    }
}
