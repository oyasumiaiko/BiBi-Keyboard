using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace BiBiVoiceWin.Settings;

public sealed partial class MainWindow : Window
{
    private readonly SettingsViewModel _viewModel = new();
    private AppWindowTitleBar? _titleBar;
    private AppWindow? _appWindow;
    private SizeInt32 _lastSize;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureTitleBar();
        Root.DataContext = _viewModel;
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Root_PointerWheelChanged), true);
        Root.ActualThemeChanged += (_, _) =>
        {
            ApplyTitleBarTheme();
            ApplyCardTheme();
        };
        ApplyCardTheme();
        SizeChanged += OnWindowSizeChanged;
        Closed += (_, _) => SaveWindowState();
        try
        {
            _viewModel.Load();
        }
        catch (Exception ex)
        {
            SettingsLog.Write("SettingsViewModel.Load failed", ex);
            StatusText.Text = "加载配置失败，已写入日志";
        }
    }

    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var scroll = FindScrollViewer(e.OriginalSource as DependencyObject) ?? FindScrollViewer(Root);
        if (scroll is null) return;
        var point = e.GetCurrentPoint(scroll);
        var delta = point.Properties.MouseWheelDelta;
        if (delta == 0) return;

        // 手动滚动时用动画，避免出现“前半段跳、后半段顺”的割裂感。
        var next = scroll.VerticalOffset - delta;
        if (next < 0) next = 0;
        if (next > scroll.ScrollableHeight) next = scroll.ScrollableHeight;
        scroll.ChangeView(null, next, null, disableAnimation: false);
        e.Handled = true;
    }

    private void ConfigureTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow = appWindow;

            // 启动时恢复上次窗口大小，避免每次都回到默认尺寸。
            var state = WindowStateStore.Load();
            var target = WindowStateStore.NormalizeSize(state);
            appWindow.Resize(target);
            _lastSize = target;

            // 设置窗口图标，避免显示默认程序图标。
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                _titleBar = appWindow.TitleBar;
                _titleBar.ExtendsContentIntoTitleBar = true;
                UpdateTitleBarInsets();
                ApplyTitleBarTheme();
            }
        }
        catch (Exception ex)
        {
            SettingsLog.Write("ConfigureTitleBar failed", ex);
        }
    }

    private void UpdateTitleBarInsets()
    {
        if (_titleBar is null) return;
        AppTitleBar.Margin = new Thickness(_titleBar.LeftInset, 0, _titleBar.RightInset, 0);
    }

    private void ApplyTitleBarTheme()
    {
        if (_titleBar is null) return;
        var isDark = Root.ActualTheme == ElementTheme.Dark;

        var foreground = isDark ? Colors.White : Colors.Black;
        var inactiveForeground = isDark ? Colors.Gray : Colors.DarkGray;
        var hoverBg = isDark ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
        var pressedBg = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);

        _titleBar.ForegroundColor = foreground;
        _titleBar.InactiveForegroundColor = inactiveForeground;
        _titleBar.BackgroundColor = Colors.Transparent;
        _titleBar.InactiveBackgroundColor = Colors.Transparent;

        _titleBar.ButtonForegroundColor = foreground;
        _titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        _titleBar.ButtonBackgroundColor = Colors.Transparent;
        _titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        _titleBar.ButtonHoverBackgroundColor = hoverBg;
        _titleBar.ButtonHoverForegroundColor = foreground;
        _titleBar.ButtonPressedBackgroundColor = pressedBg;
        _titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void ApplyCardTheme()
    {
        var isDark = Root.ActualTheme == ElementTheme.Dark;
        var cardBg = isDark
            ? Color.FromArgb(255, 31, 31, 31)
            : Color.FromArgb(255, 246, 246, 246);
        var cardBorder = isDark
            ? Color.FromArgb(255, 42, 42, 42)
            : Color.FromArgb(255, 224, 224, 224);

        Root.Resources["CardBackgroundBrush"] = new SolidColorBrush(cardBg);
        Root.Resources["CardBorderBrush"] = new SolidColorBrush(cardBorder);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ScrollViewer sv) return sv;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        if (_appWindow is null) return;
        _lastSize = _appWindow.Size;
    }

    private void SaveWindowState()
    {
        if (_lastSize.Width <= 0 || _lastSize.Height <= 0) return;
        WindowStateStore.Save(_lastSize);
    }


    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Load();
        StatusText.Text = "已重新加载";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Save(out var error))
        {
            StatusText.Text = "已保存";
            return;
        }

        StatusText.Text = $"保存失败：{error}";
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        var path = _viewModel.ConfigPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText.Text = "配置路径为空";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"打开失败：{ex.Message}";
        }
    }
}
