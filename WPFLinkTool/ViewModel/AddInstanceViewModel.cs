using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;

namespace WPFLinkTool
{
    public class AddInstanceViewModel : ViewModelBase
    {
        public readonly SharedViewModel Shared;
        public bool IsCopyChecked { get; set; } = true;
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
        public string InstanceName { get; set; }
        public string DataSource { get; set; }
        private string _statusText;
        public string StatusText
        {
            get
            {
                return _statusText;
            }
            set
            {
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public RelayCommand CalculateFilesCommand { get; private set; }
        public RelayCommand OpenBrowseDialog { get; private set; }
        public ICommand AddInstanceCommand { get; }

        public AddInstanceViewModel(SharedViewModel shared)
        {
            Shared = shared;
            InstanceName = "";
            DataSource = "";
            OpenBrowseDialog = new(OpenFolderBrowser);
            AddInstanceCommand = new AddInstanceCommand(this, (ex) => StatusText = ex.Message);
        }

        public void OpenFolderBrowser(object i)
        {
            FolderBrowserDialog dialog = new();
            dialog.ShowDialog();
            DataSource = dialog.SelectedPath;
            OnPropertyChanged("DataSource");
            dialog.Dispose();
        }
    }
}
