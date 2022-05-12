using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LincleLINK
{
    public class AddInstanceWindowLogic : ViewModelBase
    {
        private string _instanceName;
        public string InstanceName
        {
            get { return _instanceName; }
            set
            {
                _instanceName = value;
                OnPropertyChanged(nameof(InstanceName));
            }
        }

        private string _dataPath;
        public string DataPath
        {
            get { return _dataPath; }
            set
            {
                _dataPath = value;
                OnPropertyChanged(nameof(DataPath));
            }
        }

        private bool _isCopyChecked;
        public bool IsCopyChecked
        {
            get { return _isCopyChecked; }
            set
            {
                _isCopyChecked = value;
                OnPropertyChanged(nameof(IsCopyChecked));
            }
        }

        private bool _isMoveChecked;
        public bool IsMoveChecked
        {
            get { return _isMoveChecked; }
            set
            {
                _isMoveChecked = value;
                OnPropertyChanged(nameof(IsMoveChecked));
            }
        }

        private ObservableCollection<string> _logList;
        public ObservableCollection<string> LogList
        {
            get { return _logList; }
            set
            {
                _logList = value;
            }
        }

        private double ProgressStep;
        private double _progress;
        public double Progress
        {
            get { return _progress; }
            set
            {
                _progress = value;
                OnPropertyChanged(nameof(Progress));
            }
        }

        private bool _uIEnabled;
        public bool UIEnabled
        {
            get { return _uIEnabled; }
            set
            {
                _uIEnabled = value;
                OnPropertyChanged(nameof(UIEnabled));
            }
        }

        public RelayCommand MakeInstanceCommand { get; set; }
        public RelayCommand BrowseCommand { get; set; }

        public AddInstanceWindowLogic()
        {
            MakeInstanceCommand = new RelayCommand(MakeInstance);
            BrowseCommand = new RelayCommand(BrowseForFolder);
            IsCopyChecked = true;
            Progress = 0;
            LogList = new();
            UIEnabled = true;
        }

        public void BrowseForFolder(object o)
        {
            FolderBrowserDialog dialog = new();
            dialog.ShowDialog();
            DataPath = dialog.SelectedPath;
            dialog.Dispose();
        }

        public void MakeInstance(object o)
        {
            LogList.Add("Validating...");
            if (ValidateInstance())
            {
                LogList.Add("Good to go!");
                try
                {
                    CreateInstance();
                }
                catch (Exception e)
                {
                    LogList.Add(e.Message);
                }
            }
        }

        public bool ValidateInstance()
        {
            if (string.IsNullOrWhiteSpace(InstanceName))
            {
                System.Windows.Forms.MessageBox.Show("Instance name cannot be empty.",
                    "Empty instance name", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (string.IsNullOrWhiteSpace(DataPath))
            {
                System.Windows.Forms.MessageBox.Show("Data path cannot be empty.",
                    "Empty data path", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            string[] existingInstances = Directory.GetFiles(MainWindowLogic.instanceDir, "*.json");
            foreach (string instance in existingInstances)
            {
                if (InstanceName.ToLower() == Path.GetFileNameWithoutExtension(instance).ToLower())
                {
                    System.Windows.Forms.MessageBox.Show("Instance with this name already exists.",
                        "Duplicate instance name", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            foreach (char c in InstanceName)
            {
                if (Path.GetInvalidFileNameChars().Contains(c))
                {
                    System.Windows.Forms.MessageBox.Show("Instance name contains invalid characters.",
                        "Invalid characters", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            foreach (char c in DataPath)
            {
                if (Path.GetInvalidPathChars().Contains(c))
                {
                    System.Windows.Forms.MessageBox.Show("Data path contains invalid characters",
                        "Invalid characters", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            return true;
        }

        public async void CreateInstance()
        {
            UIEnabled = false;
            Progress = 0;
            List<InstanceFile> iFiles = new();
            List<string> directories = new();
            long totalSize = 0;
            int alreadyExists = 0;
            long newsize = 0;

            DirectoryInfo dInfo = new(DataPath);
            var allFiles = dInfo.GetFiles("*", SearchOption.AllDirectories);
            ProgressStep = 100D / (allFiles.Length + 2);
            LogList.Add("Hashing...");
            foreach (var file in allFiles)
            {
                string relativePath = Path.GetRelativePath(DataPath, Path.GetDirectoryName(file.FullName));
                if (relativePath == ".")
                {
                    relativePath = string.Empty;
                }
                string hash = await Task.Run(() => GetMD5Checksum(file.FullName));
                string hashedFileName = hash + file.Extension;
                //string hashedFileName = "test";
                LogList.Add($"Hashing {file.FullName}");
                if (IsCopyChecked == true && IsMoveChecked == false)
                {
                    if (!File.Exists(Path.Combine(MainWindowLogic.dbDir, hashedFileName)))
                    {
                        await Task.Run(() => File.Copy(file.FullName, Path.Combine(MainWindowLogic.dbDir, hashedFileName)));
                        newsize += file.Length;
                    }
                    else alreadyExists++;
                }
                else if (IsCopyChecked == false && IsMoveChecked == true)
                {
                    if (!File.Exists(Path.Combine(MainWindowLogic.dbDir, hashedFileName)))
                    {
                        await Task.Run(() => File.Move(file.FullName, Path.Combine(MainWindowLogic.dbDir, hashedFileName)));
                        newsize += file.Length;
                    }
                    else alreadyExists++;
                }

                InstanceFile iFile = new(file.Name, relativePath, file.Length, hashedFileName);
                iFiles.Add(iFile);
                totalSize += file.Length;
                Progress += ProgressStep;
            }
            LogList.Add("Collecting directories...");
            foreach (var dir in Directory.GetDirectories(DataPath, "*", SearchOption.AllDirectories))
            {
                directories.Add(Path.GetRelativePath(DataPath, dir));
            }
            Progress += ProgressStep;

            Instance instance = new(iFiles, directories, totalSize, allFiles.Length, InstanceName);
            var options = new JsonSerializerOptions { WriteIndented = true };
            LogList.Add("Saving instance to file...");
            instance.SaveToFile(options);
            Progress += ProgressStep;
            LogList.Add($"Instance added. {alreadyExists} files already exist. {Instance.ReadableSize(newsize)} added to the db.");
            UIEnabled = true;
        }

        public static string GetMD5Checksum(string fileName)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(fileName);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "");
        }
    }
}
