using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static WPFLinkTool.Global;

namespace WPFLinkTool
{
    public class MakeHardLinksCommand : AsyncCommandBase
    {
        private readonly MainWindowViewModel vm;
        
        public MakeHardLinksCommand(MainWindowViewModel viewmodel, Action<Exception> onException) : base(onException)
        {
            vm = viewmodel;   
        }
        
        protected override async Task ExecuteAsync(object parameter)
        {
            IsExecuting = true;
            vm.UIEnabled = false;
            string selInst = vm.SelectedInstance;
            var selectedInstance = (from inst in vm.Shared.Info.InstanceList
                                   where inst.InstanceName == selInst
                                   select inst).Single();
            


            var prompt = MessageBox.Show($"Creating hard links from {selectedInstance.InstanceName}.\nProceed to link target selection?", "Make hard links", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (prompt == DialogResult.Yes)
            {
                FolderBrowserDialog dialog = new();
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string target = dialog.SelectedPath;

                    var uniqueFolders = (from folders in selectedInstance.InstanceFiles
                                         where folders.Location != string.Empty
                                         select folders.Location).Distinct();

                    double progressStep = 100D / (selectedInstance.InstanceFiles.Count + uniqueFolders.Count());

                    foreach (var folder in uniqueFolders)
                    {
                        if (!Directory.Exists(target + folder))
                        {
                            await Task.Run(() => Directory.CreateDirectory(target + folder));
                        }
                        vm.Progress += progressStep;
                    }

                    foreach (var file in selectedInstance.InstanceFiles)
                    {
                        if (!File.Exists(target + file.Location + @"\" + file.OriginalFileName))
                        {
                            await Task.Run(() => CreateHardLink(target + file.Location + @"\" + file.OriginalFileName,
                                dbDir + @"\" + file.HashedFileName, IntPtr.Zero));
                        }
                        vm.Progress += progressStep;
                    }
                }
            }
            
            vm.Progress = 0;



            vm.UIEnabled = true;
            IsExecuting = false;
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes
        );
    }
}
