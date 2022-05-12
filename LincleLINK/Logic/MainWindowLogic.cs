using BencodeNET.Parsing;
using BencodeNET.Torrents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace LincleLINK
{
    public class MainWindowLogic : ViewModelBase
    {
        //public static readonly string currentDir = Directory.GetCurrentDirectory();
        public static readonly string currentDir = @"F:\LincleLINK";
        public static readonly string dbDir = Path.Join(currentDir, "db");
        public static readonly string instanceDir = Path.Join(currentDir, "instance");

        private ObservableCollection<InstanceListEntry> _instanceList;
        public ObservableCollection<InstanceListEntry> InstanceList
        {
            get { return _instanceList; }
            set
            {
                _instanceList = value;
                OnPropertyChanged(nameof(InstanceList));
            }
        }

        private InstanceListEntry _selectedInstance;
        public InstanceListEntry SelectedInstance
        {
            get { return _selectedInstance; }
            set
            {
                _selectedInstance = value;
                OnPropertyChanged(nameof(SelectedInstance));
                LogList.Add($"Selected {SelectedInstance.InstanceName}");
            }
        }

        private ObservableCollection<string> _logList;
        public ObservableCollection<string> LogList
        {
            get { return _logList; }
            set
            {
                _logList = value;
                Controls.LogScrollViewer.ScrollToBottom();
            }
        }

        public double ProgressStep;
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

        private bool _isFree;
        public bool IsFree
        {
            get { return _isFree; }
            set
            {
                _isFree = value;
                OnPropertyChanged(nameof(IsFree));
            }
        }

        private List<string> _matchedList;
        public List<string> MatchedList
        {
            get { return _matchedList; }
            set
            {
                _matchedList = value;
                OnPropertyChanged(nameof(MatchedList));
                Controls.MatchedFilesScroller.ScrollToBottom();
            }
        }

        private string _relativePath;
        public string RelativePath
        {
            get { return _relativePath; }
            set
            {
                _relativePath = value;
                OnPropertyChanged(nameof(RelativePath));
            }
        }

        private string _torrentLinkTarget;
        public string TorrentLinkTarget
        {
            get { return _torrentLinkTarget; }
            set
            {
                _torrentLinkTarget = value;
                OnPropertyChanged(nameof(TorrentLinkTarget));
            }
        }

        private string _torrentFilePath;
        public string TorrentFilePath
        {
            get { return _torrentFilePath; }
            set
            {
                _torrentFilePath = value;
                OnPropertyChanged(nameof(TorrentFilePath));
            }
        }

        private string _torrentDownloadPath;
        public string TorrentDownloadPath
        {
            get { return _torrentDownloadPath; }
            set
            {
                _torrentDownloadPath = value;
                OnPropertyChanged(nameof(TorrentDownloadPath));
            }
        }

        private bool _didCheckFiles;
        public bool DidCheckFiles
        {
            get { return _didCheckFiles; }
            set
            {
                _didCheckFiles = value;
                OnPropertyChanged(nameof(DidCheckFiles));
            }
        }

        private bool _didCheckPieces;
        public bool DidCheckPieces
        {
            get { return _didCheckPieces; }
            set
            {
                _didCheckPieces = value;
                OnPropertyChanged(nameof(DidCheckPieces));
            }
        }

        private string _dbSize;
        public string DBSize
        {
            get { return _dbSize; }
            set
            {
                _dbSize = value;
                OnPropertyChanged(nameof(DBSize));
            }
        }

        private string _savings;
        public string Savings
        {
            get { return _savings; }
            set
            {
                _savings = value;
                OnPropertyChanged(nameof(Savings));
            }
        }

        public SynchronizationContext UIContext { get; set; }
        public Dictionary<PassedFileInfo, HashSet<long>>? FilePieceMap { get; set; }
        public List<long>? BadPieces { get; set; }
        public MainWindowControls Controls { get; set; }
        public RelayCommand OpenAddInstanceCommand { get; set; }
        public RelayCommand DeleteInstanceCommand { get; set; }
        public RelayCommand CreateHardLinksCommand { get; set; }
        public RelayCommand CheckUnusedCommand { get; set; }
        public RelayCommand ImportLegacyCommand { get; set; }
        public AsyncRelayCommand CheckFilesCommand { get; set; }
        public AsyncRelayCommand CheckPiecesCommand { get; set; }
        public RelayCommand FolderBrowseTorrentFileCommand { get; set; }
        public RelayCommand FolderBrowseTorrentDLPathCommand { get; set; }
        public AsyncRelayCommand LinkToTorrentCommand { get; set; }

        public MainWindowLogic(MainWindowControls controls, SynchronizationContext uicontext)
        {
            CheckDirs();
            UIContext = uicontext;
            Controls = controls;
            LogList = new();
            OpenAddInstanceCommand = new(OpenAddInstanceWindow);
            DeleteInstanceCommand = new(DeleteInstance, CanDoActionWithInstance);
            CreateHardLinksCommand = new(CreateHardLinks, CanDoActionWithInstance);
            CheckUnusedCommand = new(CheckForUnusedFiles);
            ImportLegacyCommand = new(ImportLegacyInstances);
            CheckFilesCommand = new(CheckFiles, (ex) => LogList.Add(ex.Message));
            CheckPiecesCommand = new(CheckPieces, (ex) => LogList.Add(ex.Message));
            FolderBrowseTorrentFileCommand = new(FolderBrowseTorrentPath);
            FolderBrowseTorrentDLPathCommand = new(FolderBrowserTorrentDLPath);
            LinkToTorrentCommand = new(LinkToTorrent, (ex) => LogList.Add(ex.Message));
            MatchedList = new();
            RelativePath = string.Empty;
            UpdateInstanceList();
            IsFree = true;
        }

        public void OpenAddInstanceWindow(object o)
        {
            AddInstanceWindow window = new();
            window.ShowDialog();
            UpdateInstanceList();
        }

        public void DeleteInstance(object o)
        {
            try
            {
                IsFree = false;
                if (MessageBox.Show($"Delete {SelectedInstance.InstanceName}? This will not delete the actual files.",
                    "Delete instance", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    File.Delete(Path.Join(instanceDir, SelectedInstance.InstanceName + ".json"));
                    LogList.Add($"Instance {SelectedInstance.InstanceName} deleted");
                }
                else
                {
                    LogList.Add("Delete operation aborted.");
                }
                UpdateInstanceList();
                IsFree = true;
            }
            catch (Exception e)
            {
                LogList.Add(e.Message);
            }
        }

        public async void CreateHardLinks(object o)
        {
            try
            {
                IsFree = false;
                Progress = 0;
                if (MessageBox.Show($"About to create hard links from {SelectedInstance.InstanceName}. " +
                    $"Proceed to link target directory selection?", "Create hard links", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    FolderBrowserDialog folderBrowser = new FolderBrowserDialog();
                    if (folderBrowser.ShowDialog() == DialogResult.OK)
                    {
                        string selectedPath = folderBrowser.SelectedPath;
                        using FileStream openStream = File.OpenRead(Path.Combine(instanceDir, SelectedInstance.InstanceName + ".json"));
                        Instance inst = await JsonSerializer.DeserializeAsync<Instance>(openStream);
                        ProgressStep = 100D / inst.TotalFileCount;

                        foreach (string dir in inst.DirectoryList)
                        {
                            if (!Directory.Exists(Path.Combine(selectedPath, dir)))
                            {
                                Directory.CreateDirectory(Path.Combine(selectedPath, dir));
                            }
                        }

                        LogList.Add("Checking for duplicate files...");

                        if (CheckForDupes(inst, selectedPath))
                        {
                            foreach (var file in inst.FileList)
                            {
                                if (File.Exists(Path.Combine(selectedPath, file.RelativePath, file.FileName)))
                                {
                                    //File.Delete(Path.Combine(selectedPath, file.RelativePath, file.FileName));
                                    //LogList.Add($"Deleting {Path.Combine(selectedPath, file.RelativePath, file.FileName)}");
                                }
                            }
                        }
                        else
                        {
                            LogList.Add("Link operation aborted.");
                            IsFree = true;
                            return;
                        }

                        LogList.Add("Linking...");

                        foreach (var file in inst.FileList)
                        {
                            await Task.Run(() => CreateHardLink(Path.Combine(selectedPath, file.RelativePath, file.FileName),
                                Path.Combine(dbDir, file.HashedFileName), IntPtr.Zero));
                            Progress += ProgressStep;
                        }

                        LogList.Add("Done!");
                        IsFree = true;
                    }
                    else LogList.Add("Link operation aborted.");
                }
                else LogList.Add("Link operation aborted.");
                IsFree = true;
            }
            catch (Exception e)
            {
                LogList.Add(e.Message);
            }
        }

        public void CheckDirs()
        {
            if (Directory.Exists(dbDir) == false)
                Directory.CreateDirectory(dbDir);
            if (Directory.Exists(instanceDir) == false)
                Directory.CreateDirectory(instanceDir);
        }

        public bool CheckForDupes(Instance inst, string targetPath)
        {
            foreach (var file in inst.FileList)
            {
                if (File.Exists(Path.Combine(targetPath, file.RelativePath, file.FileName)))
                {
                    LogList.Add($"File {file.FileName} already exists in {targetPath}");
                    if (MessageBox.Show($"{file.FileName} already exists in the target directory. Delete and link? 'No' will cancel the operation.",
                        "Duplicate file", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        return true;
                    }
                    else return false;
                }
            }
            return false;
        }

        public async void UpdateInstanceList()
        {
            IsFree = false;
            InstanceList = new ObservableCollection<InstanceListEntry>();
            foreach (var file in Directory.GetFiles(instanceDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                using FileStream openStream = File.OpenRead(file);
                Instance inst = await JsonSerializer.DeserializeAsync<Instance>(openStream);
                InstanceListEntry entry = new(inst);
                InstanceList.Add(entry);
                openStream.Close();
            }
            LogList.Add("Instance list updated.");
            UpdateDBSize();
            IsFree = true;
        }

        public async void CheckForUnusedFiles(object o)
        {
            IsFree = false;
            HashSet<string> existingFiles = new();

            foreach (var file in Directory.GetFiles(dbDir, "*"))
            {
                existingFiles.Add(Path.GetFileName(file));
            }

            foreach (var instJson in Directory.GetFiles(instanceDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                using FileStream openStream = File.OpenRead(instJson);
                Instance inst = await JsonSerializer.DeserializeAsync<Instance>(openStream);
                foreach (var file in inst.FileList)
                {
                    existingFiles.Remove(file.HashedFileName);
                }
            }

            if (existingFiles.Count > 0)
            {
                if (MessageBox.Show($"{existingFiles.Count} unused files found. Delete?", "Unused files found", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    foreach (var file in existingFiles)
                    {
                        File.Delete(Path.Combine(dbDir, file));
                    }
                    LogList.Add("Unused files deleted.");
                    UpdateInstanceList();
                }
                else
                {
                    LogList.Add("Unused files deletion aborted.");
                }
            }
            else
            {
                MessageBox.Show("No unused files found.", "No unused files", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            IsFree = true;
        }
        public bool CanDoActionWithInstance(object o)
        {
            if (SelectedInstance == null || !IsFree)
                return false;
            else return true;
        }

        public void ImportLegacyInstances(object o)
        {
            IsFree = false;

            OpenFileDialog fileDialog = new();
            fileDialog.Filter = "Legacy DBInfo|*.xml";
            fileDialog.Title = "Select legacy DBInfo.xml";
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                XmlSerializer serializer = new(typeof(DBInfo));
                FileStream fs = new(fileDialog.FileName, FileMode.Open);
                XmlReader reader = XmlReader.Create(fs);
                DBInfo info = (DBInfo)serializer.Deserialize(reader);
                foreach (var instance in info.InstanceList)
                {
                    if (File.Exists(Path.Combine(instanceDir, instance.InstanceName + ".json")))
                    {
                        LogList.Add($"Instance {instance.InstanceName} already exists. Not importing.");
                    }
                    else
                    {
                        List<InstanceFile> fileList = new();
                        HashSet<string> uniqueDirs = new();
                        foreach (var fileInfo in instance.InstanceFiles)
                        {
                            string relativePath = fileInfo.Location;
                            if (fileInfo.Location.StartsWith(@"\"))
                            {
                                relativePath = fileInfo.Location.Substring(1);
                                //LogList.Add(relativePath);
                                //break;
                            }
                            uniqueDirs.Add(relativePath);
                            InstanceFile newFile = new(fileInfo.OriginalFileName, relativePath, fileInfo.SizeBytes, fileInfo.HashedFileName);
                            fileList.Add(newFile);
                        }
                        List<string> dirs = new();
                        foreach (var dir in uniqueDirs)
                        {
                            dirs.Add(dir);
                        }
                        Instance newInstance = new(fileList, dirs, instance.Size, instance.Entries, instance.InstanceName);
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        newInstance.SaveToFile(options);

                        LogList.Add($"Instance {instance.InstanceName} imported.");
                    }
                }
                UpdateInstanceList();
                LogList.Add("Importing finished");
                fs.Close();
            }
            else
            {
                LogList.Add("Import operation aborted.");
            }

            IsFree = true;
        }

        public async Task CheckFiles()
        {
            await Task.Run(() => CheckFilesJob());
        }

        public async void CheckFilesJob()
        {
            if (SelectedInstance == null)
            {
                MessageBox.Show("Please select an instance", "No instance selected", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else if (string.IsNullOrEmpty(TorrentFilePath))
            {
                MessageBox.Show("Please select a torrent file", "No torrent file selected", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                Progress = 0;
                UIContext.Send(x => MatchedList.Clear(), null);
                var parser = new BencodeParser();
                List<string> matchedList = new();
                Torrent torrent = parser.Parse<Torrent>(TorrentFilePath);
                ProgressStep = 100D / torrent.Files.Count;
                using FileStream openStream = File.OpenRead(Path.Combine(instanceDir, SelectedInstance.InstanceName + ".json"));
                Instance inst = await JsonSerializer.DeserializeAsync<Instance>(openStream);
                IsFree = false;
                foreach (var file in torrent.Files)
                {
                    string? relPath = Path.GetDirectoryName(file.FullPath);
                    if (relPath == null)
                    {
                        relPath = string.Empty;
                    }

                    if (relPath.StartsWith(RelativePath))
                    {
                        string relPathQ = relPath;
                        if (relPath != string.Empty && !string.IsNullOrEmpty(RelativePath))
                        {
                            if (!Path.EndsInDirectorySeparator(RelativePath))
                            {
                                RelativePath += Path.DirectorySeparatorChar;
                            }
                            relPathQ = relPath.Substring(RelativePath.Length);
                        }
                        var instQuery = inst.FileList.Select(f => f).Where(f => f.RelativePath == relPathQ)
                            .Select(f => f).Where(f => f.FileName == file.FileName).Select(f => f)
                            .Where(f => f.FileSize == file.FileSize).Select(f => f);

                        if (instQuery.Count() > 0)
                        {
                            matchedList.Add(Path.Combine(instQuery.Single().RelativePath, instQuery.Single().FileName));
                            //UIContext.Send(x => MatchedList.Add(Path.Combine(instQuery.Single().RelativePath, instQuery.Single().FileName)), null);
                        }
                    }
                    Progress += ProgressStep;
                }
                UIContext.Send(x => MatchedList = matchedList, null);
                UIContext.Send(x => LogList.Add($"Matched {matchedList.Count} out of {torrent.Files.Count} files (compared names and sizes)."), null);
                //LogList.Add($"Matched {MatchedList.Count} out of {torrent.Files.Count} files (compared names and sizes).");

                if (MatchedList.Count == 0)
                {
                    UIContext.Send(x => LogList.Add("Check if your relative path is correct."), null);
                    //LogList.Add("Check if your relative path is correct.");
                }
                else
                {
                    DidCheckFiles = true;
                }
                IsFree = true;
            }
        }

        public async Task CheckPieces()
        {
            DidCheckFiles = false;
            await Task.Run(() => CheckPiecesJob());
        }

        public async void CheckPiecesJob()
        {
            Progress = 0;
            IsFree = false;
            var parser = new BencodeParser();

            Torrent torrent = parser.Parse<Torrent>(TorrentFilePath);
            ProgressStep = 100D / (torrent.Files.Count + 1);
            List<string> torrentFiles = new();
            UIContext.Send(x => LogList.Add($"Piece length: {torrent.PieceSize}"), null);
            UIContext.Send(x => LogList.Add($"Number of pieces: {torrent.NumberOfPieces}"), null);
            UIContext.Send(x => LogList.Add("Beginning piece check, this might take a while..."), null);

            // make a list with individual pieces
            List<byte[]> torrentPieceList = new();
            for (int i = 0; i < torrent.Pieces.Length; i += 20)
            {
                byte[] piece = new byte[20];
                Array.Copy(torrent.Pieces, i, piece, 0, 20);
                torrentPieceList.Add(piece);
            }

            using FileStream openStream = File.OpenRead(Path.Combine(instanceDir, SelectedInstance.InstanceName + ".json"));
            Instance inst = await JsonSerializer.DeserializeAsync<Instance>(openStream);

            TorrentPiecer piecer = new(torrent.TotalSize, torrent.PieceSize, torrent.NumberOfPieces);

            foreach (var file in torrent.Files)
            {
                string? relPath = Path.GetDirectoryName(file.FullPath);
                if (relPath == null)
                {
                    relPath = string.Empty;
                }

                var iQ = inst.FileList.Select(f => f).Where(f => Path.Combine(RelativePath, f.RelativePath) == relPath)
                    .Select(f => f).Where(f => f.FileName == file.FileName).Select(f => f)
                    .Where(f => f.FileSize == file.FileSize).Select(f => f);

                if (iQ.Count() > 0)
                {
                    byte[] fileBytes = File.ReadAllBytes(Path.Combine(dbDir, iQ.Single().HashedFileName));
                    piecer.PushFile(Path.Join(RelativePath, iQ.Single().RelativePath, iQ.Single().FileName),
                        iQ.Single().HashedFileName, fileBytes);
                }
                else
                {
                    byte[] zeroBytes = new byte[file.FileSize];
                    Array.Fill<byte>(zeroBytes, 0);
                    piecer.PushFile(file.FullPath, "NO LOCAL FILE", zeroBytes);
                }
                Progress += ProgressStep;
            }

            // compare pieces
            List<long> badPieces = new();
            if (piecer.Pieces.Count == torrentPieceList.Count)
            {
                for (int i = 0; i < torrent.NumberOfPieces; i++)
                {
                    if (!piecer.Pieces[i].SequenceEqual(torrentPieceList[i]))
                    {
                        badPieces.Add(i);
                    }
                }
                UIContext.Send(x => LogList.Add($"Piece check finished. {torrent.NumberOfPieces - badPieces.Count} out of {torrent.NumberOfPieces} pieces matched."), null);
                BadPieces = badPieces;
                FilePieceMap = piecer.FilePieceMap;
                DidCheckPieces = true;
            }
            else
            {
                UIContext.Send(x => LogList.Add("Piece count does not match, something went terribly wrong."), null);
            }
            Progress += ProgressStep;
            IsFree = true;
        }

        public async Task LinkToTorrent()
        {
            await Task.Run(() => LinkToTorrentFunc());
        }

        public void LinkToTorrentFunc()
        {
            if (BadPieces == null)
            {
                MessageBox.Show("Check pieces first.");
            }
            else if (FilePieceMap == null)
            {
                MessageBox.Show("Check pieces first");
            }
            else if (string.IsNullOrEmpty(TorrentDownloadPath))
            {
                MessageBox.Show("Please input the torrent download path", "Torrent download path empty",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                DidCheckPieces = false;
                UIContext.Send(x => LogList.Add("Linking..."), null);

                Progress = 0;
                DidCheckFiles = false;
                ProgressStep = 100D / FilePieceMap.Keys.Count;
                foreach (var file in FilePieceMap)
                {
                    string getDir = Path.GetDirectoryName(file.Key.FileName);
                    if (getDir != null)
                    {
                        if (!Directory.Exists(Path.Combine(TorrentDownloadPath, getDir)))
                        {
                            Directory.CreateDirectory(Path.Combine(TorrentDownloadPath, getDir));
                        }
                    }

                    if (!file.Value.Select(p => p).Intersect(BadPieces).Any())
                    {
                        if (!File.Exists(Path.Combine(TorrentDownloadPath, file.Key.FileName)))
                        {
                            CreateHardLink(Path.Combine(TorrentDownloadPath, file.Key.FileName),
                                Path.Combine(dbDir, file.Key.HashedFileName), IntPtr.Zero);
                            //LogList.Add($"Linking {Path.Combine(TorrentDownloadPath, file.Key.FileName)}");
                        }
                    }
                    Progress += ProgressStep;
                }

                UIContext.Send(x => LogList.Add("Linking finished"), null);
                UIContext.Send(x => BadPieces = null, null);
                UIContext.Send(x => FilePieceMap = null, null);
            }
        }

        public void FolderBrowseTorrentPath(object o)
        {
            OpenFileDialog fileDialog = new();
            fileDialog.Filter = "Torrent file|*.torrent";
            fileDialog.Title = "Select a torrent file";
            if (fileDialog.ShowDialog() == DialogResult.OK)
            {
                TorrentFilePath = fileDialog.FileName;
            }
        }

        public void FolderBrowserTorrentDLPath(object o)
        {
            FolderBrowserDialog dialog = new();
            dialog.ShowDialog();
            TorrentDownloadPath = dialog.SelectedPath;
            dialog.Dispose();
        }

        public bool CheckIfCanLinkToTorrent(object o)
        {
            if (!IsFree || BadPieces == null || FilePieceMap == null || string.IsNullOrEmpty(TorrentDownloadPath))
            {
                return false;
            }
            else return true;
        }

        public bool CheckIfCanCheckFiles(object o)
        {
            if (!IsFree || SelectedInstance == null || string.IsNullOrEmpty(TorrentFilePath))
            {
                return false;
            }
            else return true;
        }

        public void UpdateDBSize()
        {
            long dbSize = 0;
            DirectoryInfo di = new(dbDir);
            foreach (var file in di.GetFiles())
            {
                dbSize += file.Length;
            }
            DBSize = Instance.ReadableSize(dbSize);
            long instTotal = 0;
            foreach(var inst in InstanceList)
            {
                instTotal += inst.TotalFileSize;
            }
            Savings = Instance.ReadableSize(instTotal - dbSize);
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes
        );
    }
}
