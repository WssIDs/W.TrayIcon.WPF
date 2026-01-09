namespace W.TrayIcon.WPF.Native;

public enum ETrayIconMessage
{
    NIM_ADD = 0x00000000,
    NIM_DELETE = 0x00000002,
}

[Flags]
public enum ETrayIconFlags
{
    NIF_MESSAGE = 0x00000001,
    NIF_ICON = 0x00000002,
    NIF_TIP = 0x00000004,
}