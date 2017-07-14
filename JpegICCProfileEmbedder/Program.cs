using System;
using System.Collections.Generic;
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
            }
        }

        static byte[] SOI = { 0xFF, 0xD8 }; // Start of image segment(SOI)
        static byte[] App0Marker = { 0xFF, 0xE0 };// APP0 marker
        static byte[] App2Marker = { 0xFF, 0xE2 }; // APP2 marker
        static byte[] JFIFIdentify = { 0x4A, 0x46, 0x49, 0x46, 0x00 }; // "JFIF\0"

        private static bool InsertICCProfileInJpegFile(string srcPath, string ICCProfilePath)
        {
            // 
            // https://en.wikipedia.org/wiki/JPEG_File_Interchange_Format
            //
            int SOIandApp0Size = SOI.Length + App0Marker.Length + 16; // SOI + APP0 segment size without Thumbnail
            const int SegmentLengthSize = 2;

            bool ret = false;

            using (FileStream fsJpegImage = new FileStream(srcPath, FileMode.Open, FileAccess.ReadWrite))
            using (FileStream fsICCProfile = new FileStream(ICCProfilePath, FileMode.Open, FileAccess.ReadWrite))
            using (MemoryStream msJpegImageWithoutHeader = new MemoryStream())
            using (MemoryStream msICCProfile = new MemoryStream())
            {
                if (fsICCProfile.Length > 0xFFFF) // HACK : サイズが、0xFFFFを超えた場合は、APP2 Segmentの分割が必要だが未対応
                {
                    throw new InvalidDataException($"{fsICCProfile.Name} is too large, this is not supported size right now.");
                }
                byte[] App0 = new byte[SOIandApp0Size];
                fsJpegImage.Read(App0, 0, SOIandApp0Size);
                var App0HEaderSize = BitConverter.ToInt16(App0.Skip(SOI.Length + App0Marker.Length).Take(SegmentLengthSize).Reverse().ToArray(), 0); // Reverse is requried for endian

                byte[] ICCProfileSize = new byte[SegmentLengthSize];
                ICCProfileSize[0] = BitConverter.GetBytes(fsICCProfile.Length)[1];
                ICCProfileSize[1] = BitConverter.GetBytes(fsICCProfile.Length)[0];

                fsJpegImage.CopyTo(msJpegImageWithoutHeader);
                fsICCProfile.CopyTo(msICCProfile);
                fsJpegImage.Seek(0, 0);
                fsJpegImage.Write(App0, 0, App0.Length);
                fsJpegImage.Write(App2Marker, 0, App2Marker.Length);
                fsJpegImage.Write(ICCProfileSize, 0, SegmentLengthSize); // Set ICC profile size
                fsJpegImage.Write(msICCProfile.GetBuffer(), 0, (int)msICCProfile.Length);
                fsJpegImage.Write(msJpegImageWithoutHeader.GetBuffer(), 0, (int)(msJpegImageWithoutHeader.Length - App0HEaderSize + SOI.Length + App0Marker.Length));
            }
            return ret;
        }

    }
}
