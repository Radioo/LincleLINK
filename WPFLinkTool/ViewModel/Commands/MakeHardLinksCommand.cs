using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using static WPFLinkTool.Global;

namespace WPFLinkTool
{
    public class MakeHardLinksCommand : AsyncCommandBase
    {
        private readonly MainWindowViewModel vm;
        private bool proceed = false;
        private bool delet = false;
        private string target;

        public MakeHardLinksCommand(MainWindowViewModel viewmodel, Action<Exception> onException) : base(onException)
        {
            vm = viewmodel;
        }

        protected override async Task ExecuteAsync(object parameter)
        {
            IsExecuting = true;
            vm.UIEnabled = false;
            vm.LinkButtonEnabled = false;
            DataInstance selectedInstance = vm.SelectedInstance;


            var prompt = MessageBox.Show($"Creating hard links from {selectedInstance.InstanceName}.\nProceed to link target selection?", "Make hard links", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (prompt == DialogResult.Yes)
            {
                FolderBrowserDialog dialog = new();
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    target = dialog.SelectedPath;

                    foreach (var file in selectedInstance.InstanceFiles)
                    {
                        if (File.Exists(target + file.Location + @"\" + file.OriginalFileName))
                        {
                            var prompt2 = MessageBox.Show(@$"{file.Location}\{file.OriginalFileName} already exists in the target directory!" + "\n"
                                + "Do you want to delete all encountered dupes before making hard links? (If you didn't know or forgot, overwriting hard linked files changes the originals) 'No' cancels the whole operation.", "Duplicate files detected!", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                            if (prompt2 == DialogResult.Yes)
                            {
                                proceed = true;
                                delet = true;
                                break;
                            }
                            break;
                        }
                        else
                        {
                            proceed = true;
                        }
                    }

                    if (delet)
                    {
                        foreach (var file in selectedInstance.InstanceFiles)
                        {
                            if (File.Exists(target + file.Location + @"\" + file.OriginalFileName))
                            {
                                File.Delete(target + file.Location + @"\" + file.OriginalFileName);
                            }
                        }
                    }

                    if (proceed)
                    {
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

            }

            vm.Progress = 0;



            vm.UIEnabled = true;
            vm.LinkButtonEnabled = true;
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
