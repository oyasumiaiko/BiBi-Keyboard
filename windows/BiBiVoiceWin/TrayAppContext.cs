using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Channels;
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
    private readonly SynchronizationContext _uiContext;

    private readonly AppConfig _cfg;
    private readonly string _configPath;
    private readonly HotkeyManager? _hotkey;
    private readonly AudioRecorder _recorder;
    private readonly VolcStreamAsrClient _asr;
    private readonly TextInserter _inserter;

    private AppState _state = AppState.Idle;
    private IntPtr _targetWindow = IntPtr.Zero;
    private CancellationTokenSource? _workCts;
    private CancellationTokenSource? _streamCts;
    private Channel<byte[]>? _pcmChannel;
    private Task<string>? _asrTask;
    private TextInserter.StreamingSession? _streamSession;

    // 录音线程触发的“建议停止”信号，通过 UI Timer 拉回到 UI 线程执行。
    private int _pendingAutoStopReason = 0;

    public TrayAppContext()
    {
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        var (cfg, configPath, created) = AppConfig.LoadOrCreate();
        _cfg = cfg;
        _configPath = configPath;

        _recorder = new AudioRecorder();
        _recorder.AutoStopRequested += reason =>
        {
            // 避免重复触发：只有从 0->reason 的第一次设置才生效。
            Interlocked.CompareExchange(ref _pendingAutoStopReason, (int)reason, 0);
        };
        _recorder.Pcm16ChunkAvailable += OnPcm16Chunk;

        _asr = new VolcStreamAsrClient(_cfg.Volc);
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
            await BeginStopAndFinalizeAsync((AutoStopReason)reason);
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
                LogStatus("热键注册失败", ex.Message);
            }
        }
        else
        {
            LogStatus("热键配置错误", err);
        }

        LogStatus("BiBiVoiceWin 已启动", $"热键：{_cfg.Hotkey} 配置：{_configPath}");

        if (created)
        {
            LogStatus("已生成 config.json", "请先填写火山引擎 App ID / Access Token");
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
                await BeginStopAndFinalizeAsync(null);
                break;
            case AppState.Transcribing:
                LogStatus("正在识别", "请稍候…");
                break;
        }
    }

    private void StartRecording()
    {
        if (_state != AppState.Idle) return;

        _targetWindow = Win32.GetForegroundWindow();
        _state = AppState.Recording;

        _toggleItem.Text = "停止";
        _tray.Text = "BiBiVoiceWin - 录音中";

        try
        {
            var options = new RecorderOptions(
                TargetSampleRate: _cfg.TargetSampleRate,
                MaxRecordSeconds: _cfg.MaxRecordSeconds,
                AutoStopEnabled: _cfg.AutoStopEnabled,
                AutoStopSilenceMs: _cfg.AutoStopSilenceMs,
                AutoStopThresholdDb: _cfg.AutoStopThresholdDb
            );
            StartStreamingSession(options.TargetSampleRate);
            _recorder.Start(options);
        }
        catch (Exception ex)
        {
            _state = AppState.Idle;
            _toggleItem.Text = "开始录音";
            _tray.Text = "BiBiVoiceWin";
            LogStatus("启动录音失败", ex.Message);
        }
    }

    private async Task BeginStopAndFinalizeAsync(AutoStopReason? reason)
    {
        if (_state != AppState.Recording) return;

        _state = AppState.Transcribing;
        _toggleItem.Text = "收尾中…";
        _tray.Text = "BiBiVoiceWin - 收尾中";

        _workCts?.Cancel();
        _workCts?.Dispose();
        _workCts = new CancellationTokenSource();
        var ct = _workCts.Token;

        try
        {
            var audio = await _recorder.StopAsync(ct);
            CompleteAudioStream();

            if (audio.WavBytes.Length < 2000)
            {
                CancelStreamingSession();
                return;
            }

            var reasonText = reason switch
            {
                AutoStopReason.Silence => "（静音自动停止）",
                AutoStopReason.MaxDuration => "（达到最大时长自动停止）",
                _ => ""
            };
            LogStatus("识别中", $"火山流式收尾 {reasonText}");

            var finalText = await AwaitFinalResultAsync(ct);
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                PostToUi(async token =>
                {
                    if (_streamSession is null) return;
                    await _streamSession.ApplyFinalAsync(finalText, token).ConfigureAwait(true);
                });
            }
        }
        catch (OperationCanceledException)
        {
            CancelStreamingSession();
        }
        catch (Exception ex)
        {
            CancelStreamingSession();
            LogStatus("识别失败", ex.Message);
        }
        finally
        {
            CleanupStreamingSession();
            _state = AppState.Idle;
            _toggleItem.Text = "开始录音";
            _tray.Text = "BiBiVoiceWin";
        }
    }

    private void StartStreamingSession(int targetSampleRate)
    {
        CancelStreamingSession();
        CleanupStreamingSession();

        _streamCts = new CancellationTokenSource();
        _pcmChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
        _streamSession = _inserter.StartStreamingSession(_targetWindow);
        _asrTask = _asr.TranscribeStreamingAsync(_pcmChannel.Reader, targetSampleRate, OnAsrResult, _streamCts.Token);
    }

    private void CompleteAudioStream()
    {
        try { _pcmChannel?.Writer.TryComplete(); } catch { }
    }

    private void CancelStreamingSession()
    {
        try { _streamCts?.Cancel(); } catch { }
        try { _pcmChannel?.Writer.TryComplete(); } catch { }
    }

    private void CleanupStreamingSession()
    {
        _pcmChannel = null;
        _streamSession = null;
        _asrTask = null;
        _streamCts?.Dispose();
        _streamCts = null;
    }

    private async Task<string> AwaitFinalResultAsync(CancellationToken ct)
    {
        if (_asrTask is null) return "";
        var done = await Task.WhenAny(_asrTask, Task.Delay(15000, ct)).ConfigureAwait(false);
        if (done != _asrTask)
        {
            CancelStreamingSession();
            return "";
        }
        return await _asrTask.ConfigureAwait(false);
    }

    private void OnPcm16Chunk(byte[] chunk)
    {
        if (_state != AppState.Recording) return;
        var writer = _pcmChannel?.Writer;
        if (writer is null) return;
        writer.TryWrite(chunk);
    }

    private void OnAsrResult(string text, bool isFinal)
    {
        PostToUi(async token =>
        {
            if (_streamSession is null) return;
            if (isFinal)
            {
                await _streamSession.ApplyFinalAsync(text, token).ConfigureAwait(true);
            }
            else
            {
                await _streamSession.ApplyPartialAsync(text, token).ConfigureAwait(true);
            }
        });
    }

    private void PostToUi(Func<CancellationToken, Task> action)
    {
        var token = _streamCts?.Token ?? CancellationToken.None;
        _uiContext.Post(async _ =>
        {
            try { await action(token).ConfigureAwait(true); } catch { }
        }, null);
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
            LogStatus("打开配置失败", ex.Message);
        }
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        // 已禁用气泡提示（用户不希望使用系统通知）。
        LogStatus(title, text);
    }

    private void LogStatus(string title, string text)
    {
        try { Debug.WriteLine($"[{title}] {text}"); } catch { }
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
