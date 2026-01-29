using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BiBiVoiceWin;

internal enum AppState
{
    Idle = 0,
    Recording = 1,
    Transcribing = 2
}

/// <summary>
/// 最小托盘常驻程序：
/// - 全局热键触发录音
/// - 调用火山 ASR 转文字
/// - 将文字插入当前前台应用的焦点输入框
/// </summary>
public sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly System.Windows.Forms.Timer _uiTimer;

    private readonly AppConfig _cfg;
    private readonly string _configPath;
    private readonly HotkeyManager? _hotkey;
    private readonly AudioRecorder _recorder;
    private readonly VolcFlashAsrClient _asr;
    private readonly TextInserter _inserter;

    private AppState _state = AppState.Idle;
    private IntPtr _targetWindow = IntPtr.Zero;
    private CancellationTokenSource? _workCts;

    // 录音线程触发的“建议停止”信号，通过 UI Timer 拉回到 UI 线程执行。
    private int _pendingAutoStopReason = 0;

    public TrayAppContext()
    {
        var (cfg, configPath, created) = AppConfig.LoadOrCreate();
        _cfg = cfg;
        _configPath = configPath;

        _recorder = new AudioRecorder();
        _recorder.AutoStopRequested += reason =>
        {
            // 避免重复触发：只有从 0->reason 的第一次设置才生效。
            Interlocked.CompareExchange(ref _pendingAutoStopReason, (int)reason, 0);
        };

        _asr = new VolcFlashAsrClient(_cfg.Volc);
        _inserter = new TextInserter(InsertModeParser.ParseOrDefault(_cfg.InsertMode), _cfg.AppendSpace);

        _toggleItem = new ToolStripMenuItem("开始录音");
        _toggleItem.Click += async (_, _) => await ToggleAsync();

        var openConfig = new ToolStripMenuItem("打开配置文件");
        openConfig.Click += (_, _) => OpenConfigFile();

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => Exit();

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _toggleItem,
            new ToolStripSeparator(),
            openConfig,
            exit
        });

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "BiBiVoiceWin",
            ContextMenuStrip = _menu
        };
        _tray.DoubleClick += async (_, _) => await ToggleAsync();

        // UI 定时器：把“录音线程的自动停止事件”拉回 UI 线程处理
        _uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _uiTimer.Tick += async (_, _) =>
        {
            var reason = Interlocked.Exchange(ref _pendingAutoStopReason, 0);
            if (reason == 0) return;
            await BeginStopAndTranscribeAsync((AutoStopReason)reason);
        };
        _uiTimer.Start();

        // 注册全局热键
        if (HotkeySpecParser.TryParse(_cfg.Hotkey, out var spec, out var err))
        {
            try
            {
                _hotkey = new HotkeyManager(spec);
                _hotkey.Pressed += async (_, _) => await ToggleAsync();
            }
            catch (Exception ex)
            {
                ShowBalloon("热键注册失败", ex.Message, ToolTipIcon.Error);
            }
        }
        else
        {
            ShowBalloon("热键配置错误", err, ToolTipIcon.Warning);
        }

        ShowBalloon(
            "BiBiVoiceWin 已启动",
            $"热键：{_cfg.Hotkey}\n配置：{_configPath}",
            created ? ToolTipIcon.Warning : ToolTipIcon.Info
        );

        if (created)
        {
            ShowBalloon("已生成 config.json", "请先填写火山引擎 AppKey / AccessKey", ToolTipIcon.Warning);
        }
    }

    private async Task ToggleAsync()
    {
        switch (_state)
        {
            case AppState.Idle:
                StartRecording();
                break;
            case AppState.Recording:
                await BeginStopAndTranscribeAsync(null);
                break;
            case AppState.Transcribing:
                ShowBalloon("正在识别", "请稍候…", ToolTipIcon.Info);
                break;
        }
    }

    private void StartRecording()
    {
        if (_state != AppState.Idle) return;

        _targetWindow = Win32.GetForegroundWindow();
        _state = AppState.Recording;

        _toggleItem.Text = "停止并识别";
        _tray.Text = "BiBiVoiceWin - 录音中";
        ShowBalloon("开始录音", "再次触发热键停止并识别", ToolTipIcon.Info);

        try
        {
            var options = new RecorderOptions(
                TargetSampleRate: _cfg.TargetSampleRate,
                MaxRecordSeconds: _cfg.MaxRecordSeconds,
                AutoStopEnabled: _cfg.AutoStopEnabled,
                AutoStopSilenceMs: _cfg.AutoStopSilenceMs,
                AutoStopThresholdDb: _cfg.AutoStopThresholdDb
            );
            _recorder.Start(options);
        }
        catch (Exception ex)
        {
            _state = AppState.Idle;
            _toggleItem.Text = "开始录音";
            _tray.Text = "BiBiVoiceWin";
            ShowBalloon("启动录音失败", ex.Message, ToolTipIcon.Error);
        }
    }

    private async Task BeginStopAndTranscribeAsync(AutoStopReason? reason)
    {
        if (_state != AppState.Recording) return;

        _state = AppState.Transcribing;
        _toggleItem.Text = "识别中…";
        _tray.Text = "BiBiVoiceWin - 识别中";

        _workCts?.Cancel();
        _workCts?.Dispose();
        _workCts = new CancellationTokenSource();
        var ct = _workCts.Token;

        try
        {
            var audio = await _recorder.StopAsync(ct);
            if (audio.WavBytes.Length < 2000)
            {
                ShowBalloon("录音太短", "没有采集到有效音频", ToolTipIcon.Warning);
                return;
            }

            var reasonText = reason switch
            {
                AutoStopReason.Silence => "（静音自动停止）",
                AutoStopReason.MaxDuration => "（达到最大时长自动停止）",
                _ => ""
            };
            ShowBalloon("开始识别", $"正在调用火山 ASR…{reasonText}", ToolTipIcon.Info);

            var text = await _asr.TranscribeAsync(audio.WavBytes, ct);
            ShowBalloon("识别完成", text.Length > 80 ? (text[..80] + "…") : text, ToolTipIcon.Info);

            await _inserter.InsertAsync(_targetWindow, text, ct);
        }
        catch (OperationCanceledException)
        {
            ShowBalloon("已取消", "当前识别任务已取消", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowBalloon("识别失败", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _state = AppState.Idle;
            _toggleItem.Text = "开始录音";
            _tray.Text = "BiBiVoiceWin";
        }
    }

    private void OpenConfigFile()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _configPath,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            ShowBalloon("打开配置失败", ex.Message, ToolTipIcon.Error);
        }
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        try
        {
            _tray.ShowBalloonTip(1500, title, text, icon);
        }
        catch
        {
            // ignore
        }
    }

    private void Exit()
    {
        try { _workCts?.Cancel(); } catch { }
        try { _workCts?.Dispose(); } catch { }

        try { _uiTimer.Stop(); } catch { }
        _uiTimer.Dispose();

        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        _hotkey?.Dispose();
        _recorder.Dispose();
        ExitThread();
    }
}

