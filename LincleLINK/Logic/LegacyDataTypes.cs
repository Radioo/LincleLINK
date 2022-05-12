using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace LincleLINK
{
    public class DBInfo
    {
        public List<DataInstance> InstanceList { get; set; }
        public int InstanceCount => InstanceList.Count;
        public long DBSize
        {
            get
            {
                long size = 0;
                DirectoryInfo dir = new("");
                foreach (var file in dir.GetFiles())
                {
                    size += file.Length;
                }
                return size;
            }
        }

        public long IndividualSize
        {
            get
            {
                long size = 0;
                foreach (DataInstance instance in InstanceList)
                {
                    size += instance.Size;
                }
                return size;

            }
        }

        public DBInfo()
        {
            if (InstanceList == null)
                InstanceList = new();
        }

        public void SaveToXml()
        {
            XmlSerializer serializer = new(typeof(DBInfo));
            //TextWriter writer = new StreamWriter(DBInfoPath);
            //serializer.Serialize(writer, this);
        }
        public void UpdateDBSize()
        {

        }
    }

    public class DataInstance
    {
        public string InstanceName { get; set; }
        public List<InstanceFileInfo> InstanceFiles { get; set; }
        public int Entries { get; set; }
        public long Size { get; set; }
        public string PrettySize;

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

    public class InstanceFileInfo
    {
        public string OriginalFileName { get; set; }
        public string HashedFileName { get; set; }
        public string Location { get; set; }
        public long SizeBytes { get; set; }
        public string PrettySize;

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
