using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace W.TrayIcon.WPF.Helpers;

public static class XamlHelper
{
    public static T? Clone<T>(T? original) where T : FrameworkElement
    {
        if (original == null) return default;

        var xaml = XamlWriter.Save(original);

        using var stringReader = new StringReader(xaml);
        using var xmlReader = System.Xml.XmlReader.Create(stringReader);
        return (T)XamlReader.Load(xmlReader);
    }
}
