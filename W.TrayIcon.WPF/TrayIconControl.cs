using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using W.TrayIcon.WPF.Helpers;
using W.TrayIcon.WPF.Native;
using W.TrayIcon.WPF.Statics;

namespace W.TrayIcon.WPF;

/// <summary>
/// 
/// </summary>
public class TrayIconControl : Control
{
    private IntPtr _hWnd = IntPtr.Zero;

    private readonly GlobalMouseHook _hook = new();

    private readonly DispatcherTimer _clickTimer = new();

    private HwndSource? _source;

    private Popup? _popup;
    private Border? _wrapper;

    private bool _isPopupInitialized;
    private bool _isHovering;

    private NOTIFYICONDATA _data;
    
    private readonly DispatcherTimer _hoverWatch = new() { Interval = TimeSpan.FromMilliseconds(100) };
    
    private bool _isIconShown;

    private System.Drawing.Icon? _trayIcon;
    
    private uint _taskbarCreatedMsg;
    
    private Window? _ownerWindow;
        
    private bool _isCleanedUp;
    
    // Popup создаётся в коде и живёт в отдельном HWND, поэтому не входит в дерево окна:
    // ресурсы темы (ThemeMode=System, DynamicResource) до него не доходят и при смене темы не обновляются.
    // Делаем popup логическим потомком контрола, тогда поиск ресурсов и их инвалидация идут и через него.
    private readonly List<object> _logicalChildren = new();
 
    protected override System.Collections.IEnumerator LogicalChildren => _logicalChildren.GetEnumerator();
    
    /// <summary>
    /// Постоянный идентификатор иконки. Рекомендуется задавать явно в приложении-потребителе.
    /// Если не задан, вычисляется из пути к exe хост-приложения.
    /// </summary>
    public Guid IconGuid { get; set; } = Guid.Empty;

    private bool _useGuid;
    
    public TrayIconControl()
    {
        Initialized += OnInitialized;
        Unloaded += OnUnloaded;

        Background = Application.Current.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush;
        Foreground = Application.Current.TryFindResource("TextFillColorPrimaryBrush") as Brush;
        // Default padding
        Padding = new Thickness(10, 8, 10, 8);
        BorderThickness = new Thickness(0.2);
        BorderBrush = Application.Current.TryFindResource("ControlElevationBorderBrush") as Brush;

        _clickTimer.Interval = TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime());
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            HandleSingleClick();
        };

        _hoverWatch.Tick += (_, _) =>
        {
            if (IsInnerTaskIcon()) return;

            _hoverWatch.Stop();
            SetIsHovering(false);
            ClosePopup();
        };
    }

    private static Guid CreateGuidFromExePath()
    {
        string? path = null;

        try
        {
            using var process = Process.GetCurrentProcess();
            path = process.MainModule?.FileName;
        }
        catch
        {
            // игнорируем, пойдём по запасному пути
        }

        // Запуск через "dotnet app.dll": MainModule указывает на dotnet.exe,
        // а он общий для всех приложений
        if (string.IsNullOrEmpty(path) ||
            Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            path = Assembly.GetEntryAssembly()?.Location;
        }

        if (string.IsNullOrEmpty(path))
        {
            path = "W.TrayIcon.WPF";
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));

        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }
    
    /// <summary>
    /// Hovering mouse inside tray icon
    /// </summary>
    /// <returns></returns>
    private bool IsHovering() => _isHovering;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="isHovering"></param>
    private void SetIsHovering(bool isHovering) => _isHovering = isHovering;

    /// <summary>
    /// Initialized popup?
    /// </summary>
    /// <returns></returns>
    private bool IsPopupInitialized() => _isPopupInitialized;

    /// <summary>
    /// Get instance tooltip popup
    /// </summary>
    /// <returns></returns>
    private Popup? GetPopup() => _popup;

    /// <summary>
    /// Set instance tooltip popup
    /// </summary>
    /// <returns></returns>
    private void SetPopup(Popup popup) => _popup = popup;

    /// <summary>
    /// Set main wrapper tooltip 
    /// </summary>
    /// <returns></returns>
    private Border? GetWrapper() => _wrapper;

    private void SetWrapper(Border? wrapper) => _wrapper = wrapper;

    private void OnInitialized(object? sender, EventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(new DependencyObject())) return;
        
        Dispatcher.ShutdownStarted += OnDispatcherShutdownStarted;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;                  // ДОБАВЛЕНО
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;    // ДОБАВЛЕНО
        
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
            
            _ownerWindow = wnd;
            wnd.Closed += OnOwnerClosed;
        }

        if (Application.Current != null)
        {
            Application.Current.Exit += OnAppExit;
        }
        
        _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        
        var root = new FrameworkElement();
        _source.RootVisual = root;

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        if (ContextMenu != null)
        {
            ContextMenu.Opened += ContextMenu_Opened;
            ContextMenu.Closed += ContextMenu_Closed;
        }

        InitPopup();
        ChangeColors();

        _hWnd = GetHandle();

        if (IsShow)
        {
            ShowIcon(_hWnd);
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            // Событие приходит НЕ из UI-потока, а ChangeColors трогает WPF-объекты
            // Низкий приоритет: WPF сначала сам переключит ресурсы темы (ThemeMode=System),
            // потом мы пересчитаем свои цвета, и они не перетрутся более ранним обновлением
            Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)ChangeColors);
        }
    }

    private void Window_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_source?.RootVisual is not FrameworkElement element) return;

        DataContext = GetWindow()?.DataContext;
        element.DataContext = DataContext;
        InitPopup();
    }

    /// <summary>
    /// 
    /// </summary>
    private RECT IconPosition { get; set; }

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
        DependencyProperty.Register(nameof(IsShow), typeof(bool), typeof(TrayIconControl),
            new PropertyMetadata(false, OnIsShowChanged));

    /// <summary>
    /// 
    /// </summary>
    /// <param name="dp"></param>
    /// <param name="e"></param>
    private static void OnIsShowChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
    {
        if (dp is not TrayIconControl control || e.NewValue is not bool show) return;

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

    public object? TrayToolTip
    {
        get => (object?)GetValue(TrayToolTipProperty);
        set => SetValue(TrayToolTipProperty, value);
    }

    // Using a DependencyProperty as the backing store for TrayToolTip.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty TrayToolTipProperty =
        DependencyProperty.Register(nameof(TrayToolTip), typeof(object), typeof(TrayIconControl),
            new PropertyMetadata(null));


    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    // Using a DependencyProperty as the backing store for Icon.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(TrayIconControl),
            new PropertyMetadata(null));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    // Using a DependencyProperty as the backing store for CornerRadiusProperty.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(TrayIconControl),
            new PropertyMetadata(new CornerRadius(5)));

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

    /// <summary>
    /// Заменяет GlobalMouseHook_OnMouseClicked.
    /// Подписка на хук существует только пока меню открыто.
    /// </summary>
    private void OnGlobalClick(Native.MouseEventArgs mouseEventArgs)
    {
        if (ContextMenu is not { IsOpen: true }) return;
 
        // Правый клик по самой иконке обрабатывает WndProc (WM_RBUTTONDOWN)
        if (IsHovering() && mouseEventArgs.MouseEvent == EMouseEvent.RightMouseButtonDown) return;
 
        // Клик внутри самого меню его не закрывает
        if (PresentationSource.FromVisual(ContextMenu) is HwndSource src &&
            NativeMethods.GetWindowRect(src.Handle, out var rect) &&
            NativeMethods.GetCursorPos(out var pt) &&
            pt.x >= rect.Left && pt.x <= rect.Right &&
            pt.y >= rect.Top && pt.y <= rect.Bottom)
        {
            return;
        }
 
        ContextMenu.IsOpen = false;
    }

    /// <summary>
    /// Change color theme background and foreground tooltip
    /// </summary>
    private void ChangeColors()
    {
        var isBoundAfter = BindingOperations.IsDataBound(this, BackgroundProperty);

        if (!isBoundAfter)
        {
            Background = Application.Current.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush;
        }
            
        isBoundAfter = BindingOperations.IsDataBound(this, ForegroundProperty);

        if (!isBoundAfter)
        {
            Foreground = Application.Current.TryFindResource("TextFillColorPrimaryBrush") as Brush;
        }
        
        isBoundAfter = BindingOperations.IsDataBound(this, BorderBrushProperty);

        if (!isBoundAfter)
        {
            BorderBrush = Application.Current.TryFindResource("ControlElevationBorderBrush") as Brush;
        }
        
        // var isDark = IsDarkTheme();
        //
        // const string bgDarkColor = "#FF2E2E2E";
        // const string bgLightColor = "#FFF8F8F8";
        // const string fgDarkColor = "#FFFFFFFF";
        // const string fgLightColor = "#FF000000";
        //
        // var bg = isDark ? (Color)ColorConverter.ConvertFromString(bgDarkColor) : (Color)ColorConverter.ConvertFromString(bgLightColor);
        // var fg = isDark ? (Color)ColorConverter.ConvertFromString(fgDarkColor) : (Color)ColorConverter.ConvertFromString(fgLightColor);
        //
        // Background = new SolidColorBrush(bg);
        // Foreground = new SolidColorBrush(fg);
 
        var wrapper = GetWrapper();

        if (wrapper == null) return;
        
        wrapper.Background = Background;
        wrapper.SetValue(TextElement.ForegroundProperty, Foreground);
    }

    private void OnProcessExit(object? sender, EventArgs e) => EmergencyRemoveIcon();

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) => EmergencyRemoveIcon();

    /// <summary>
    /// Аварийное удаление иконки. Эти события могут прийти не из UI-потока,
    /// поэтому здесь нельзя трогать WPF-объекты (Popup, таймеры, HwndSource).
    /// Только Shell_NotifyIcon с уже готовой структурой _data.
    /// </summary>
    private void EmergencyRemoveIcon()
    {
        if (!_isIconShown) return;
        _isIconShown = false;

        NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_DELETE, ref _data);
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e) { }

    private void OnDispatcherShutdownStarted(object? sender, EventArgs e) => Cleanup();
    
    private void OnOwnerClosed(object? sender, EventArgs e) => Cleanup();
    
    private void OnAppExit(object sender, ExitEventArgs e) => Cleanup();
    
    private void Cleanup()
    {
        if (_isCleanedUp) return;
        _isCleanedUp = true;
 
        HideIcon();
        Initialized -= OnInitialized;
 
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
 
        if (Application.Current != null)
        {
            Application.Current.Exit -= OnAppExit;
        }
 
        if (ContextMenu != null)
        {
            ContextMenu.Opened -= ContextMenu_Opened;
            ContextMenu.Closed -= ContextMenu_Closed;
        }
 
        _clickTimer.Stop();
 
        _source?.Dispose();
        _source = null;
        _hWnd = IntPtr.Zero;

        if (_ownerWindow == null) return;
        
        _ownerWindow.DataContextChanged -= Window_DataContextChanged;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;                  // ДОБАВЛЕНО
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;    // ДОБАВЛЕНО
        _ownerWindow.Closed -= OnOwnerClosed;
        _ownerWindow = null;
    }
    
    private void Wrapper_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_popup is { IsOpen: true })
        {
            PositionPopup();
        }
    }
    
    private void HideIcon()
    {
        if (!_isIconShown) return;
        _isIconShown = false;
 
        _hoverWatch.Stop();
        SetIsHovering(false);
        ClosePopup();
 
        _hook.Stop();
        _hook.OnMouseClicked -= OnGlobalClick;
 
        _hWnd = GetHandle();
 
        var source = HwndSource.FromHwnd(_hWnd);
        source?.RemoveHook(WndProc);
 
        NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_DELETE, ref _data);
        
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    /// <summary>
    /// Обновление прямоугольника иконки. Вызывается один раз при входе
    /// курсора на иконку (раньше Shell_NotifyIconGetRect вызывался на КАЖДОЕ движение мыши).
    /// </summary>
    private void UpdateIconRect()
    {
        NOTIFYICONIDENTIFIER nii = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _hWnd,
            uID = 0,
            guidItem = _useGuid ? IconGuid : Guid.Empty
        };
 
        if (NativeMethods.Shell_NotifyIconGetRect(ref nii, out var rect) == 0)
        {
            IconPosition = rect;
        }
    }

    /// <summary>
    /// Add icon to the system tray (Taskbar)
    /// </summary>
    /// <param name="hWnd"></param>
    private void ShowIcon(IntPtr hWnd)
    {
        if (_isIconShown) return;
        _isIconShown = true;
 
        var iconHandle = IntPtr.Zero;
        
        if (Icon != null)
        {
            _trayIcon = IconHelper.GetIconFromImageSource(Icon, 32);
            iconHandle = _trayIcon.Handle;
        }
        else
        {
            var module = Process.GetCurrentProcess().MainModule;
 
            if (module != null)
            {
                var exePath = module.FileName;
 
                if (!string.IsNullOrEmpty(exePath))
                {
                    _trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
 
                    if (_trayIcon != null)
                    {
                        iconHandle = _trayIcon.Handle;
                    }
                }
            }
        }
 
        IconGuid = IconGuid != Guid.Empty ? IconGuid : CreateGuidFromExePath();
        _useGuid = true;
        
        _data = new NOTIFYICONDATA
        {
            uCallbackMessage = (int)EWindowMessages.WM_USER + 1,
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hWnd,
            uID = 0,
            uFlags = ETrayIconFlags.NIF_ICON | ETrayIconFlags.NIF_MESSAGE | ETrayIconFlags.NIF_GUID,
            hIcon = iconHandle,
            guidItem = IconGuid
        };
 
        // // ДОБАВЛЕНО: удаляем «призрак» от прошлого запуска (если процесс был убит из студии)
        // var stale = new NOTIFYICONDATA
        // {
        //     cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        //     hWnd = hWnd,
        //     uID = 0,
        //     uFlags = ETrayIconFlags.NIF_GUID,
        //     guidItem = IconGuid
        // };
        
        //NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_DELETE, ref stale);
        NativeMethods.RefreshTrayArea(); 

        // ИЗМЕНЕНО: если добавление с GUID не удалось (например, сменился путь exe),
        // повторяем без GUID
        if (!NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_ADD, ref _data))
        {
            Debug.WriteLine($"NIM_ADD with GUID failed, err={Marshal.GetLastWin32Error()}");
            _useGuid = false;
            _data.uFlags &= ~ETrayIconFlags.NIF_GUID;
            _data.guidItem = Guid.Empty;
            NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_ADD, ref _data);
        }
        
        _hWnd = GetHandle();
 
        var source = HwndSource.FromHwnd(_hWnd);
        source?.AddHook(WndProc);
    }

    private void ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _hook.OnMouseClicked -= OnGlobalClick;
        _hook.Stop();
    }

    // Хук включается только на время, пока меню открыто
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
 
        _hook.OnMouseClicked -= OnGlobalClick;
        _hook.OnMouseClicked += OnGlobalClick;
        _hook.Start();
    }

private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreatedMsg != 0 && (uint)msg == _taskbarCreatedMsg)
        {
            if (!_isIconShown) return IntPtr.Zero;
            
            NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_DELETE, ref _data);
            NativeMethods.Shell_NotifyIcon(ETrayIconMessage.NIM_ADD, ref _data);

            return IntPtr.Zero;
        }

        if ((EWindowMessages)msg != EWindowMessages.WM_USER + 1) return IntPtr.Zero; // наш callback
        
        var windowMessage = (EWindowMessages)lParam.ToInt32();
 
        switch (windowMessage)
        {
            case EWindowMessages.WM_MOUSEMOVE:
                
                if (!IsHovering())
                {
                    SetIsHovering(true);
                    UpdateIconRect();
                    _hoverWatch.Start();
 
                    if (IsInnerTaskIcon())
                    {
                        if (ContextMenu is not { IsOpen: true })
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
                        ClosePopup();
                        
                        ContextMenu.PlacementTarget = this;
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

        return IntPtr.Zero;
    }

    private void HandleSingleClick()
    {
        HideContextMenu();

        LeftClickCommand?.Execute(null);
    }

    // Убрана проверка _isInContextMenu и Dispatcher.Invoke:
    // метод вызывается из UI-потока (WndProc / DispatcherTimer).
    private void HideContextMenu()
    {
        if (ContextMenu?.IsOpen == true)
        {
            ContextMenu.IsOpen = false;
        }
    }

    /// <summary>
    /// Позиция курсора берётся напрямую через GetCursorPos,
    /// а не из глобального хука (хук движения мыши больше не используется).
    /// </summary>
    private bool IsInnerTaskIcon()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return false;

        return pt.x >= IconPosition.Left &&
               pt.x <= IconPosition.Right &&
               pt.y >= IconPosition.Top &&
               pt.y <= IconPosition.Bottom;
    }

    /// <summary>
    /// Позиция popup считается один раз перед показом
    /// (раньше это делал таймер каждые 100 мс, даже когда popup закрыт).
    /// Математика расчёта сохранена как в исходном коде.
    /// </summary>
    private void PositionPopup()
    {
        if (_popup?.Child is not FrameworkElement child) return;
 
        child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = child.DesiredSize;
 
        TrayIconStatics.CalculateDpi();
        var scale = TrayIconStatics.DpiScale;
 
        var iconCenter = IconPosition.Left + (IconPosition.Right - IconPosition.Left) / 2;
        var iconCenterDpi = iconCenter * scale;
 
        var popupWidth = desired.Width - child.Margin.Left - child.Margin.Right;
        var popupHeight = desired.Height - child.Margin.Top - child.Margin.Bottom;
        var popupPosition = iconCenterDpi - popupWidth / 2;
 
        var rect = NativeMethods.GetPrimaryWorkArea();
 
        if (iconCenterDpi + popupWidth / 2 > rect.Right)
        {
            popupPosition = rect.Right - popupWidth;
        }
 
        const int offset = 12;
        var iconPositionTop = (IconPosition.Top * scale) - offset;
 
        _popup.HorizontalOffset = popupPosition;
        _popup.VerticalOffset = iconPositionTop - popupHeight;
    }

    /// <summary>
    /// Без Dispatcher.InvokeAsync: вызывается из WndProc, т.е. уже в UI-потоке.
    /// Позиция считается перед показом.
    /// </summary>
    private async Task OnTrayMouseEnter()
    {
        var popup = GetPopup();
 
        if (popup == null || popup.IsOpen) return;
 
        await Task.Delay(500);
 
        if (!IsHovering()) return;
        if (ContextMenu is { IsOpen: true }) return;
 
        PositionPopup();
        popup.IsOpen = true;
    }

    /// <summary>
    /// Close existing popup
    /// </summary>
    private void ClosePopup()
    {
        var popup = GetPopup();
 
        if (popup is { IsOpen: true })
        {
            popup.IsOpen = false;
        }
    }

    private void InitPopup()
    {
        _isPopupInitialized = false;

        var popup = GetPopup();

        if (popup != null)
        {
            popup.IsOpen = false;
            popup.Child = null;
            
            RemoveLogicalChild(popup);
            _logicalChildren.Remove(popup);
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

        TrayToolTip ??= GetWindow()?.Title;

        var wrapper = GetWrapper();

        if (wrapper != null)
        {
            wrapper.SizeChanged -= Wrapper_SizeChanged;
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
            Margin = new Thickness(10)
            // Effect = new DropShadowEffect
            // {
            //     Color = Color.FromArgb(150, 0, 0, 0),
            //     BlurRadius = 0.5,
            //     ShadowDepth = 0.2,
            //     Direction = 250,
            //     Opacity = 0.2
            // }
        };

        wrapper.SetValue(TextElement.ForegroundProperty, Foreground);

        wrapper.SizeChanged += Wrapper_SizeChanged;
        
        SetWrapper(wrapper);

        popup = new Popup
        {
            DataContext = DataContext,
            Placement = PlacementMode.AbsolutePoint,
            HorizontalOffset = IconPosition.Left,
            VerticalOffset = IconPosition.Top,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
            AllowsTransparency = true,
            Child = GetWrapper()
        };

        SetPopup(popup);
        
        _logicalChildren.Add(popup);
        AddLogicalChild(popup);

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