using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static WPFLinkTool.Global;

namespace WPFLinkTool
{
    public class DataInstance
    {
        public string InstanceName { get; set; }
        public List<InstanceFileInfo> InstanceFiles { get; set; }
        public int Entries { get; set; }
        public long Size { get; set; }
        public string PrettySize => ReadableSize(Size);

        public DataInstance(string name, List<InstanceFileInfo> fileInfos, int entries, long size)
        {
            InstanceName = name;
            InstanceFiles = fileInfos;
            Entries = entries;
            Size = size;
        }
        
        public DataInstance()
        {

        }
    }
}
