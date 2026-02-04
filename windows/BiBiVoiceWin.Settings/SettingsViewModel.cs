using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using BiBiVoiceWin;

namespace BiBiVoiceWin.Settings;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private const int AutoSaveDelayMs = 500;

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
    private bool _streamPauseEnabled = true;
    private double _streamPauseSilenceMs = 2000;
    private double _streamPauseThresholdDb = -35;

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
    private bool _volcDebugLogIncludeSecrets;

    private bool _dialogEnabled;
    private double _dialogSourceMaxChars = 800;
    private double _dialogMinUpdateChars = 8;
    private double _dialogMaxSummaryChars = 200;
    private double _dialogTtlMinutes = 240;

    private bool _proofreadEnabled = true;
    private double _proofreadSourceMaxChars = 800;
    private string _dialogApiProfileId = "default";
    private string _proofreadApiProfileId = "default";
    private bool _isLoading;
    private System.Threading.Timer? _autoSaveTimer;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel()
    {
        PropertyChanged += (_, _) =>
        {
            if (_isLoading) return;
            ScheduleAutoSave();
        };

        ApiProfiles.CollectionChanged += OnApiProfilesChanged;
    }

    public ObservableCollection<ApiProfileItem> ApiProfiles { get; } = new();

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

    public bool StreamPauseEnabled
    {
        get => _streamPauseEnabled;
        set => SetField(ref _streamPauseEnabled, value);
    }

    public double StreamPauseSilenceMs
    {
        get => _streamPauseSilenceMs;
        set => SetField(ref _streamPauseSilenceMs, value);
    }

    public double StreamPauseThresholdDb
    {
        get => _streamPauseThresholdDb;
        set => SetField(ref _streamPauseThresholdDb, value);
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

    public bool VolcDebugLogIncludeSecrets
    {
        get => _volcDebugLogIncludeSecrets;
        set => SetField(ref _volcDebugLogIncludeSecrets, value);
    }

    public bool DialogEnabled
    {
        get => _dialogEnabled;
        set => SetField(ref _dialogEnabled, value);
    }

    public string DialogApiProfileId
    {
        get => _dialogApiProfileId;
        set => SetField(ref _dialogApiProfileId, value);
    }


    public double DialogSourceMaxChars
    {
        get => _dialogSourceMaxChars;
        set => SetField(ref _dialogSourceMaxChars, value);
    }

    public double DialogMinUpdateChars
    {
        get => _dialogMinUpdateChars;
        set => SetField(ref _dialogMinUpdateChars, value);
    }

    public double DialogMaxSummaryChars
    {
        get => _dialogMaxSummaryChars;
        set => SetField(ref _dialogMaxSummaryChars, value);
    }

    public double DialogTtlMinutes
    {
        get => _dialogTtlMinutes;
        set => SetField(ref _dialogTtlMinutes, value);
    }

    public bool ProofreadEnabled
    {
        get => _proofreadEnabled;
        set => SetField(ref _proofreadEnabled, value);
    }

    public string ProofreadApiProfileId
    {
        get => _proofreadApiProfileId;
        set => SetField(ref _proofreadApiProfileId, value);
    }


    public double ProofreadSourceMaxChars
    {
        get => _proofreadSourceMaxChars;
        set => SetField(ref _proofreadSourceMaxChars, value);
    }

    public void Load()
    {
        _isLoading = true;
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
        StreamPauseEnabled = cfg.StreamPauseEnabled;
        StreamPauseSilenceMs = cfg.StreamPauseSilenceMs;
        StreamPauseThresholdDb = cfg.StreamPauseThresholdDb;
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
        VolcDebugLogIncludeSecrets = cfg.Volc.DebugLogIncludeSecrets;

        ApiProfiles.Clear();
        var profileList = cfg.ApiProfiles is not null && cfg.ApiProfiles.Count > 0
            ? cfg.ApiProfiles
            : new List<ApiProfile> { ApiProfile.CreateDefault() };
        foreach (var profile in profileList)
        {
            ApiProfiles.Add(new ApiProfileItem(profile));
        }

        DialogEnabled = cfg.DialogContext.Enabled;
        DialogApiProfileId = ResolveProfileId(cfg.DialogContext.ApiProfileId, profileList);
        DialogSourceMaxChars = cfg.DialogContext.SourceMaxChars;
        DialogMinUpdateChars = cfg.DialogContext.MinUpdateChars;
        DialogMaxSummaryChars = cfg.DialogContext.MaxSummaryChars;
        DialogTtlMinutes = cfg.DialogContext.TtlMinutes;

        ProofreadEnabled = cfg.Proofread.Enabled;
        ProofreadApiProfileId = ResolveProfileId(cfg.Proofread.ApiProfileId, profileList, DialogApiProfileId);
        ProofreadSourceMaxChars = cfg.Proofread.SourceMaxChars;
        _isLoading = false;
    }

    public bool Save(out string error)
    {
        error = "";
        try
        {
            var profiles = ApiProfiles.Select(p => p.ToConfig()).ToList();
            if (profiles.Count == 0)
            {
                profiles.Add(ApiProfile.CreateDefault());
            }

            var dialogProfileId = ResolveProfileId(DialogApiProfileId, profiles);
            var proofreadProfileId = ResolveProfileId(ProofreadApiProfileId, profiles, dialogProfileId);

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
                StreamPauseEnabled = StreamPauseEnabled,
                StreamPauseSilenceMs = ToInt(StreamPauseSilenceMs, 2000),
                StreamPauseThresholdDb = StreamPauseThresholdDb,
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
                    Language = VolcLanguage.Trim(),
                    DebugLogIncludeSecrets = VolcDebugLogIncludeSecrets
                },
                ApiProfiles = profiles,
                DialogContext = new DialogContextConfig
                {
                    Enabled = DialogEnabled,
                    ApiProfileId = dialogProfileId,
                    SourceMaxChars = ToInt(DialogSourceMaxChars, 800),
                    MinUpdateChars = ToInt(DialogMinUpdateChars, 8),
                    MaxSummaryChars = ToInt(DialogMaxSummaryChars, 200),
                    TtlMinutes = ToInt(DialogTtlMinutes, 240)
                },
                Proofread = new ProofreadConfig
                {
                    Enabled = ProofreadEnabled,
                    ApiProfileId = proofreadProfileId,
                    SourceMaxChars = ToInt(ProofreadSourceMaxChars, 800)
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

    private void ScheduleAutoSave()
    {
        lock (this)
        {
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = new System.Threading.Timer(_ =>
            {
                Save(out var _);
            }, null, AutoSaveDelayMs, Timeout.Infinite);
        }
    }

    public void AddApiProfile()
    {
        var profile = new ApiProfileItem(ApiProfile.CreateDefault())
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"API {ApiProfiles.Count + 1}"
        };
        ApiProfiles.Add(profile);
    }

    public void RemoveApiProfile(ApiProfileItem profile)
    {
        if (ApiProfiles.Count <= 1) return;
        ApiProfiles.Remove(profile);

        if (string.Equals(DialogApiProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            DialogApiProfileId = ApiProfiles[0].Id;
        }
        if (string.Equals(ProofreadApiProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            ProofreadApiProfileId = ApiProfiles[0].Id;
        }
    }

    private void OnApiProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ApiProfileItem item in e.OldItems)
            {
                item.PropertyChanged -= OnApiProfileChanged;
            }
        }
        if (e.NewItems is not null)
        {
            foreach (ApiProfileItem item in e.NewItems)
            {
                item.PropertyChanged += OnApiProfileChanged;
            }
        }

        if (_isLoading) return;
        ScheduleAutoSave();
    }

    private void OnApiProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoading) return;
        ScheduleAutoSave();
    }

    private static int ToInt(double value, int fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
        return Math.Max(0, (int)Math.Round(value));
    }

    private static string ResolveProfileId(string? id, List<ApiProfile> profiles, string? fallback = null)
    {
        if (profiles.Count == 0) return "";
        if (!string.IsNullOrWhiteSpace(id)
            && profiles.Any(p => string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return id!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback)
            && profiles.Any(p => string.Equals(p.Id, fallback.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return fallback!.Trim();
        }

        return profiles[0].Id;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
