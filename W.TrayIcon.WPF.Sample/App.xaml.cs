using System.Configuration;
using System.Data;
using System.Windows;

namespace W.TrayIcon.WPF.Sample
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private MainWindow? wnd = null;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            wnd = new MainWindow();
            wnd.Show();
        }
    }

}
