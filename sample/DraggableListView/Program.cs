using System;
using System.Windows;

namespace DraggableListViewSample
{
    public partial class App : Application
    {
        [STAThread]
        public static void Main()
        {
            var application = new App();
            var mainWindow = new TestWindow();
            application.Run(mainWindow);
        }
    }
}
