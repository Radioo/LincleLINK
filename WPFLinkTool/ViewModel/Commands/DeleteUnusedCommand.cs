using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static WPFLinkTool.Global;

namespace WPFLinkTool
{
    class DeleteUnusedCommand : AsyncCommandBase
    {
        private readonly MainWindowViewModel vm;
        
        public DeleteUnusedCommand(MainWindowViewModel viewModel, Action<Exception> onException) : base(onException)
        {
            vm = viewModel;
        }

        protected override async Task ExecuteAsync(object parameter)
        {
            IsExecuting = true;
            vm.UIEnabled = false;
            vm.LinkButtonEnabled = false;

            HashSet<string> dBFiles = new();
            DirectoryInfo dir = new(dbDir);
            foreach (var file in dir.GetFiles())
            {
                dBFiles.Add(file.Name);
            }

            foreach (var instance in vm.Shared.Info.InstanceList)
            {
                foreach (var file in instance.InstanceFiles)
                {
                    dBFiles.Remove(file.HashedFileName);
                }
            }

            double progressStep = 100D / dBFiles.Count;

            var prompt = MessageBox.Show($"Found {dBFiles.Count} unused files. Delete?", "Delete unused files",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (prompt == DialogResult.Yes)
            {
                foreach (var file in dBFiles)
                {
                    await Task.Run(() => File.Delete(dbDir + @"\" + file));
                    vm.Progress += progressStep;
                }
            }

            await Task.Run(() => vm.UpdateDBSize());

            vm.Progress = 0;
            vm.UIEnabled = true;
            vm.LinkButtonEnabled = true;
            IsExecuting = false;
        }
    }
}
