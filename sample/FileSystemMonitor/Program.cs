using System;
using System.Windows;
using Illustra.Views;

namespace Illustra.Sample.FileSystemMonitor
{
    public class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();
            app.Run(new TestWindow());
        }
    }
}
