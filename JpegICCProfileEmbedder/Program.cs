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
                // ICCProfileHandlerForJpeg.InsertICCProfileInJpegFile(args[0], args[1]);
                //                RestoreICCProfileFromJpegFile(args[0], args[1]);
                //   ICCProfileHandlerForJpeg.RestoreICCProfileFromJpegFile(args[0], @"Q:\Data\Projects\JpegHeader\restored.icc" );
                var (data, type, crc, size) = PngChunkHandler.Restore(args[0], PngChunkHandler.ChunkType.pHYs);
                var (dpix, dpiy) = PngChunkHandler.GetDPIformPHYS(data);
                ;
            }
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
