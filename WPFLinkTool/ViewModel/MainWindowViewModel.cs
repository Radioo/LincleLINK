using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Xml;
using System.Xml.Serialization;
using static WPFLinkTool.Global;

namespace WPFLinkTool
{
    public class MainWindowViewModel : ViewModelBase
    {
        public SharedViewModel Shared;

        public Logger Loggerr;

        public ObservableCollection<string> LogEntries
        {
            get
            {
                return _logEntries;
            }
            set
            {
                _logEntries = value;
                OnPropertyChanged(nameof(LogEntries));
            }
        }
        private ObservableCollection<string> _logEntries;

        private string _dBSize;
        public string DBSize
        {
            get
            {
                return _dBSize;
            }
            set
            {
                _dBSize = value;
                OnPropertyChanged(nameof(DBSize));
            }
        }
        private string _sumSize;
        public string SumSize
        {
            get
            {
                return _sumSize;
            }
            set
            {
                _sumSize = value;
                OnPropertyChanged(nameof(SumSize));
            }
        }
        private bool _linkButtonEnabled = false;
        public bool LinkButtonEnabled
        {
            get
            {
                return _linkButtonEnabled;
            }
            set
            {
                _linkButtonEnabled = value;
                OnPropertyChanged(nameof(LinkButtonEnabled));
            }
        }
        private bool _uIEnabled = true;
        public bool UIEnabled
        {
            get
            {
                return _uIEnabled;
            }
            set
            {
                _uIEnabled = value;
                OnPropertyChanged(nameof(UIEnabled));
            }
        }
        private double _progress;
        public double Progress
        {
            get
            {
                return _progress;
            }
            set
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }
        private string _headerText;
        public string HeaderText
        {
            get
            {
                return _headerText;
            }
            set
            {
                _headerText = value;
                OnPropertyChanged(nameof(HeaderText));               
            }
        }
        private DataInstance _selectedInstance;
        public DataInstance SelectedInstance
        {
            get
            {
                return _selectedInstance;
            }
            set
            {
                _selectedInstance = value;
                OnPropertyChanged(nameof(SelectedInstance));
                LinkButtonEnabled = true;
            }
        }

        public ObservableCollection<DataInstance> InstanceList
        {
            get
            {
                ObservableCollection<DataInstance> list = new();
                foreach (var instance in Shared.Info.InstanceList)
                {
                    list.Add(instance);
                }
                return list;
            }
        }

        public RelayCommand OpenAddInstanceWindow { get; private set; }
        public RelayCommand SelectedInstanceCommand { get; private set; }
        public RelayCommand DeleteInstanceCommand { get; private set; }
        public ICommand MakeHardLinksCommand { get; private set; }
        public ICommand DeleteUnusedCommand { get; private set; }

        public ScrollViewer Scroller;

        public MainWindowViewModel(SharedViewModel shared, ScrollViewer scroller)
        {
            if (!Directory.Exists(dbDir))
                Directory.CreateDirectory(dbDir);
            Shared = shared;
            Scroller = scroller;
            Loggerr = new(Scroller);
            OpenAddInstanceWindow = new(OpenInstanceWindow);
            DeleteInstanceCommand = new(DeleteInstance);
            MakeHardLinksCommand = new MakeHardLinksCommand(this, (ex) => Loggerr.Log(ex.Message));
            DeleteUnusedCommand = new DeleteUnusedCommand(this, (ex) => Loggerr.Log(ex.Message));
            LoadDBInfo();
            Task.Run(() => UpdateDBSize());
            Task.Run(() => UpdateSumSize());
            //HeaderText = "Loading finished, probably... Have a nice day!";
            LogEntries = Loggerr.LogEntries;
            Loggerr.Log("Loading finished, probably... Have a nice day!");
        }

        private void LoadDBInfo()
        {
            if (!File.Exists(DBInfoPath))
            {
                Shared.Info = new();
            }
            else
            {
                XmlSerializer serializer = new(typeof(DBInfo));
                FileStream fs = new(DBInfoPath, FileMode.Open);
                XmlReader reader = XmlReader.Create(fs);
                Shared.Info = (DBInfo)serializer.Deserialize(reader);
            }
        }

        public void OpenInstanceWindow(object o)
        {
            AddInstanceDialog dialog = new(Shared);
            dialog.ShowDialog();
            OnPropertyChanged(nameof(InstanceList));
            LinkButtonEnabled = false;
            Task.Run(() => UpdateDBSize());
            Task.Run(() => UpdateSumSize());
        }      

        public void DeleteInstance(object o)
        {
            var prompt = MessageBox.Show($"Delete {SelectedInstance.InstanceName}? This will not delete the files.", "Delete instance", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (prompt == DialogResult.Yes)
            {
                Shared.Info.InstanceList.Remove(SelectedInstance);
                Shared.Info.SaveToXml();
                LinkButtonEnabled = false;
                UpdateSumSize();
                OnPropertyChanged(nameof(InstanceList));
                Loggerr.Log("Deleting instance operation finished.");
            }
        }
        public void UpdateDBSize()
        {
            DirectoryInfo dir = new(dbDir);
            long size = 0;
            foreach (var file in dir.GetFiles())
            {
                size += file.Length;
            }
            DBSize = ReadableSize(size);
        }
        public void UpdateSumSize()
        {
            long size = 0;
            foreach (var instance in Shared.Info.InstanceList)
            {
                size += instance.Size;
            }
            SumSize = ReadableSize(size);
        }
    }
}
