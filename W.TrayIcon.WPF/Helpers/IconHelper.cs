using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using W.TrayIcon.WPF.Native;

namespace W.TrayIcon.WPF.Helpers;

public static class IconHelper
{
#if NET10_0_OR_GREATER
    public static IntPtr GetAppIconHandle()
    {
        return System.Drawing.SystemIcons.Application.Handle;
    }
#elif NET6_0_OR_GREATER
    public static IntPtr GetAppIconHandle()
    {
        // В .NET 5–9 Icon.Handle может быть недоступен, используем обход
        using (var bmp = System.Drawing.SystemIcons.Application.ToBitmap())
        {
            return bmp.GetHicon();
        }
    }
#else
public static IntPtr GetAppIconHandle()
{
    // В старых фреймворках только ToBitmap
    using (var bmp = System.Drawing.SystemIcons.Application.ToBitmap())
    {
        return bmp.GetHicon();
    }
}
#endif

    public static Icon GetIconFromImageSource(ImageSource imageSource, int size = 16)
    {
        if (imageSource is BitmapSource bitmapSource)
        {
            // Конвертируем в Bitmap
            using (var ms = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(ms);
                using (var bmp = new Bitmap(ms))
                {
                    // Меняем размер под нужный DPI
                    using (var resized = new Bitmap(bmp, new Size(size, size)))
                    {
                        IntPtr hIcon = resized.GetHicon();

                        // Создаём Icon из handle
                        Icon icon = Icon.FromHandle(hIcon);
                        Icon clone = (Icon)icon.Clone();

                        // Освобождаем исходный handle
                        NativeMethods.DestroyIcon(hIcon);

                        return clone;
                    }
                }
            }
        }

        throw new ArgumentException("ImageSource must be a BitmapSource");
    }
}