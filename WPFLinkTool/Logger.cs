using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace WPFLinkTool
{
    public class Logger
    {
        public ObservableCollection<string> LogEntries;
        public ScrollViewer Scroller;

        public Logger(ScrollViewer scroller)
        {
            LogEntries = new();
            Scroller = scroller;
        }

        public void Log(string message)
        {
            LogEntries.Add(DateTime.Now.ToString() + " " + message);
            Scroller.ScrollToBottom();
        }
    }
}
