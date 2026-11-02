using System.Diagnostics;
using System.Windows.Threading;

namespace W.TrayIcon.WPF.Native;

public enum EMouseEvent
{
    None,
    MouseStop,
    MouseMove,
    LeftMouseButtonUp,
    LeftMouseButtonDown,
    RightMouseButtonUp,
    RightMouseButtonDown,
    MiddleMouseButtonUp,
    MiddleMouseButtonDown
}

public class MouseEventArgs
{
    public EMouseEvent MouseEvent { get; init; }
}
public class MouseMoveEventArgs : MouseEventArgs
{
    public Point Point { get; set; }
}

public abstract class MouseStopEventArgs : MouseMoveEventArgs
{
    /// <summary>
    /// 
    /// </summary>
    public DateTime LastMoveTime { get; set; }
}

public struct Point
{
    public int X { get; set; }

    public int Y { get; set; }
}

public delegate void MouseClickEvent(MouseEventArgs mouseEventArgs);

public delegate void MouseMoveEvent(MouseMoveEventArgs mouseEventArgs);

public delegate void MouseStopEvent(MouseStopEventArgs mouseEventArgs);

public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

/// <summary>
/// Global Hook class for mouse events
/// </summary>
public class GlobalMouseHook
{
    private IntPtr _hookId = IntPtr.Zero;

    private readonly LowLevelMouseProc _proc;
    
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public GlobalMouseHook() => _proc = HookCallback;

    private MouseClickEvent? _onMouseClicked;

    /// <summary>
    /// MouseClick
    /// </summary>
    public event MouseClickEvent OnMouseClicked
    {
        add => _onMouseClicked += value;
        remove => _onMouseClicked -= value;
    }

    /// <summary>
    /// Start Hook Process
    /// </summary>
    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;   // идемпотентно
        _hookId = SetHook(_proc);
    }

    /// <summary>
    /// Start Hook Process
    /// </summary>
    public void Stop()
    {
        if (_hookId == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    private nint SetHook(LowLevelMouseProc proc)
    {
        using var curProcess = Process.GetCurrentProcess() ?? throw new Exception();
        if (curProcess.MainModule == null) throw new Exception();

        using var curModule = curProcess.MainModule;

        return curModule.ModuleName == null
            ? throw new Exception()
            : NativeMethods.SetWindowsHookEx(EWindowHooks.WH_MOUSE_LL, proc,
                NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
    }

    private nint HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        
        var ev = (EWindowMessages)(int)wParam switch
        {
            EWindowMessages.WM_LBUTTONDOWN => EMouseEvent.LeftMouseButtonDown,
            EWindowMessages.WM_RBUTTONDOWN => EMouseEvent.RightMouseButtonDown,
            EWindowMessages.WM_MBUTTONDOWN => EMouseEvent.MiddleMouseButtonDown,
            _ => EMouseEvent.None
        };

        if (ev != EMouseEvent.None)
        {
            // Ничего тяжёлого в самом хуке
            _dispatcher.BeginInvoke(DispatcherPriority.Input,
                new Action(() => _onMouseClicked?.Invoke(new MouseEventArgs { MouseEvent = ev })));
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}