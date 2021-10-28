using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WPFLinkTool
{
    public class SharedViewModel : ViewModelBase
    {
        private DBInfo _info;
        public DBInfo Info
        {
            get
            {
                return _info;
            }
            set
            {
                _info = value;
                OnPropertyChanged(nameof(Info));
            }
        }
    }
}
