using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;

namespace W.TrayIcon.WPF.Sample;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    public MainWindow()
    {
        Loaded += MainWindow_Loaded;
        InitializeComponent();

        DataContext = this;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        DataContext = this;

        int i = 0;

        await Task.Run(async () =>
        {
            while (true)
            {
                Test = $"Test Window {i}";

                await Task.Delay(1000);
                i++;
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _test = "Test Window";

    public string Test
    {
        get => _test;
        set
        {
            _test = value;
            OnPropertyChanged();
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("Test");
    }
}