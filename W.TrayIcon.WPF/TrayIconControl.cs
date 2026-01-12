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
using System.Windows.Threading;
using W.TrayIcon.WPF.Helpers;
using W.TrayIcon.WPF.Native;

namespace W.TrayIcon.WPF;

/// <summary>
/// 
/// </summary>
public class TrayIconControl : Control
{
    private CancellationTokenSource _cancellationTokenSource = new();

    private IntPtr _hWnd = IntPtr.Zero;

    private readonly DispatcherTimer _clickTimer = new();

    private HwndSource? _source = null;

    private Popup? _popup;
    private Border? _wrapper = null;

    private bool _isPopupInitialized = false;
    private bool _isHovering = false;

    protected NOTIFYICONDATA Data;

    public TrayIconControl()
    {
        Initialized += OnInitialized;
        Unloaded += OnUnloaded;


        Background = Brushes.White;
        Padding = new Thickness(10);


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

    private void GlobalMouseHook_OnMouseClicked()
    {
        if (!IsHovering())
        {
            HideContextMenu();
        }
    }

    /// <summary>
    /// Change color theme background and foreground tooltip
    /// </summary>
    private void ChangeColors()
    {
        bool isDark = IsDarkTheme();

        var bgDarkColor = "#FF282828";
        var bgLightColor = "#FFDADADA";
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

    private void HideIcon()
    {
        GlobalMouseHook.Stop();
        GlobalMouseHook.OnMouseClicked -= GlobalMouseHook_OnMouseClicked;

        _cancellationTokenSource.Cancel();

        _hWnd = GetHandle();

        var source = HwndSource.FromHwnd(_hWnd);
        source?.RemoveHook(WndProc);

        NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_DELETE, ref Data);
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

        GlobalMouseHook.Start();
        GlobalMouseHook.OnMouseClicked += GlobalMouseHook_OnMouseClicked;

        _cancellationTokenSource = new CancellationTokenSource();
        var checkMouseMoveTask = Task.Factory.StartNew(CheckMouseMove, cancellationToken: _cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        _hWnd = GetHandle();

        var source = HwndSource.FromHwnd(_hWnd);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((EWindowMessages)msg == EWindowMessages.WM_USER + 1) // наш callback
        {
            var windowMessage = (EWindowMessages)lParam.ToInt32();

            switch (windowMessage)
            {
                case EWindowMessages.WM_MOUSEMOVE:

                    if (ContextMenu == null || (ContextMenu != null && !ContextMenu.IsOpen))
                    {
                        if (!IsHovering())
                        {
                            SetIsHovering(true);
                        }
                    }
                    else
                    {
                        _ = OnTrayMouseLeave();
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

                    Debug.WriteLine("Double Click");
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

    private async void HideContextMenu()
    {
        await Dispatcher.Invoke(async () =>
        {
            if (ContextMenu != null)
            {
                if (ContextMenu?.IsOpen == true)
                {
                    await Task.Delay(1000);
                    ContextMenu.IsOpen = false;
                }
            }
        });
    }

    private async Task CheckMouseMove()
    {
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                var hasToolTipMode = false;

                await Dispatcher.BeginInvoke(() =>
                {
                    hasToolTipMode = (ContextMenu == null || (ContextMenu != null && !ContextMenu.IsOpen));
                });

                if (_hWnd != IntPtr.Zero)
                {
                    NOTIFYICONIDENTIFIER nii = new()
                    {
                        cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
                        hWnd = _hWnd,
                        uID = 0,
                    };

                    int hr = NativeMethods.Shell_NotifyIconGetRect(ref nii, out var _taskIconPosition);

                    IconPosition = _taskIconPosition;

                    if (hr == 0)
                    {
                        await Dispatcher.BeginInvoke(() =>
                        {
                            var popup = GetPopup();

                            if (popup != null && IsPopupInitialized())
                            {
                                popup.HorizontalOffset = (IconPosition.Left + (IconPosition.Right - IconPosition.Left) / 2) - ((FrameworkElement)popup.Child).ActualWidth / 2;
                                popup.VerticalOffset = IconPosition.Top - ((FrameworkElement)popup.Child).ActualHeight - 12;
                            }
                        });

                        if (IsHovering())
                        {
                            if (!IsInnerTaskIcon())
                            {
                                SetIsHovering(false);
                            }

                        }
                    }
                    else
                    {
                        SetIsHovering(false);
                        continue;
                    }
                }


                if (hasToolTipMode)
                {
                    if (IsHovering())
                    {
                        if (IsInnerTaskIcon())
                        {
                            await OnTrayMouseEnter();
                            continue;
                        }
                        else
                        {
                            await OnTrayMouseLeave();
                            continue;
                        }
                    }
                    else
                    {
                        await OnTrayMouseLeave();
                        HideContextMenu();

                        continue;
                    }
                }
                else
                {
                    await OnTrayMouseLeave();
                }

                await Task.Delay(100);
            }
        }
        catch (Exception)
        {
            //ignore
            //throw;
        }
    }

    private bool IsInnerTaskIcon()
    {
        return GlobalMouseHook.MousePosition.x >= IconPosition.Left &&
                            GlobalMouseHook.MousePosition.x <= IconPosition.Right &&
                            GlobalMouseHook.MousePosition.y >= IconPosition.Top &&
                            GlobalMouseHook.MousePosition.y <= IconPosition.Bottom;
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
                    await Task.Delay(250);
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

        if (ToolTip is string toolTip)
        {
            var binding = BindingOperations.GetBinding(this, ToolTipProperty);
            if (binding != null)
            {
                Debug.WriteLine("Found binding ToolTipProperty");

                var textBox = new TextBlock
                {
                    DataContext = DataContext
                };

                textBox.SetBinding(TextBlock.TextProperty, binding);

                element = textBox;
            }
            else
            {
                Debug.WriteLine("Binding not found");

                element = new TextBlock
                {
                    Text = toolTip
                };
            }
        }
        else
        {
            element = (FrameworkElement?)ToolTip;

            if (element != null)
            {
                element.DataContext = DataContext;
            }
        }

        if (ToolTip == null)
        {
            ToolTip = GetWindow()?.Title;
        }

        var wrapper = GetWrapper();

        if (wrapper != null)
        {
            wrapper.Child = null;
            wrapper = null;
        }

        wrapper = new Border
        {
            DataContext = DataContext,
            CornerRadius = CornerRadius,
            //BorderThickness = ,
            //BorderBrush = ,
            Background = Background,
            Padding = Padding,
            Child = element
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