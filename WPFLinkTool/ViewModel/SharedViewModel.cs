using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WPFLinkTool
{
    public class SharedViewModel : ViewModelBase
    {
        private DBInfo _Info;
        public DBInfo Info
        {
            get { return _Info; }
            set
            {
                _Info = value;
                OnPropertyChanged(nameof(Info));
            }
        }
    }
}
