using System.Diagnostics;
using System.Runtime.InteropServices;
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
    public EMouseEvent MouseEvent { get; set; }
}
public class MouseMoveEventArgs : MouseEventArgs
{
    public Point Point { get; set; }
}

public class MouseStopEventArgs : MouseMoveEventArgs
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

    private bool _isStopped = false;

    private DateTime? _lastTime = DateTime.UtcNow;

    private DispatcherTimer _idleTimer = new();

    /// <summary>
    /// 
    /// </summary>
    private Point _mousePosition;

    public GlobalMouseHook()
    {
        _proc = HookCallback;
    }

    private MouseClickEvent? _onMouseClicked;

    /// <summary>
    /// MouseClick
    /// </summary>
    public event MouseClickEvent OnMouseClicked
    {
        add => _onMouseClicked += value;
        remove => _onMouseClicked -= value;
    }

    private MouseMoveEvent? _onMouseMove;

    /// <summary>
    /// MouseMove
    /// </summary>
    public event MouseMoveEvent OnMouseMove
    {
        add => _onMouseMove += value;
        remove => _onMouseMove -= value;
    }

    private MouseStopEvent? _onMouseStop;

    /// <summary>
    /// MouseStop
    /// </summary>
    public event MouseStopEvent OnMouseStop
    {
        add => _onMouseStop += value;
        remove => _onMouseStop -= value;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public Point GetMousePosition() => _mousePosition;

    /// <summary>
    /// Start Hook Process
    /// </summary>
    public void Start()
    {
        _hookId = SetHook(_proc);

        _idleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };

        _idleTimer.Tick += Timer_Tick;
        _idleTimer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var idleTime = DateTime.UtcNow - _lastTime;

        if (idleTime > TimeSpan.FromMilliseconds(100))
        {
            if (!_isStopped)
            {
                _isStopped = true;

                var mouseEvent = new MouseStopEventArgs
                {
                    MouseEvent = EMouseEvent.MouseStop,
                    LastMoveTime = _lastTime ?? DateTime.UtcNow,
                    Point = _mousePosition
                };

                _onMouseStop?.Invoke(mouseEvent);
            }
        }
    }

    /// <summary>
    /// Start Hook Process
    /// </summary>
    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
        }

        _idleTimer.Stop();
        _idleTimer.Tick -= Timer_Tick;
    }

    private nint SetHook(LowLevelMouseProc proc)
    {
        using Process curProcess = Process.GetCurrentProcess() ?? throw new Exception();
        if (curProcess.MainModule == null) throw new Exception();

        using ProcessModule curModule = curProcess.MainModule;

        if (curModule.ModuleName == null) throw new Exception();

        return NativeMethods.SetWindowsHookEx(EWindowHooks.WH_MOUSE_LL, proc, NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
    }

    private nint HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var wMessageParam = (EWindowMessages)wParam.ToInt32();

            switch (wMessageParam)
            {
                case EWindowMessages.WM_MOUSEMOVE:
                    {
                        _isStopped = false;
                        _lastTime = DateTime.UtcNow;

                        MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                        var mousePos = new Point
                        {
                            X = hookStruct.pt.x,
                            Y = hookStruct.pt.y
                        };

                        _mousePosition = mousePos;

                        var mouseEvent = new MouseMoveEventArgs
                        {
                            MouseEvent = EMouseEvent.MouseMove,
                            Point = mousePos
                        };

                        _onMouseMove?.Invoke(mouseEvent);

                        break;
                    }
                case EWindowMessages.WM_RBUTTONDOWN:
                case EWindowMessages.WM_LBUTTONDOWN:
                case EWindowMessages.WM_MBUTTONDOWN:
                    {
                        var eventType = EMouseEvent.None;

                        switch (wMessageParam)
                        {
                            case EWindowMessages.WM_RBUTTONDOWN:
                                eventType = EMouseEvent.RightMouseButtonDown;
                                break;
                            case EWindowMessages.WM_LBUTTONDOWN:
                                eventType = EMouseEvent.LeftMouseButtonDown;
                                break;
                            case EWindowMessages.WM_MBUTTONDOWN:
                                eventType = EMouseEvent.MiddleMouseButtonDown;
                                break;
                            case EWindowMessages.WM_LBUTTONUP:
                                eventType = EMouseEvent.LeftMouseButtonUp;
                                break;
                            case EWindowMessages.WM_RBUTTONUP:
                                eventType = EMouseEvent.RightMouseButtonUp;
                                break;
                            case EWindowMessages.WM_MBUTTONUP:
                                eventType = EMouseEvent.MiddleMouseButtonUp;
                                break;
                        }

                        _onMouseClicked?.Invoke(new MouseEventArgs
                        {
                            MouseEvent = eventType,
                        });

                        break;
                    }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}