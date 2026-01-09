using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using W.TrayIcon.WPF.Native;

namespace W.TrayIcon.WPF;

/// <summary>
/// 
/// </summary>
public class TrayIconControl : Control
{
    private CancellationTokenSource _cancellationTokenSource = new();
    private nint _hWnd = nint.Zero;
    private readonly DispatcherTimer _clickTimer = new();

    private Popup? _popup;
    private bool _isHovering = false;

    protected NOTIFYICONDATA Data;

    public TrayIconControl()
    {
        Loaded += OnLoaded;
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

    ///// <summary>
    ///// Запущена ли задача проверки позиции мыши
    ///// </summary>
    //private bool _taskRunning = false;

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

    private static void OnIsShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrayIconControl control)
        {
            if (e.NewValue is bool show)
            {
                if (show)
                {
                    if (control.GetHandle() != nint.Zero)
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
        if (!_isHovering)
        {
            HideContextMenu();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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

        if (_popup != null)
        {
            if (_popup.Child is Border border)
            {
                border.Background = Background;
                border.SetValue(TextElement.ForegroundProperty, Foreground);
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        HideIcon();
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

    private void ShowIcon(nint hWnd)
    {
        var iconHandle = System.Drawing.SystemIcons.Application.Handle;

        Data = new NOTIFYICONDATA
        {
            uCallbackMessage = (int)EWindowMessages.WM_USER + 1,
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hWnd, // можно привязать к окну
            uID = 0,
            uFlags = ETrayIconFlags.NIF_ICON | ETrayIconFlags.NIF_TIP | ETrayIconFlags.NIF_MESSAGE,
            hIcon = iconHandle,
            //guidItem = _uniqueNumber,
            //szTip = ToolTip?.ToString() ?? string.Empty
        };
        NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_ADD, ref Data);

        GlobalMouseHook.Start();
        GlobalMouseHook.OnMouseClicked += GlobalMouseHook_OnMouseClicked;

        _cancellationTokenSource = new CancellationTokenSource();
        _ = Task.Factory.StartNew(CheckMouseMove, TaskCreationOptions.LongRunning);

        _hWnd = GetHandle();

        var source = HwndSource.FromHwnd(_hWnd);
        source?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((EWindowMessages)msg == EWindowMessages.WM_USER + 1) // наш callback
        {
            var windowMessage = (EWindowMessages)lParam.ToInt32();

            switch (windowMessage)
            {
                case EWindowMessages.WM_MOUSEMOVE:
                    //Debug.WriteLine("TrayIcon MouseMove");

                    if (ContextMenu == null || (ContextMenu != null && !ContextMenu.IsOpen))
                    {
                        if (!_isHovering)
                        {
                            _isHovering = true;
                        }
                    }
                    else
                    {
                        //_isHovering = false;
                        OnTrayMouseLeave();
                    }

                    //OnTrayMouseEnter();
                    break;
                case EWindowMessages.WM_RBUTTONUP:
                    break;
                case EWindowMessages.WM_RBUTTONDOWN:
                    if (ContextMenu != null)
                    {
                        ContextMenu.IsOpen = false;
                        if (ContextMenu?.IsOpen == false)
                        {
                            OnTrayMouseLeave();

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

        return nint.Zero;
    }

    private void HandleSingleClick()
    {
        HideContextMenu();

        LeftClickCommand?.Execute(null);
    }

    private void HideContextMenu()
    {
        Dispatcher.Invoke(() =>
        {
            if (ContextMenu != null)
            {
                // закрыть меню, если оно открыто
                if (ContextMenu?.IsOpen == true)
                {
                    ContextMenu.IsOpen = false;
                }
            }
        });
    }

    private async Task CheckMouseMove()
    {
        try
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var hasToolTipMode = false;

                await Dispatcher.BeginInvoke(() =>
                {
                    hasToolTipMode = (ContextMenu == null || (ContextMenu != null && !ContextMenu.IsOpen));
                });

                if (_hWnd != nint.Zero)
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
                            if (_popup != null)
                            {
                                _popup.HorizontalOffset = (IconPosition.Left + (IconPosition.Right - IconPosition.Left) / 2) - ((FrameworkElement)_popup.Child).ActualWidth / 2;
                                _popup.VerticalOffset = IconPosition.Top - 50;
                            }
                        });

                        if (_isHovering)
                        {
                            if (!IsInnerTaskIcon())
                            {
                                //OnTrayMouseLeave();
                                _isHovering = false;
                                //continue;
                            }
                            //else
                            //{
                            //    //Debug.WriteLine("Mouse on TaskIcon -------------------------------------------------------------------");
                            //    //OnTrayMouseEnter();
                            //    //continue;
                            //    //_isHovering = true;
                            //}

                        }
                        //else
                        //{
                        //    //OnTrayMouseLeave();
                        //    //_isHovering = false;

                        //    //await Dispatcher.BeginInvoke(() =>
                        //    //{
                        //    //    if (ContextMenu != null)
                        //    //    {
                        //    //        // закрыть меню, если оно открыто
                        //    //        if (ContextMenu?.IsOpen == true)
                        //    //        {
                        //    //            ContextMenu.IsOpen = false;
                        //    //        }
                        //    //    }
                        //    //});

                        //    //continue;
                        //}
                    }
                    else
                    {
                        //Debug.WriteLine($"Ошибка: HRESULT = 0x{hr:X}");
                        _isHovering = false;
                        continue;
                    }
                }


                if (hasToolTipMode)
                {
                    if (_isHovering)
                    {
                        if (IsInnerTaskIcon())
                        {
                            //Debug.WriteLine("Mouse on TaskIcon -------------------------------------------------------------------");
                            OnTrayMouseEnter();
                            continue;
                            //_isHovering = true;
                        }
                        else
                        {
                            OnTrayMouseLeave();
                            //_isHovering = false;
                            continue;
                        }
                    }
                    else
                    {
                        OnTrayMouseLeave();
                        //_isHovering = false;

                        HideContextMenu();

                        continue;
                    }
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

    private async void OnTrayMouseEnter()
    {
        await Dispatcher.BeginInvoke(async () =>
        {
            // показать Popup

            if (_popup != null)
            {
                if (!_popup.IsOpen)
                {
                    _popup.IsOpen = true;
                    _popup.IsOpen = false;
                    _popup.Opacity = 0;

                    await Task.Delay(500);
                    _popup.Opacity = 100;
                    _popup.IsOpen = true;
                }
            }
        });
    }

    private void OnTrayMouseLeave()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (_popup != null)
            {
                if (_popup.IsOpen)
                {
                    await Task.Delay(150);
                    _popup.Opacity = 0;
                    _popup.IsOpen = false;
                }
            }
        });
    }

    private void InitPopup()
    {
        FrameworkElement? element;

        if (ToolTip is string tp)
        {
            element = new TextBlock
            {
                Text = tp,
                Background = Background,
                Foreground = Foreground
            };
        }
        else
        {
            element = (FrameworkElement)ToolTip;
        }

        var border = new Border
        {
            CornerRadius = CornerRadius,
            //BorderThickness = ,
            //BorderBrush = ,
            Background = Background,
            Padding = Padding,
            Child = element
        };

        border.SetValue(TextElement.ForegroundProperty, Foreground);

        _popup = new Popup
        {
            Placement = PlacementMode.AbsolutePoint,
            HorizontalOffset = IconPosition.Left,
            VerticalOffset = IconPosition.Top,
            StaysOpen = false,
            Opacity = 0,
            AllowsTransparency = true,
            Child = border
        };
    }

    private Window GetWindow()
    {
        return Window.GetWindow(this);
    }

    private nint GetHandle()
    {
        return new WindowInteropHelper(GetWindow()).Handle;
    }
}