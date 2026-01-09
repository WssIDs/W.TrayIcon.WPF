namespace W.TrayIcon.WPF.Native;

public enum EWindowMessages
{
    WM_MOUSEMOVE = 0x0200,
    WM_RBUTTONDOWN = 0x0204,
    WM_LBUTTONDOWN = 0x0201,
    WM_MBUTTONDOWN = 0x0207,
    WM_LBUTTONUP = 0x0202,
    WM_RBUTTONUP = 0x0205,
    WM_MBUTTONUP = 0x0208,
    WM_MOUSELEAVE = 0x02A3,
    WM_LBUTTONDBLCLK = 0x0203,

    WM_USER = 0x0400,
    WM_TIMER = 0x0113,
}

public enum EWindowHooks : int
{
    WH_MOUSE_LL = 14,
}
