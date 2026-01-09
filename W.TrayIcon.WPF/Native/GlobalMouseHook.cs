using System.Diagnostics;
using System.Runtime.InteropServices;

namespace W.TrayIcon.WPF.Native;

public delegate void MouseClickEvent();

public delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

/// <summary>
/// Global Hook class for mouse events
/// </summary>
public class GlobalMouseHook
{
    private static nint _hookId = nint.Zero;

    private static readonly LowLevelMouseProc _proc = HookCallback;

    private static MouseClickEvent? _onMouseClicked;

    /// <summary>
    /// MouseClick
    /// </summary>
    public static event MouseClickEvent OnMouseClicked
    {
        add => _onMouseClicked += value;
        remove => _onMouseClicked -= value;
    }

    private static DateTime _lastTime = DateTime.Now;

    /// <summary>
    /// 
    /// </summary>
    private static bool _isTaskRunning = false;

    /// <summary>
    /// 
    /// </summary>
    private static bool _isMoving = false;

    /// <summary>
    /// 
    /// </summary>
    public static POINT MousePosition { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public static void Start()
    {
        _hookId = SetHook(_proc);
        _isTaskRunning = true;
        Task.Factory.StartNew(CheckMouseMove, TaskCreationOptions.LongRunning);
    }

    public static void Stop()
    {
        _isTaskRunning = false;
        NativeMethods.UnhookWindowsHookEx(_hookId);
    }

    private static async Task CheckMouseMove()
    {
        while (_isTaskRunning)
        {
            await Task.Delay(100);

            if (_isMoving)
            {
                if ((DateTime.Now - _lastTime).TotalMilliseconds > 50)
                {
                    _isMoving = false;
                    //Debug.WriteLine("Move Stop");
                }
            }
        }
    }

    private static nint SetHook(LowLevelMouseProc proc)
    {
        using Process curProcess = Process.GetCurrentProcess() ?? throw new Exception();
        if (curProcess.MainModule == null) throw new Exception();

        using ProcessModule curModule = curProcess.MainModule;

        return NativeMethods.SetWindowsHookEx(EWindowHooks.WH_MOUSE_LL, proc, NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
    }

    private static nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var wMessageParam = (EWindowMessages)wParam.ToInt32();

            switch (wMessageParam)
            {
                case EWindowMessages.WM_MOUSEMOVE:
                    {
                        _isMoving = true;
                        _lastTime = DateTime.Now;

                        MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                        MousePosition = hookStruct.pt;

                        break;
                    }
                case EWindowMessages.WM_RBUTTONDOWN:
                case EWindowMessages.WM_LBUTTONDOWN:
                case EWindowMessages.WM_MBUTTONDOWN:
                    {
                        _onMouseClicked?.Invoke();
                        break;
                    }
            }

            //Debug.WriteLine($"Global window message - {wMessageParam}");
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}