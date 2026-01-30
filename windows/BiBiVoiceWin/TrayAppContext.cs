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
    private Icon? _dynamicIcon;

    private readonly AppConfig _cfg;
    private readonly string _configPath;
    private readonly HotkeyManager? _hotkey;
    private readonly KeyboardHook? _keyboardHook;
    private readonly AudioRecorder _recorder;
    private readonly VolcStreamAsrClient _asr;
    private readonly TextInserter _inserter;
    private readonly DialogContextManager _dialogContext;

    private AppState _state = AppState.Idle;
    private IntPtr _targetWindow = IntPtr.Zero;
    private CancellationTokenSource? _workCts;
    private CancellationTokenSource? _streamCts;
    private Channel<byte[]>? _pcmChannel;
    private Task<string>? _asrTask;
    private TextInserter.StreamingSession? _streamSession;
    private string _dialogContextKey = "";
    private string? _dialogContextText;

    // 录音线程触发的“建议停止”信号，通过 UI Timer 拉回到 UI 线程执行。
    private int _pendingAutoStopReason = 0;
    private bool _pcmChunkLogged;
    private bool _partialLogged;
    private bool _streamingStarted;
    private int _recordSampleRate;
    private readonly object _pcmLock = new();
    private Queue<byte[]>? _preRollChunks;
    private int _preRollBytes;
    private int _preRollMaxBytes;
    private DateTimeOffset _transcribeStartedAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan TranscribeWatchdogTimeout = TimeSpan.FromSeconds(15);
    private bool _finalReceived;
    private bool _restartAfterFinalize;

    // 按住说话
    private Keys _holdKey = Keys.Space;
    private int _holdMinMs;
    private bool _holdKeyDown;
    private bool _holdTriggered;
    private System.Threading.Timer? _holdTimer;

    public TrayAppContext()
    {
        // 确保使用 WinForms 的同步上下文，便于 UI 线程相关操作
        if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var (cfg, configPath, created) = AppConfig.LoadOrCreate();
        _cfg = cfg;
        _configPath = configPath;
        _holdMinMs = Math.Max(80, _cfg.HoldToTalkMinHoldMs);

        _recorder = new AudioRecorder();
        _recorder.AutoStopRequested += reason =>
        {
            // 按住说话期间不启用静音判停，避免中途停顿就被自动收尾。
            if (_holdKeyDown && _holdTriggered) return;
            // 避免重复触发：只有从 0->reason 的第一次设置才生效。
            Interlocked.CompareExchange(ref _pendingAutoStopReason, (int)reason, 0);
        };
        _recorder.Pcm16ChunkAvailable += OnPcm16Chunk;

        _asr = new VolcStreamAsrClient(_cfg.Volc);
        _inserter = new TextInserter(InsertModeParser.ParseOrDefault(_cfg.InsertMode), _cfg.AppendSpace);
        _dialogContext = new DialogContextManager(_cfg.DialogContext);

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
        UpdateTrayIcon();

        // UI 定时器：把“录音线程的自动停止事件”拉回 UI 线程处理
        _uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _uiTimer.Tick += async (_, _) =>
        {
            var reason = Interlocked.Exchange(ref _pendingAutoStopReason, 0);
            if (reason == 0) return;
            await BeginStopAndFinalizeAsync((AutoStopReason)reason);
        };
        _uiTimer.Start();
        _uiTimer.Tick += (_, _) => CheckTranscribeWatchdog();

        // 按住说话（优先）或全局热键（兜底）
        var holdErr = "";
        if (_cfg.HoldToTalkEnabled && KeyParser.TryParseSingleKey(_cfg.HoldToTalkKey, out var holdKey, out holdErr))
        {
            try
            {
                _holdKey = holdKey;
                _keyboardHook = new KeyboardHook();
                _keyboardHook.KeyEvent += OnHoldKeyEvent;
                LogStatus("按住说话", $"按键：{_cfg.HoldToTalkKey}，长按阈值 {_holdMinMs}ms");
            }
            catch (Exception ex)
            {
                LogStatus("键盘钩子失败", ex.Message);
            }
        }
        else
        {
            if (_cfg.HoldToTalkEnabled)
            {
                LogStatus("按住说话配置错误", holdErr);
            }

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
        }

        LogStatus("BiBiVoiceWin 已启动", $"热键：{_cfg.Hotkey} 配置：{_configPath}");
        LogStatus("日志路径", AppLogger.LogPath);

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

    private void StartRecording(bool holdToTalkSession = false, bool deferStreaming = false)
    {
        if (_state != AppState.Idle) return;

        ResetPreRollBuffer();
        _finalReceived = false;
        _restartAfterFinalize = false;
        _targetWindow = Win32.GetForegroundWindow();
        var title = Win32.GetWindowTitle(_targetWindow);
        LogStatus("目标窗口", $"0x{_targetWindow.ToInt64():X} {title}");
        _dialogContextKey = DialogContextManager.BuildWindowKey(_targetWindow);
        _dialogContextText = _dialogContext.GetDialogContext(_dialogContextKey);
        if (!string.IsNullOrWhiteSpace(_dialogContextText))
        {
            LogStatus("上下文", $"已加载（长度 {_dialogContextText.Length}）");
        }
        _state = AppState.Recording;

        _toggleItem.Text = "停止";
        _tray.Text = "BiBiVoiceWin - 录音中";
        UpdateTrayIcon();

        try
        {
            var autoStopEnabled = _cfg.AutoStopEnabled && !holdToTalkSession;
            if (!autoStopEnabled && _cfg.AutoStopEnabled && holdToTalkSession)
            {
                LogStatus("自动判停", "按住说话会忽略静音判停");
            }
            var options = new RecorderOptions(
                TargetSampleRate: _cfg.TargetSampleRate,
                MaxRecordSeconds: _cfg.MaxRecordSeconds,
                AutoStopEnabled: autoStopEnabled,
                AutoStopSilenceMs: _cfg.AutoStopSilenceMs,
                AutoStopThresholdDb: _cfg.AutoStopThresholdDb
            );
            _recordSampleRate = options.TargetSampleRate;
            _streamingStarted = false;
            if (!deferStreaming)
            {
                StartStreamingSession(_recordSampleRate);
                _streamingStarted = true;
            }
            else
            {
                InitPreRollBuffer(_recordSampleRate, _holdMinMs);
            }
            _recorder.Start(options);
        }
        catch (Exception ex)
        {
            _state = AppState.Idle;
            _toggleItem.Text = "开始录音";
            _tray.Text = "BiBiVoiceWin";
            UpdateTrayIcon();
            LogStatus("启动录音失败", ex.Message);
        }
    }

    private async Task BeginStopAndFinalizeAsync(AutoStopReason? reason)
    {
        if (_state != AppState.Recording) return;

        _state = AppState.Transcribing;
        _transcribeStartedAt = DateTimeOffset.UtcNow;
        _toggleItem.Text = "收尾中…";
        _tray.Text = "BiBiVoiceWin - 收尾中";
        UpdateTrayIcon();

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

                _ = Task.Run(async () =>
                {
                    try { await _dialogContext.UpdateFromFinalAsync(_dialogContextKey, finalText, CancellationToken.None); }
                    catch { }
                });
            }
            else
            {
                LogStatus("识别完成", "最终结果为空");
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
            ResetPreRollBuffer();
            _state = AppState.Idle;
            _transcribeStartedAt = DateTimeOffset.MinValue;
            _toggleItem.Text = "开始录音";
            _tray.Text = "BiBiVoiceWin";
            UpdateTrayIcon();
            if (!TryRestartAfterFinalize())
            {
                ResetHoldState();
            }
        }
    }

    private async Task StopPreRollDiscardAsync()
    {
        if (_state != AppState.Recording) return;

        _state = AppState.Idle;
        _toggleItem.Text = "开始录音";
        _tray.Text = "BiBiVoiceWin";
        UpdateTrayIcon();
        ResetHoldState();

        _workCts?.Cancel();
        _workCts?.Dispose();
        _workCts = new CancellationTokenSource();
        var ct = _workCts.Token;

        try
        {
            await _recorder.StopAsync(ct).ConfigureAwait(true);
        }
        catch
        {
            // 忽略异常，短按取消录音不需要错误提示
        }
        finally
        {
            CancelStreamingSession();
            CleanupStreamingSession();
            ResetPreRollBuffer();
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
        _pcmChunkLogged = false;
        _partialLogged = false;
        LogStatus("流式会话启动", $"Endpoint: {_cfg.Volc.Endpoint} ResourceId: {_cfg.Volc.ResourceId}");
        _asrTask = _asr.TranscribeStreamingAsync(_pcmChannel.Reader, targetSampleRate, OnAsrResult, _streamCts.Token, _dialogContextText);
        UpdateTrayIcon();
    }

    private void BeginHoldStreaming()
    {
        if (_state != AppState.Recording) return;
        if (_streamingStarted) return;
        StartStreamingSession(_recordSampleRate);
        FlushPreRollToChannel();
    }

    private void InitPreRollBuffer(int sampleRate, int holdMinMs)
    {
        lock (_pcmLock)
        {
            _preRollChunks = new Queue<byte[]>();
            _preRollBytes = 0;
            var bytesPerSecond = Math.Max(1, sampleRate * 2);
            var targetMs = Math.Max(holdMinMs, 100);
            _preRollMaxBytes = (int)Math.Ceiling(bytesPerSecond * (targetMs / 1000.0));
        }
    }

    private void ResetPreRollBuffer()
    {
        lock (_pcmLock)
        {
            _preRollChunks?.Clear();
            _preRollChunks = null;
            _preRollBytes = 0;
            _preRollMaxBytes = 0;
            _streamingStarted = false;
        }
    }

    private void FlushPreRollToChannel()
    {
        lock (_pcmLock)
        {
            _streamingStarted = true;
            if (_preRollChunks is null) return;
            var writer = _pcmChannel?.Writer;
            if (writer is null) return;
            while (_preRollChunks.Count > 0)
            {
                var chunk = _preRollChunks.Dequeue();
                writer.TryWrite(chunk);
            }
            _preRollBytes = 0;
        }
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
        UpdateTrayIcon();
    }

    private async Task<string> AwaitFinalResultAsync(CancellationToken ct)
    {
        if (_asrTask is null) return "";
        var done = await Task.WhenAny(_asrTask, Task.Delay(30000, ct)).ConfigureAwait(false);
        if (done != _asrTask)
        {
            LogStatus("识别超时", "30 秒内未收到最终结果");
            CancelStreamingSession();
            return "";
        }
        return await _asrTask.ConfigureAwait(false);
    }

    private void OnPcm16Chunk(byte[] chunk)
    {
        if (_state != AppState.Recording) return;
        lock (_pcmLock)
        {
            if (!_streamingStarted)
            {
                if (_preRollChunks is null) return;
                _preRollChunks.Enqueue(chunk);
                _preRollBytes += chunk.Length;
                while (_preRollBytes > _preRollMaxBytes && _preRollChunks.Count > 0)
                {
                    var drop = _preRollChunks.Dequeue();
                    _preRollBytes -= drop.Length;
                }
                return;
            }

            var writer = _pcmChannel?.Writer;
            if (writer is null) return;
            if (!_pcmChunkLogged)
            {
                _pcmChunkLogged = true;
                var db = EstimatePcm16Dbfs(chunk);
                LogStatus("音频流", $"已开始接收 PCM 分片（首包 {chunk.Length} bytes，约 {db:0.0} dBFS）");
            }
            writer.TryWrite(chunk);
        }
    }

    private void OnAsrResult(string text, bool isFinal)
    {
        if (isFinal)
        {
            _finalReceived = true;
            LogStatus("识别完成", $"最终文本长度 {text.Length}");
        }
        else if (!_partialLogged)
        {
            _partialLogged = true;
            LogStatus("识别流", $"收到首个片段，长度 {text.Length}");
        }
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

    private void PostToUiAction(Action action)
    {
        _uiContext.Post(_ =>
        {
            try { action(); } catch { }
        }, null);
    }

    private void OnHoldKeyEvent(object? sender, KeyboardHook.KeyboardHookEventArgs e)
    {
        if (e.IsInjected) return;
        if (e.Key != _holdKey) return;

        if (e.IsKeyDown)
        {
            // 重复按下（自动重复）直接吞掉，避免长按产生连发空格
            if (_holdKeyDown)
            {
                e.Suppress = true;
                return;
            }

            _holdKeyDown = true;
            _holdTriggered = false;

            if (_state == AppState.Transcribing)
            {
                if (_finalReceived)
                {
                    // 已收到最终结果：等待收尾完成后自动继续
                    _restartAfterFinalize = true;
                    LogStatus("收尾中", "已收到最终结果，完成后继续录音");
                    return;
                }

                // 还未收到最终结果：强制中断本次收尾并立即开始新会话
                LogStatus("收尾中", "未收到最终结果，已强制中断并重新开始");
                ForceReset();
                // ForceReset 会清掉按键状态，这里重新标记并启动录音
                _holdKeyDown = true;
                _holdTriggered = false;
                StartRecording(holdToTalkSession: true, deferStreaming: true);
                StartHoldTimer();
                return;
            }

            PostToUiAction(() => StartRecording(holdToTalkSession: true, deferStreaming: true));
            StartHoldTimer();
            return;
        }

        if (e.IsKeyUp)
        {
            _holdKeyDown = false;
            StopHoldTimer();

            if (_state == AppState.Transcribing)
            {
                // 收尾中抬键只更新状态，不触发 Stop/Discard
                return;
            }

            if (_holdTriggered)
            {
                // 松开 -> 停止识别
                PostToUi(async _ => await BeginStopAndFinalizeAsync(null));
            }
            else
            {
                // 未达到长按阈值：停止并丢弃预录音
                PostToUi(async _ => await StopPreRollDiscardAsync());
            }
        }
    }

    private void StartHoldTimer()
    {
        _holdTimer?.Dispose();
        _holdTimer = new System.Threading.Timer(_ =>
        {
            if (!_holdKeyDown) return;
            // 达到长按阈值 -> 开始流式识别（包含预录音）
            PostToUiAction(() =>
            {
                if (!_holdKeyDown || _holdTriggered) return;
                _holdTriggered = true;
                BeginHoldStreaming();
            });
        }, null, _holdMinMs, Timeout.Infinite);
    }

    private void StopHoldTimer()
    {
        try { _holdTimer?.Dispose(); } catch { }
        _holdTimer = null;
    }

    private void UpdateTrayIcon()
    {
        var micOn = _state == AppState.Recording;
        var streamOn = _asrTask is not null || _state == AppState.Transcribing;
        try
        {
            var icon = TrayIconRenderer.Create(micOn, streamOn);
            _dynamicIcon?.Dispose();
            _dynamicIcon = icon;
            _tray.Icon = icon;
        }
        catch
        {
            // 兜底：失败时保持现有图标
        }
    }

    private void ResetHoldState()
    {
        _holdKeyDown = false;
        _holdTriggered = false;
        StopHoldTimer();
    }

    private bool TryRestartAfterFinalize()
    {
        if (!_restartAfterFinalize) return false;
        _restartAfterFinalize = false;

        if (!_holdKeyDown) return false;
        // 用户仍在按住：进入新一轮录音
        StartRecording(holdToTalkSession: true, deferStreaming: true);
        StartHoldTimer();
        return true;
    }

    private void CheckTranscribeWatchdog()
    {
        if (_state != AppState.Transcribing) return;
        if (_transcribeStartedAt == DateTimeOffset.MinValue) return;
        if (DateTimeOffset.UtcNow - _transcribeStartedAt < TranscribeWatchdogTimeout) return;

        // 兜底：识别流程卡住时强制清理，避免托盘状态一直亮且无法继续使用。
        LogStatus("识别超时", $"超过 {TranscribeWatchdogTimeout.TotalSeconds:0} 秒未完成，已强制重置");
        ForceReset();
    }

    private void ForceReset()
    {
        try { _workCts?.Cancel(); } catch { }
        try { _workCts?.Dispose(); } catch { }
        _workCts = null;

        CancelStreamingSession();
        CleanupStreamingSession();
        ResetPreRollBuffer();
        _state = AppState.Idle;
        _transcribeStartedAt = DateTimeOffset.MinValue;
        _toggleItem.Text = "开始录音";
        _tray.Text = "BiBiVoiceWin";
        UpdateTrayIcon();
        ResetHoldState();
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
        AppLogger.Status(title, text);
    }

    private static double EstimatePcm16Dbfs(byte[] pcm16)
    {
        if (pcm16.Length < 2) return double.NegativeInfinity;
        double sumSquares = 0;
        var count = 0;
        for (var i = 0; i + 1 < pcm16.Length; i += 2)
        {
            var sample = BitConverter.ToInt16(pcm16, i);
            var f = sample / 32768.0;
            sumSquares += f * f;
            count++;
        }
        if (count == 0) return double.NegativeInfinity;
        var rms = Math.Sqrt(sumSquares / count);
        if (rms <= 0) return double.NegativeInfinity;
        return 20.0 * Math.Log10(rms);
    }

    private void Exit()
    {
        try { _workCts?.Cancel(); } catch { }
        try { _workCts?.Dispose(); } catch { }

        try { _uiTimer.Stop(); } catch { }
        _uiTimer.Dispose();

        _tray.Visible = false;
        _tray.Dispose();
        _dynamicIcon?.Dispose();
        _menu.Dispose();
        _hotkey?.Dispose();
        _keyboardHook?.Dispose();
        _holdTimer?.Dispose();
        _recorder.Dispose();
        ExitThread();
    }
}
