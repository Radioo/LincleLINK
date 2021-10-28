using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static WPFLinkTool.Global;

namespace WPFLinkTool
{
    public class AddInstanceCommand : AsyncCommandBase
    {
        private readonly AddInstanceViewModel vm;

        public AddInstanceCommand(AddInstanceViewModel addInstanceViewModel, Action<Exception> onException) : base(onException)
        {
            vm = addInstanceViewModel;
        }
        
        protected override async Task ExecuteAsync(object parameter)
        {
            if (vm.InstanceName != string.Empty && vm.DataSource != string.Empty && IsUniqueName(vm.InstanceName))
            {
                IsExecuting = true;
                vm.Progress = 0;

                // Create a list of InstanceFileInfos
                List<InstanceFileInfo> fileInfos = new();
                long instanceSize = 0;

                // Enumerate files
                vm.StatusText = "Enumerating files...";
                DirectoryInfo dir = new(vm.DataSource);
                var files = dir.EnumerateFiles("", SearchOption.AllDirectories);
                int already = 0;

                // Calculate progress step
                double progressStep = 100D / files.Count();
                
                foreach (var file in files)
                {
                    // Hash and make file infos
                    vm.StatusText = $"Hashing {file.FullName}";
                    string hash = await Task.Run(() => GetMD5Checksum(file.FullName));
                    InstanceFileInfo inFile = new(file.Name, hash + file.Extension,
                        file.DirectoryName.Replace(vm.DataSource, ""), file.Length);
                    instanceSize += file.Length;

                    // Update the progress bar
                    vm.Progress += progressStep;

                    // Add file info to the list
                    fileInfos.Add(inFile);

                    // Check if file exists and move/copy
                    if (!File.Exists(dbDir + @"\" + hash + file.Extension))
                    {
                        if (vm.IsCopyChecked)
                        {
                            vm.StatusText = $"Copying {file.Name}";
                            await Task.Run(() => File.Copy(file.FullName, dbDir + @"\" + hash + file.Extension));
                        }
                        else
                        {
                            vm.StatusText = $"Moving {file.Name}";
                            await Task.Run(() => File.Move(file.FullName, dbDir + @"\" + hash + file.Extension));
                        }
                    }
                    else already++;
                }
                
                // Create a DataInstance and add to DBInfo
                DataInstance instance = new(vm.InstanceName, fileInfos, fileInfos.Count, instanceSize);
                vm.Shared.Info.InstanceList.Add(instance);
                vm.Shared.Info.SaveToXml();

                vm.StatusText = $"Done. {instance.InstanceName} added with {instance.Entries} files and a size of {instance.PrettySize} \n" +
                    $"{already} files already exist.";
                IsExecuting = false;
            }
            else
            {
                MessageBox.Show("Empty fields or duplicate instance name.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        public string GetMD5Checksum(string filename)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filename);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "");
        }
        public bool IsUniqueName(string instancename)
        {
            List<string> names = new();
            List<string> namesAdd = new();
            foreach (var instance in vm.Shared.Info.InstanceList)
            {
                names.Add(instance.InstanceName);
            }
            namesAdd = names;
            namesAdd.Add(instancename);
            if (names.Count == namesAdd.Distinct().Count())
                return true;
            else return false;
        }
    }
}
