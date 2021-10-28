using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static WPFLinkTool.Global;

namespace WPFLinkTool
{
    public class InstanceFileInfo
    {
        public string OriginalFileName { get; set; }
        public string HashedFileName { get; set; }
        public string Location { get; set; }
        public long SizeBytes { get; set; }
        public string PrettySize => ReadableSize(SizeBytes);

        public InstanceFileInfo(string orgFName, string hashFName, string loc, long size)
        {
            OriginalFileName = orgFName;
            HashedFileName = hashFName;
            Location = loc;
            SizeBytes = size;
        }
        public InstanceFileInfo()
        {

        }
    }
}
