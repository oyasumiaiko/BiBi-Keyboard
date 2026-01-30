using Microsoft.UI.Xaml;

namespace BiBiVoiceWin.Settings;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        SettingsLog.Init();
        UnhandledException += (_, args) =>
        {
            SettingsLog.Write("App.UnhandledException", args.Exception);
            // 没有主窗口时也能提示用户：帮助定位“窗口打不开/秒退”的原因。
            SettingsLog.ShowFatal("设置界面发生未处理异常，已写入日志。");
            args.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            SettingsLog.Write("OnLaunched failed", ex);
            SettingsLog.ShowFatal("设置界面启动失败，已写入日志。");
            Environment.Exit(1);
        }
    }
}
