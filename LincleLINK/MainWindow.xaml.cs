﻿using LincleLINK.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace LincleLINK
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {    
        public MainWindow()
        {
            InitializeComponent();
            MainWindowControls controls = new(LogScroller, MatchedFilesScroller);
            var uiContext = SynchronizationContext.Current;
            DataContext = new MainWindowLogic(controls, uiContext);
        }
    }

    public struct MainWindowControls
    {
        public ScrollViewer LogScrollViewer;
        public ScrollViewer MatchedFilesScroller;

        public MainWindowControls(ScrollViewer scroller, ScrollViewer matchedScroller)
        {
            LogScrollViewer = scroller;
            MatchedFilesScroller = matchedScroller;
        }
    }
}
