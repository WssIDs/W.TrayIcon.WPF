using System.Windows;

namespace W.TrayIcon.WPF.Statics;

public static class TrayIconStatics
{
    public static double DpiScale = 1.0f;

    /// <summary>
    /// 
    /// </summary>
    public static void CalculateDpi()
    {
        // DPI‑коррекция
        var source = PresentationSource.FromVisual(Application.Current.MainWindow);

        var dpiX = 1.0;
        //var dpiY = 1.0;
        if (source?.CompositionTarget != null)
        {
            dpiX = source.CompositionTarget.TransformFromDevice.M11;
            //dpiY = source.CompositionTarget.TransformFromDevice.M22;
        }

        DpiScale = dpiX;
    }
}