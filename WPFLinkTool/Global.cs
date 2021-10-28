using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WPFLinkTool
{
    public class Global
    {
        //public static string curDir = @"F:\Torrents\bemaniso.ws\HardLinkUI";
        //public static string curDir = @"C:\Tools and stuff\WPFLinkTool";
        public static string curDir = Directory.GetCurrentDirectory();
        public static string dbDir = curDir + @"\DB";
        public static string xmlDir = curDir + @"\XML";
        public static string DBInfoPath = curDir + @"\DBInfo.xml";

        public static string ReadableSize(long size)
        {
            if (size < 1024f)
            {
                return $"{size} B";
            }
            if (size > 1024f && size < 1048576f)
            {
                return $"{Math.Round(size / 1024f, 2)} KB";
            }
            if (size > 1048576f && size < 1073741824f)
            {
                return $"{Math.Round(size / 1048576f, 2)} MB";
            }
            if (size > 1073741824f)
            {
                return $"{Math.Round(size / 1073741824f, 2)} GB";
            }
            else return "what?";
        }
    }


}
