using System.Windows;

namespace W.TrayIcon.WPF.DpiSample
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
#pragma warning disable WPF0001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
            if (Application.Current != null)
            {
                Application.Current.ThemeMode = (Application.Current.ThemeMode == ThemeMode.Light) ? ThemeMode.Dark : ThemeMode.Light;
            }
#pragma warning restore WPF0001

        }
    }
}