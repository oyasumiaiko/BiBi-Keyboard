using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace BiBiVoiceWin.Settings;

public sealed partial class MainWindow : Window
{
    private readonly SettingsViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        Root.DataContext = _viewModel;
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
