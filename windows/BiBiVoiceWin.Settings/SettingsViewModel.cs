using System.ComponentModel;
using System.Runtime.CompilerServices;
using BiBiVoiceWin;

namespace BiBiVoiceWin.Settings;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private string _configPath = "";
    private string _hotkey = "Space";
    private string _insertMode = "SendInput";
    private bool _appendSpace;
    private bool _holdToTalkEnabled = true;
    private string _holdToTalkKey = "Space";
    private double _holdToTalkMinHoldMs = 500;
    private double _targetSampleRate = 16000;
    private double _maxRecordSeconds = 60;
    private bool _autoStopEnabled = true;
    private double _autoStopSilenceMs = 1200;
    private double _autoStopThresholdDb = -35;
    private double _transcribeWatchdogSeconds = 15;

    private string _volcEndpoint = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async";
    private string _volcAppKey = "";
    private string _volcAccessKey = "";
    private string _volcResourceId = "volc.seedasr.sauc.duration";
    private bool _volcEnableDdc = true;
    private bool _volcEnableNonstream = true;
    private bool _volcEnableVad;
    private double _volcVadEndWindowSizeMs = 800;
    private double _volcVadForceToSpeechTimeMs = 1000;
    private string _volcLanguage = "";

    private bool _dialogEnabled;
    private string _llmEndpoint = "https://api.openai.com/v1/chat/completions";
    private string _llmApiKey = "";
    private string _llmModel = "gpt-4o-mini";
    private double _llmTemperature = 0.2;
    private double _sourceMaxChars = 800;
    private double _minUpdateChars = 8;
    private double _maxSummaryChars = 200;
    private double _ttlMinutes = 240;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ConfigPath
    {
        get => _configPath;
        set => SetField(ref _configPath, value);
    }

    public string Hotkey
    {
        get => _hotkey;
        set => SetField(ref _hotkey, value);
    }

    public string InsertMode
    {
        get => _insertMode;
        set => SetField(ref _insertMode, value);
    }

    public bool AppendSpace
    {
        get => _appendSpace;
        set => SetField(ref _appendSpace, value);
    }

    public bool HoldToTalkEnabled
    {
        get => _holdToTalkEnabled;
        set => SetField(ref _holdToTalkEnabled, value);
    }

    public string HoldToTalkKey
    {
        get => _holdToTalkKey;
        set => SetField(ref _holdToTalkKey, value);
    }

    public double HoldToTalkMinHoldMs
    {
        get => _holdToTalkMinHoldMs;
        set => SetField(ref _holdToTalkMinHoldMs, value);
    }

    public double TargetSampleRate
    {
        get => _targetSampleRate;
        set => SetField(ref _targetSampleRate, value);
    }

    public double MaxRecordSeconds
    {
        get => _maxRecordSeconds;
        set => SetField(ref _maxRecordSeconds, value);
    }

    public bool AutoStopEnabled
    {
        get => _autoStopEnabled;
        set => SetField(ref _autoStopEnabled, value);
    }

    public double AutoStopSilenceMs
    {
        get => _autoStopSilenceMs;
        set => SetField(ref _autoStopSilenceMs, value);
    }

    public double AutoStopThresholdDb
    {
        get => _autoStopThresholdDb;
        set => SetField(ref _autoStopThresholdDb, value);
    }

    public double TranscribeWatchdogSeconds
    {
        get => _transcribeWatchdogSeconds;
        set => SetField(ref _transcribeWatchdogSeconds, value);
    }

    public string VolcEndpoint
    {
        get => _volcEndpoint;
        set => SetField(ref _volcEndpoint, value);
    }

    public string VolcAppKey
    {
        get => _volcAppKey;
        set => SetField(ref _volcAppKey, value);
    }

    public string VolcAccessKey
    {
        get => _volcAccessKey;
        set => SetField(ref _volcAccessKey, value);
    }

    public string VolcResourceId
    {
        get => _volcResourceId;
        set => SetField(ref _volcResourceId, value);
    }

    public bool VolcEnableDdc
    {
        get => _volcEnableDdc;
        set => SetField(ref _volcEnableDdc, value);
    }

    public bool VolcEnableNonstream
    {
        get => _volcEnableNonstream;
        set => SetField(ref _volcEnableNonstream, value);
    }

    public bool VolcEnableVad
    {
        get => _volcEnableVad;
        set => SetField(ref _volcEnableVad, value);
    }

    public double VolcVadEndWindowSizeMs
    {
        get => _volcVadEndWindowSizeMs;
        set => SetField(ref _volcVadEndWindowSizeMs, value);
    }

    public double VolcVadForceToSpeechTimeMs
    {
        get => _volcVadForceToSpeechTimeMs;
        set => SetField(ref _volcVadForceToSpeechTimeMs, value);
    }

    public string VolcLanguage
    {
        get => _volcLanguage;
        set => SetField(ref _volcLanguage, value);
    }

    public bool DialogEnabled
    {
        get => _dialogEnabled;
        set => SetField(ref _dialogEnabled, value);
    }

    public string LlmEndpoint
    {
        get => _llmEndpoint;
        set => SetField(ref _llmEndpoint, value);
    }

    public string LlmApiKey
    {
        get => _llmApiKey;
        set => SetField(ref _llmApiKey, value);
    }

    public string LlmModel
    {
        get => _llmModel;
        set => SetField(ref _llmModel, value);
    }

    public double LlmTemperature
    {
        get => _llmTemperature;
        set => SetField(ref _llmTemperature, value);
    }

    public double SourceMaxChars
    {
        get => _sourceMaxChars;
        set => SetField(ref _sourceMaxChars, value);
    }

    public double MinUpdateChars
    {
        get => _minUpdateChars;
        set => SetField(ref _minUpdateChars, value);
    }

    public double MaxSummaryChars
    {
        get => _maxSummaryChars;
        set => SetField(ref _maxSummaryChars, value);
    }

    public double TtlMinutes
    {
        get => _ttlMinutes;
        set => SetField(ref _ttlMinutes, value);
    }

    public void Load()
    {
        var (cfg, path, _) = AppConfig.LoadOrCreate();
        ConfigPath = path;

        Hotkey = cfg.Hotkey;
        InsertMode = cfg.InsertMode;
        AppendSpace = cfg.AppendSpace;
        HoldToTalkEnabled = cfg.HoldToTalkEnabled;
        HoldToTalkKey = cfg.HoldToTalkKey;
        HoldToTalkMinHoldMs = cfg.HoldToTalkMinHoldMs;
        TargetSampleRate = cfg.TargetSampleRate;
        MaxRecordSeconds = cfg.MaxRecordSeconds;
        AutoStopEnabled = cfg.AutoStopEnabled;
        AutoStopSilenceMs = cfg.AutoStopSilenceMs;
        AutoStopThresholdDb = cfg.AutoStopThresholdDb;
        TranscribeWatchdogSeconds = cfg.TranscribeWatchdogSeconds;

        VolcEndpoint = cfg.Volc.Endpoint;
        VolcAppKey = cfg.Volc.AppKey;
        VolcAccessKey = cfg.Volc.AccessKey;
        VolcResourceId = cfg.Volc.ResourceId;
        VolcEnableDdc = cfg.Volc.EnableDdc;
        VolcEnableNonstream = cfg.Volc.EnableNonstream;
        VolcEnableVad = cfg.Volc.EnableVad;
        VolcVadEndWindowSizeMs = cfg.Volc.VadEndWindowSizeMs;
        VolcVadForceToSpeechTimeMs = cfg.Volc.VadForceToSpeechTimeMs;
        VolcLanguage = cfg.Volc.Language;

        DialogEnabled = cfg.DialogContext.Enabled;
        LlmEndpoint = cfg.DialogContext.LlmEndpoint;
        LlmApiKey = cfg.DialogContext.LlmApiKey;
        LlmModel = cfg.DialogContext.LlmModel;
        LlmTemperature = cfg.DialogContext.LlmTemperature;
        SourceMaxChars = cfg.DialogContext.SourceMaxChars;
        MinUpdateChars = cfg.DialogContext.MinUpdateChars;
        MaxSummaryChars = cfg.DialogContext.MaxSummaryChars;
        TtlMinutes = cfg.DialogContext.TtlMinutes;
    }

    public bool Save(out string error)
    {
        error = "";
        try
        {
            var cfg = new AppConfig
            {
                Hotkey = Hotkey.Trim(),
                InsertMode = InsertMode.Trim(),
                AppendSpace = AppendSpace,
                HoldToTalkEnabled = HoldToTalkEnabled,
                HoldToTalkKey = HoldToTalkKey.Trim(),
                HoldToTalkMinHoldMs = ToInt(HoldToTalkMinHoldMs, 500),
                TargetSampleRate = ToInt(TargetSampleRate, 16000),
                MaxRecordSeconds = ToInt(MaxRecordSeconds, 60),
                AutoStopEnabled = AutoStopEnabled,
                AutoStopSilenceMs = ToInt(AutoStopSilenceMs, 1200),
                AutoStopThresholdDb = AutoStopThresholdDb,
                TranscribeWatchdogSeconds = ToInt(TranscribeWatchdogSeconds, 15),
                Volc = new VolcConfig
                {
                    Endpoint = VolcEndpoint.Trim(),
                    AppKey = VolcAppKey.Trim(),
                    AccessKey = VolcAccessKey.Trim(),
                    ResourceId = VolcResourceId.Trim(),
                    EnableDdc = VolcEnableDdc,
                    EnableNonstream = VolcEnableNonstream,
                    EnableVad = VolcEnableVad,
                    VadEndWindowSizeMs = ToInt(VolcVadEndWindowSizeMs, 800),
                    VadForceToSpeechTimeMs = ToInt(VolcVadForceToSpeechTimeMs, 1000),
                    Language = VolcLanguage.Trim()
                },
                DialogContext = new DialogContextConfig
                {
                    Enabled = DialogEnabled,
                    LlmEndpoint = LlmEndpoint.Trim(),
                    LlmApiKey = LlmApiKey.Trim(),
                    LlmModel = LlmModel.Trim(),
                    LlmTemperature = (float)LlmTemperature,
                    SourceMaxChars = ToInt(SourceMaxChars, 800),
                    MinUpdateChars = ToInt(MinUpdateChars, 8),
                    MaxSummaryChars = ToInt(MaxSummaryChars, 200),
                    TtlMinutes = ToInt(TtlMinutes, 240)
                }
            };

            if (string.IsNullOrWhiteSpace(ConfigPath))
            {
                var (_, path, _) = AppConfig.LoadOrCreate();
                ConfigPath = path;
            }

            AppConfig.Save(cfg, ConfigPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static int ToInt(double value, int fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
        return Math.Max(0, (int)Math.Round(value));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
