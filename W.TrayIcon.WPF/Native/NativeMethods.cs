using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace W.TrayIcon.WPF.Native;

/// <summary>
/// Наименование библиотек
/// </summary>
public static class ImportLibNames
{
    public const string User32 = "user32.dll";

    public const string Kernel32 = "kernel32.dll";

    public const string Shell32 = "shell32.dll";

    public const string UxTheme = "uxtheme.dll";
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int x;
    public int y;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public nint dwExtraInfo;
}

// Структура RECT
[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

// Структура NOTIFYICONIDENTIFIER
[StructLayout(LayoutKind.Sequential)]
public struct NOTIFYICONIDENTIFIER
{
    public int cbSize;
    public nint hWnd;
    public int uID;
    public Guid guidItem;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NOTIFYICONDATA
{
    public int cbSize;
    public nint hWnd;
    public int uID;
    public ETrayIconFlags uFlags;
    public int uCallbackMessage;
    public nint hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
    public Guid guidItem;
}

public static class NativeMethods
{
    [DllImport(ImportLibNames.User32)]
    public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport(ImportLibNames.User32, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport(ImportLibNames.User32, SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(ImportLibNames.User32, CharSet = CharSet.Auto)] 
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport(ImportLibNames.User32, CharSet = CharSet.Auto)]
    public extern static bool DestroyIcon(IntPtr handle);

    public static System.Drawing.Icon GetIconFromHandle(IntPtr hIcon)
    {
        System.Drawing.Icon icon = System.Drawing.Icon.FromHandle(hIcon);
        // Клонируем, чтобы освободить оригинальный handle
        System.Drawing.Icon clone = (System.Drawing.Icon)icon.Clone();
        DestroyIcon(hIcon);
        return clone;
    }


    [DllImport(ImportLibNames.User32)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport(ImportLibNames.User32, CharSet = CharSet.Auto, SetLastError = true)]
    public static extern nint SetWindowsHookEx(EWindowHooks idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport(ImportLibNames.User32, CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport(ImportLibNames.User32, CharSet = CharSet.Auto, SetLastError = true)]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport(ImportLibNames.Kernel32, CharSet = CharSet.Auto, SetLastError = true)]
    public static extern nint GetModuleHandle(string lpModuleName);

    [DllImport(ImportLibNames.User32, SetLastError = true)]
    public static extern nint SetTimer(nint hWnd, nint nIDEvent, uint uElapse, nint lpTimerFunc);

    [DllImport(ImportLibNames.User32, SetLastError = true)]
    public static extern bool KillTimer(nint hWnd, nint uIDEvent);

    [DllImport(ImportLibNames.Shell32, CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(ETrayIconMessage dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport(ImportLibNames.User32)]
    public static extern uint GetDoubleClickTime();

    // Импорт функции Shell_NotifyIconGetRect
    [DllImport(ImportLibNames.Shell32, SetLastError = true)]
    public static extern int Shell_NotifyIconGetRect(
        ref NOTIFYICONIDENTIFIER identifier,
        out RECT iconLocation
    );

    [DllImport(ImportLibNames.User32, SetLastError = true)]
    public static extern bool SystemParametersInfo(ESystemParameters uiAction, int uiParam, out RECT pvParam, int fWinIni);


    /// <summary>
    /// Рабочая область primary screen (где находится трей).
    /// Возвращает координаты в WPF DIPs.
    /// </summary>
    public static Rect GetPrimaryWorkArea()
    {
        if (SystemParametersInfo(ESystemParameters.SPI_GETWORKAREA, 0, out RECT rect, 0))
        {
            // Преобразуем в WPF Rect (DIPs)
            double dpiX = 1.0, dpiY = 1.0;
            if (Application.Current?.MainWindow != null)
            {
                var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
                dpiX = dpi.DpiScaleX;
                dpiY = dpi.DpiScaleY;
            }

            return new Rect(
                rect.Left / dpiX,
                rect.Top / dpiY,
                (rect.Right - rect.Left) / dpiX,
                (rect.Bottom - rect.Top) / dpiY
            );
        }

        return Rect.Empty;
    }
}

public static class NativeThemeColorsMethods
{
    [DllImport(ImportLibNames.UxTheme, CharSet = CharSet.Unicode)]
    private static extern nint OpenThemeData(nint hwnd, string pszClassList);

    [DllImport(ImportLibNames.UxTheme)]
    private static extern int CloseThemeData(nint hTheme);

    [DllImport(ImportLibNames.UxTheme, CharSet = CharSet.Unicode)]
    private static extern int GetThemeColor(nint hTheme, int iPartId, int iStateId, int iPropId, out int pColor);

    // Константы для Tooltip
    private const int TTP_STANDARD = 1;   // часть "стандартный тултип"
    private const int TTS_NORMAL = 1;   // состояние "нормальный"
    private const int WP_CAPTION = 1; // часть окна — заголовок
    private const int CS_ACTIVE = 1; // активное состояние
    private const int TMT_TEXTCOLOR = 3803;
    private const int TMT_FILLCOLOR = 3802;

    [DllImport(ImportLibNames.User32)]
    private static extern int GetSysColor(int nIndex);

    const int COLOR_WINDOW = 5;

    public static Color GetWindowColor()
    {
        int rgb = GetSysColor(COLOR_WINDOW);
        return GetColor(rgb, false);
    }

    public static Color GetTooltipBackground()
    {
        var hTheme = OpenThemeData(IntPtr.Zero, "Window");
        if (hTheme != IntPtr.Zero)
        {
            if (GetThemeColor(hTheme, WP_CAPTION, CS_ACTIVE, TMT_FILLCOLOR, out int argb) == 0)
            {
                _ = CloseThemeData(hTheme);
                return GetColor(argb);
            }
            _ = CloseThemeData(hTheme);
        }
        return SystemColors.WindowColor; // fallback
    }

    public static Color GetTooltipText()
    {
        nint hTheme = OpenThemeData(IntPtr.Zero, "Tooltip");
        if (hTheme != IntPtr.Zero)
        {
            int argb;
            if (GetThemeColor(hTheme, TTP_STANDARD, TTS_NORMAL, TMT_TEXTCOLOR, out argb) == 0)
            {
                _ = CloseThemeData(hTheme);
                return GetColor(argb);
            }
            _ = CloseThemeData(hTheme);
        }
        return Colors.Black; // fallback
    }

    private static Color GetColor(int argb, bool isAlpha = true)
    {
        return Color.FromArgb(
                    isAlpha ? (byte)((argb >> 24) & 0xFF) : (byte)0xFF,
                    (byte)(argb & 0xFF),
                    (byte)((argb >> 8) & 0xFF),
                    (byte)((argb >> 16) & 0xFF)
                );
    }
}