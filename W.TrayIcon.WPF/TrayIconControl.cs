using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using W.TrayIcon.WPF.Helpers;
using W.TrayIcon.WPF.Native;

namespace W.TrayIcon.WPF;

/// <summary>
/// 
/// </summary>
public class TrayIconControl : Control
{
    private IntPtr _hWnd = IntPtr.Zero;

    private readonly GlobalMouseHook _hook = new();

    private readonly DispatcherTimer _clickTimer = new();

    private DispatcherTimer _leaveTimer = new();

    private HwndSource? _source = null;

    private Popup? _popup;
    private Border? _wrapper = null;

    private bool _isPopupInitialized = false;
    private bool _isHovering = false;
    private bool _isInContextMenu = false;
    private DateTime _lastTime = DateTime.UtcNow;

    protected NOTIFYICONDATA Data;

    private DispatcherTimer _idleTimer = new();

    public TrayIconControl()
    {
        Initialized += OnInitialized;
        Unloaded += OnUnloaded;

        Background = Brushes.White;
        // Default padding
        Padding = new Thickness(10, 8, 10, 8);
        BorderThickness = new Thickness(0.2);
        BorderBrush = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));

        _clickTimer.Interval = TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime());
        _clickTimer.Tick += (s, e) =>
        {
            _clickTimer.Stop();
            HandleSingleClick();
        };
    }

    /// <summary>
    /// Hovering mouse inside tray icon
    /// </summary>
    /// <returns></returns>
    public bool IsHovering() => _isHovering;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="isHovering"></param>
    public void SetIsHovering(bool isHovering) => _isHovering = isHovering;

    /// <summary>
    /// Initialized popup?
    /// </summary>
    /// <returns></returns>
    public bool IsPopupInitialized() => _isPopupInitialized;

    /// <summary>
    /// Get instance tooltip popup
    /// </summary>
    /// <returns></returns>
    public Popup? GetPopup() => _popup;

    /// <summary>
    /// Set instance tooltip popup
    /// </summary>
    /// <returns></returns>
    public void SetPopup(Popup popup) => _popup = popup;

    /// <summary>
    /// Set main wrapper tooltip 
    /// </summary>
    /// <returns></returns>
    public Border? GetWrapper() => _wrapper;

    public void SetWrapper(Border? wrapper) => _wrapper = wrapper;

    private void OnInitialized(object? sender, EventArgs e)
    {
        if (!DesignerProperties.GetIsInDesignMode(new DependencyObject()))
        {
            // создаём скрытое окно‑хост
            _source = new HwndSource(new HwndSourceParameters("HiddenHost")
            {
                WindowStyle = 0x800000, // WS_OVERLAPPED
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0
            });

            _hWnd = _source.Handle;

            var wnd = GetWindow();
            DataContext = wnd?.DataContext;

            if (wnd != null)
            {
                wnd.DataContextChanged += Window_DataContextChanged;
            }

            var root = new FrameworkElement();
            _source.RootVisual = root;

            if (!DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                SystemEvents.UserPreferenceChanged += (s, e) =>
                {
                    if (e.Category == UserPreferenceCategory.General ||
                        e.Category == UserPreferenceCategory.Color)
                    {
                        ChangeColors();
                    }
                };

                InitPopup();
                ChangeColors();

                _hWnd = GetHandle();

                if (IsShow)
                {
                    ShowIcon(_hWnd);
                }
            }
        }
    }

    private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_source != null)
        {
            if (_source.RootVisual is FrameworkElement element)
            {
                DataContext = GetWindow()?.DataContext;
                element.DataContext = DataContext;
                InitPopup();
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    protected RECT IconPosition { get; set; }

    /// <summary>
    /// Скрыть или показать трей иконку
    /// </summary>
    public bool IsShow
    {
        get => (bool)GetValue(IsShowProperty);
        set => SetValue(IsShowProperty, value);
    }

    // Using a DependencyProperty as the backing store for IsShow.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IsShowProperty =
        DependencyProperty.Register(nameof(IsShow), typeof(bool), typeof(TrayIconControl), new PropertyMetadata(false, OnIsShowChanged));

    /// <summary>
    /// 
    /// </summary>
    /// <param name="d"></param>
    /// <param name="e"></param>
    private static void OnIsShowChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
    {
        if (dp is TrayIconControl control)
        {
            if (e.NewValue is bool show)
            {
                if (show)
                {
                    if (control.GetHandle() != IntPtr.Zero)
                    {
                        control.ShowIcon(control.GetHandle());
                    }
                }
                else
                {
                    control.HideIcon();
                }
            }
        }
    }

    public object? TrayToolTip
    {
        get => (object?)GetValue(TrayToolTipProperty);
        set => SetValue(TrayToolTipProperty, value);
    }

    // Using a DependencyProperty as the backing store for TrayToolTip.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty TrayToolTipProperty =
        DependencyProperty.Register(nameof(TrayToolTip), typeof(object), typeof(TrayIconControl), new PropertyMetadata(null));


    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    // Using a DependencyProperty as the backing store for Icon.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(TrayIconControl), new PropertyMetadata(null));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    // Using a DependencyProperty as the backing store for CornerRadiusProperty.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(TrayIconControl), new PropertyMetadata(new CornerRadius(5)));

    public ICommand? LeftClickCommand
    {
        get => (ICommand)GetValue(LeftClickCommandProperty);
        set => SetValue(LeftClickCommandProperty, value);
    }

    // Using a DependencyProperty as the backing store for MyProperty.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty LeftClickCommandProperty =
        DependencyProperty.Register(nameof(LeftClickCommand), typeof(ICommand), typeof(TrayIconControl));

    public ICommand? RightClickCommand
    {
        get => (ICommand)GetValue(RightClickCommandProperty);
        set => SetValue(RightClickCommandProperty, value);
    }

    // Using a DependencyProperty as the backing store for MyProperty.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty RightClickCommandProperty =
        DependencyProperty.Register(nameof(RightClickCommand), typeof(ICommand), typeof(TrayIconControl));


    public ICommand? DoubleClickCommand
    {
        get => (ICommand)GetValue(DoubleClickCommandProperty);
        set => SetValue(DoubleClickCommandProperty, value);
    }

    // Using a DependencyProperty as the backing store for DoubleClickCommand.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty DoubleClickCommandProperty =
        DependencyProperty.Register(nameof(DoubleClickCommand), typeof(ICommand), typeof(TrayIconControl));


    private bool IsDarkTheme()
    {
        var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        if (key != null)
        {
            var value = key.GetValue("AppsUseLightTheme");
            if (value is int intValue)
                return intValue == 0; // 0 = Dark, 1 = Light
        }
        return false;
    }

    private void GlobalMouseHook_OnMouseClicked(Native.MouseEventArgs mouseEventArgs)
    {
        if (!_isInContextMenu)
        {
            if (!IsHovering())
            {
                HideContextMenu();
            }
            else
            {
                if (mouseEventArgs.MouseEvent != EMouseEvent.RightMouseButtonDown && mouseEventArgs.MouseEvent != EMouseEvent.RightMouseButtonUp)
                {
                    HideContextMenu();
                }
            }
        }
    }

    /// <summary>
    /// Change color theme background and foreground tooltip
    /// </summary>
    private void ChangeColors()
    {
        bool isDark = IsDarkTheme();

        var bgDarkColor = "#FF2E2E2E";
        var bgLightColor = "#FFF8F8F8";
        var fgDarkColor = "#FFFFFFFF";
        var fgLightColor = "#FF000000";

        var bg = isDark ? (Color)ColorConverter.ConvertFromString(bgDarkColor) : (Color)ColorConverter.ConvertFromString(bgLightColor);
        var fg = isDark ? (Color)ColorConverter.ConvertFromString(fgDarkColor) : (Color)ColorConverter.ConvertFromString(fgLightColor);

        Background = new SolidColorBrush(bg);
        Foreground = new SolidColorBrush(fg);

        var wrapper = GetWrapper();

        if (wrapper != null)
        {
            wrapper.Background = Background;
            wrapper.SetValue(TextElement.ForegroundProperty, Foreground);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        HideIcon();
        Initialized -= OnInitialized;

        if (_hWnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hWnd);
        }

        var wnd = GetWindow();

        if (wnd != null)
        {
            wnd.DataContextChanged -= Window_DataContextChanged;
        }
    }

    /// <summary>
    /// CheckCursor under tray
    /// </summary>
    /// <returns></returns>
    private bool IsCursorInMainTray()
    {
        // основное окно трея
        IntPtr trayWnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (trayWnd == IntPtr.Zero) return false;

        // область уведомлений
        IntPtr notifyWnd = NativeMethods.FindWindowEx(trayWnd, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notifyWnd == IntPtr.Zero) return false;

        // прямоугольник области
        if (!NativeMethods.GetWindowRect(notifyWnd, out RECT rect)) return false;

        // позиция курсора
        NativeMethods.GetCursorPos(out POINT pt);

        return pt.x >= rect.Left && pt.x <= rect.Right &&
               pt.y >= rect.Top && pt.y <= rect.Bottom;
    }

    private void HideIcon()
    {
        _hook.Stop();
        _hook.OnMouseClicked -= GlobalMouseHook_OnMouseClicked;
        _hook.OnMouseMove -= GlobalMouseHook_OnMouseMove;
        _hook.OnMouseStop -= GlobalMouseHook_OnMouseStop;

        _hWnd = GetHandle();

        var source = HwndSource.FromHwnd(_hWnd);
        source?.RemoveHook(WndProc);

        NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_DELETE, ref Data);
    }

    private bool _inside = false;

    private void GlobalMouseHook_OnMouseMove(MouseMoveEventArgs mouseEventArgs)
    {
        if (_hWnd != IntPtr.Zero)
        {
            NOTIFYICONIDENTIFIER nii = new()
            {
                cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                hWnd = _hWnd,
                uID = 0,
            };

            var hr = NativeMethods.Shell_NotifyIconGetRect(ref nii, out var taskIconPosition);

            if (hr == 0)
            {
                IconPosition = taskIconPosition;

                if (IsInnerTaskIcon())
                {
                    if (IsCursorInMainTray())
                    {
                        _inside = !_inside;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Add icon to the system tray (Taskbar)
    /// </summary>
    /// <param name="hWnd"></param>
    private void ShowIcon(IntPtr hWnd)
    {
        IntPtr iconHandle = IntPtr.Zero;

        if (Icon != null)
        {
            var icon = IconHelper.GetIconFromImageSource(Icon, 32);
            iconHandle = icon.Handle;
        }
        else
        {
            var module = Process.GetCurrentProcess().MainModule;

            if (module != null)
            {
                var exePath = module.FileName;

                if (!string.IsNullOrEmpty(exePath))
                {
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);

                    if (icon != null)
                    {
                        iconHandle = icon.Handle;
                    }
                }
            }
        }

        Data = new NOTIFYICONDATA
        {
            uCallbackMessage = (int)EWindowMessages.WM_USER + 1,
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hWnd,
            uID = 0,
            uFlags = ETrayIconFlags.NIF_ICON | ETrayIconFlags.NIF_MESSAGE,
            hIcon = iconHandle
        };

        NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_ADD, ref Data);

        _hook.Start();
        _hook.OnMouseClicked += GlobalMouseHook_OnMouseClicked;
        _hook.OnMouseMove += GlobalMouseHook_OnMouseMove;
        _hook.OnMouseStop += GlobalMouseHook_OnMouseStop;

        _idleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };

        _idleTimer.Tick += Timer_Tick;
        _idleTimer.Start();

        _hWnd = GetHandle();

        var source = HwndSource.FromHwnd(_hWnd);
        source?.AddHook(WndProc);

        if(ContextMenu != null)
        {
            ContextMenu.Opened += ContextMenu_Opened;
            ContextMenu.Closed += ContextMenu_Closed;
        }
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _leaveTimer?.Stop();
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = sender as ContextMenu;
        
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200)
        };
        
        menu?.BeginAnimation(OpacityProperty, animation);

        _leaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };

        _leaveTimer.Tick += (_, __) =>
        {
            var mousePosition = _hook.GetMousePosition();

            var hwndSrc = (HwndSource)PresentationSource.FromVisual(ContextMenu);

            if (hwndSrc == null) return;

            if (NativeMethods.GetWindowRect(hwndSrc.Handle, out RECT rect))
            {
                _isInContextMenu = mousePosition.X >= rect.Left && mousePosition.X <= rect.Right &&
                    mousePosition.Y >= rect.Top && mousePosition.Y <= rect.Bottom;
            }
        };

        _leaveTimer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var idleTime = DateTime.UtcNow - _lastTime;

        if (idleTime > TimeSpan.FromMilliseconds(5))
        {
            if (IsHovering() && !IsInnerTaskIcon())
            {
                SetIsHovering(false);

                _ = OnTrayMouseLeave();
            }
        }
    }

    private void GlobalMouseHook_OnMouseStop(MouseStopEventArgs mouseEventArgs)
    {
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((EWindowMessages)msg == EWindowMessages.WM_USER + 1) // наш callback
        {
            var windowMessage = (EWindowMessages)lParam.ToInt32();

            switch (windowMessage)
            {
                case EWindowMessages.WM_MOUSEMOVE:

                    _lastTime = DateTime.UtcNow;

                    if (!IsHovering())
                    {
                        SetIsHovering(true);

                        if (IsInnerTaskIcon())
                        {
                            if (ContextMenu == null || (ContextMenu != null && !ContextMenu.IsOpen))
                            {
                                if (IsPopupInitialized())
                                {
                                    _ = OnTrayMouseEnter();
                                }
                            }
                        }
                    }



                    break;
                case EWindowMessages.WM_RBUTTONUP:
                    break;
                case EWindowMessages.WM_RBUTTONDOWN:
                    if (ContextMenu != null)
                    {
                        ContextMenu.IsOpen = false;
                        if (ContextMenu?.IsOpen == false)
                        {
                            _ = OnTrayMouseLeave();
                            ContextMenu.IsOpen = true;
                        }
                    }

                    RightClickCommand?.Execute(null);
                    break;
                case EWindowMessages.WM_LBUTTONUP:
                    HideContextMenu();
                    break;
                case EWindowMessages.WM_LBUTTONDOWN:
                    _clickTimer.Start();
                    break;
                case EWindowMessages.WM_LBUTTONDBLCLK:
                    HideContextMenu();

                    _clickTimer.Stop();
                    DoubleClickCommand?.Execute(null);
                    break;
                case EWindowMessages.WM_MBUTTONUP:
                case EWindowMessages.WM_MBUTTONDOWN:
                    HideContextMenu();
                    break;
            }
        }

        return IntPtr.Zero;
    }

    private void HandleSingleClick()
    {
        HideContextMenu();

        LeftClickCommand?.Execute(null);
    }

    private void HideContextMenu()
    {
        if (!_isInContextMenu)
        {
            Dispatcher.Invoke(() =>
            {
                if (ContextMenu != null)
                {
                    if (ContextMenu?.IsOpen == true)
                    {
                        ContextMenu.IsOpen = false;
                    }
                }
            });
        }
    }

    private bool IsInnerTaskIcon()
    {
        var mousePosition = _hook.GetMousePosition();

        return mousePosition.X >= IconPosition.Left &&
                            mousePosition.X <= IconPosition.Right &&
                            mousePosition.Y >= IconPosition.Top &&
                            mousePosition.Y <= IconPosition.Bottom;
    }

    private async Task OnTrayMouseEnter()
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            var popup = GetPopup();

            if (popup != null)
            {
                if (!popup.IsOpen)
                {
                    var content = popup.Child;
                    content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Size desired = content.DesiredSize;

                    var dpi = VisualTreeHelper.GetDpi(popup);
                    double scale = dpi.DpiScaleX;

                    if (IsPopupInitialized())
                    {
                        var child = (FrameworkElement?)popup.Child;

                        if (child != null)
                        {
                            double iconCenter = (IconPosition.Left + (IconPosition.Right - IconPosition.Left) / 2) / scale;

                            popup.HorizontalOffset = iconCenter - (desired.Width - child.Margin.Left - child.Margin.Right) / 2;
                            popup.VerticalOffset = (IconPosition.Top / scale) - desired.Height + child.Margin.Top + child.Margin.Bottom - 12;
                        }
                    }

                    await Task.Delay(250);
                    popup.IsOpen = true;
                }
            }
        });
    }

    private async Task OnTrayMouseLeave()
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            var popup = GetPopup();

            if (popup != null)
            {
                if (popup.IsOpen)
                {
                    //await Task.Delay(100);
                    popup.IsOpen = false;
                }
            }
        });
    }

    private void InitPopup()
    {
        _isPopupInitialized = false;

        var popup = GetPopup();

        if (popup != null)
        {
            popup.Child = null;
        }

        FrameworkElement? element;

        if (TrayToolTip is string toolTip)
        {
            var binding = BindingOperations.GetBinding(this, TrayToolTipProperty);
            if (binding != null)
            {
                var textBox = new TextBlock
                {
                    DataContext = DataContext
                };

                textBox.SetBinding(TextBlock.TextProperty, binding);

                element = textBox;
            }
            else
            {
                element = new TextBlock
                {
                    Text = toolTip
                };
            }
        }
        else
        {
            element = (FrameworkElement?)TrayToolTip;

            if (element != null)
            {
                element.DataContext = DataContext;
            }
        }

        if (TrayToolTip == null)
        {
            TrayToolTip = GetWindow()?.Title;
        }

        var wrapper = GetWrapper();

        if (wrapper != null)
        {
            wrapper.Child = null;
            wrapper = null;
        }

        wrapper = new Border
        {
            BorderThickness = BorderThickness,
            BorderBrush = BorderBrush,
            DataContext = DataContext,
            CornerRadius = CornerRadius,
            Background = Background,
            Padding = Padding,
            Child = element,
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = Color.FromArgb(150, 0, 0, 0),
                BlurRadius = 0.5,
                ShadowDepth = 0.2,
                Direction = 250,
                Opacity = 0.2
            }
        };

        wrapper.SetValue(TextElement.ForegroundProperty, Foreground);

        SetWrapper(wrapper);

        popup = new Popup
        {
            DataContext = DataContext,
            Placement = PlacementMode.AbsolutePoint,
            HorizontalOffset = IconPosition.Left,
            VerticalOffset = IconPosition.Top,
            StaysOpen = true,
            Opacity = 100,
            PopupAnimation = PopupAnimation.Fade,
            AllowsTransparency = true,
            Child = GetWrapper()
        };

        SetPopup(popup);

        if (ContextMenu != null)
        {
            ContextMenu.DataContext = DataContext;
        }

        _isPopupInitialized = true;
    }

    private Window? GetWindow()
    {
        return Window.GetWindow(this);
    }

    private nint GetHandle()
    {
        return _hWnd;
    }
}