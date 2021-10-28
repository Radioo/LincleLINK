using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using static WPFLinkTool.Global;

namespace WPFLinkTool
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
                DirectoryInfo dir = new(dbDir);
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
            TextWriter writer = new StreamWriter(DBInfoPath);
            serializer.Serialize(writer, this);
        }
        public void UpdateDBSize()
        {
            
        }
    }
}
