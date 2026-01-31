using System.Text.Json;

namespace BiBiVoiceWin;

public sealed class AppConfig
{
    public string Hotkey { get; init; } = "Space";
    public string InsertMode { get; init; } = "SendInput";
    public bool AppendSpace { get; init; } = false;

    public bool HoldToTalkEnabled { get; init; } = true;
    public string HoldToTalkKey { get; init; } = "Space";
    public int HoldToTalkMinHoldMs { get; init; } = 500;

    public int TargetSampleRate { get; init; } = 16000;
    public int MaxRecordSeconds { get; init; } = 60;

    public bool AutoStopEnabled { get; init; } = true;
    public int AutoStopSilenceMs { get; init; } = 1200;
    public double AutoStopThresholdDb { get; init; } = -35;
    public int TranscribeWatchdogSeconds { get; init; } = 15;

    public VolcConfig Volc { get; init; } = new();
    public DialogContextConfig DialogContext { get; init; } = new();
    public ProofreadConfig Proofread { get; init; } = new();

    public static (AppConfig Config, string ConfigPath, bool Created) LoadOrCreate()
    {
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        var baseDirPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        var userPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BiBiVoiceWin",
            "config.json"
        );

        var configPath = File.Exists(cwdPath)
            ? cwdPath
            : File.Exists(baseDirPath)
                ? baseDirPath
                : userPath;

        if (!File.Exists(configPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var example = CreateExample();
            var json = JsonSerializer.Serialize(example, JsonOptions);
            File.WriteAllText(configPath, json);
            return (example, configPath, true);
        }

        var content = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<AppConfig>(content, JsonOptions);
        if (config is null)
        {
            throw new InvalidOperationException($"无法解析配置文件：{configPath}");
        }

        // 兼容旧配置：如果没有 Proofread 段，则尝试读取 DialogContext.ProofreadEnabled。
        if (TryReadLegacyProofreadEnabled(content, out var legacyEnabled))
        {
            config = new AppConfig
            {
                Hotkey = config.Hotkey,
                InsertMode = config.InsertMode,
                AppendSpace = config.AppendSpace,
                HoldToTalkEnabled = config.HoldToTalkEnabled,
                HoldToTalkKey = config.HoldToTalkKey,
                HoldToTalkMinHoldMs = config.HoldToTalkMinHoldMs,
                TargetSampleRate = config.TargetSampleRate,
                MaxRecordSeconds = config.MaxRecordSeconds,
                AutoStopEnabled = config.AutoStopEnabled,
                AutoStopSilenceMs = config.AutoStopSilenceMs,
                AutoStopThresholdDb = config.AutoStopThresholdDb,
                TranscribeWatchdogSeconds = config.TranscribeWatchdogSeconds,
                Volc = config.Volc,
                DialogContext = config.DialogContext,
                Proofread = new ProofreadConfig
                {
                    Enabled = legacyEnabled
                }
            };
        }

        return (config, configPath, false);
    }

    public static void Save(AppConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 兼容旧配置：如果未单独填写校对配置，则回退使用对话上下文的 LLM 参数。
    /// </summary>
    public ProofreadConfig ResolveProofreadConfig()
    {
        var proofread = Proofread ?? new ProofreadConfig();
        var dialog = DialogContext ?? new DialogContextConfig();

        return new ProofreadConfig
        {
            Enabled = proofread.Enabled,
            LlmEndpoint = string.IsNullOrWhiteSpace(proofread.LlmEndpoint) ? dialog.LlmEndpoint : proofread.LlmEndpoint,
            LlmApiKey = string.IsNullOrWhiteSpace(proofread.LlmApiKey) ? dialog.LlmApiKey : proofread.LlmApiKey,
            LlmModel = string.IsNullOrWhiteSpace(proofread.LlmModel) ? dialog.LlmModel : proofread.LlmModel,
            LlmTemperature = proofread.LlmTemperature,
            LlmReasoningEffort = string.IsNullOrWhiteSpace(proofread.LlmReasoningEffort)
                ? dialog.LlmReasoningEffort
                : proofread.LlmReasoningEffort,
            LlmLogIncludeSecrets = proofread.LlmLogIncludeSecrets,
            SourceMaxChars = proofread.SourceMaxChars > 0 ? proofread.SourceMaxChars : dialog.SourceMaxChars
        };
    }

    private static AppConfig CreateExample()
    {
        return new AppConfig
        {
            Hotkey = "Space",
            InsertMode = "SendInput",
            AppendSpace = false,
            HoldToTalkEnabled = true,
            HoldToTalkKey = "Space",
            HoldToTalkMinHoldMs = 500,
            TargetSampleRate = 16000,
            MaxRecordSeconds = 60,
            AutoStopEnabled = true,
            AutoStopSilenceMs = 1200,
            AutoStopThresholdDb = -35,
            TranscribeWatchdogSeconds = 15,
            Volc = new VolcConfig
            {
                Endpoint = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async",
                AppKey = "",
                AccessKey = "",
                ResourceId = "volc.seedasr.sauc.duration",
                EnableDdc = true,
                EnableNonstream = true,
                EnableVad = false,
                VadEndWindowSizeMs = 800,
                VadForceToSpeechTimeMs = 1000,
                Language = "",
                DebugLogIncludeSecrets = false
            },
            DialogContext = new DialogContextConfig
            {
                Enabled = false,
                LlmEndpoint = "https://openrouter.ai/api/v1/chat/completions",
                LlmApiKey = "",
                LlmModel = "google/gemini-3-flash-preview",
                LlmTemperature = 0.2f,
                LlmReasoningEffort = "low",
                LlmLogIncludeSecrets = false,
                SourceMaxChars = 800,
                MinUpdateChars = 8,
                MaxSummaryChars = 200,
                TtlMinutes = 240
            },
            Proofread = new ProofreadConfig
            {
                Enabled = true,
                LlmEndpoint = "https://openrouter.ai/api/v1/chat/completions",
                LlmApiKey = "",
                LlmModel = "google/gemini-3-flash-preview",
                LlmTemperature = 0.2f,
                LlmReasoningEffort = "low",
                LlmLogIncludeSecrets = false,
                SourceMaxChars = 800
            }
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private static bool TryReadLegacyProofreadEnabled(string json, out bool enabled)
    {
        enabled = false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Proofread", out _)) return false;
            if (!doc.RootElement.TryGetProperty("DialogContext", out var dialog)) return false;
            if (!dialog.TryGetProperty("ProofreadEnabled", out var legacy)) return false;
            enabled = legacy.GetBoolean();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class VolcConfig
{
    public string Endpoint { get; init; } = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async";
    public string AppKey { get; init; } = "";
    public string AccessKey { get; init; } = "";
    public string ResourceId { get; init; } = "volc.seedasr.sauc.duration";
    public bool EnableDdc { get; init; } = true;
    public bool EnableNonstream { get; init; } = true;
    public bool EnableVad { get; init; } = false;
    public int VadEndWindowSizeMs { get; init; } = 800;
    public int VadForceToSpeechTimeMs { get; init; } = 1000;
    public string Language { get; init; } = "";
    // 是否把密钥原文写入日志（高风险，默认关闭）。
    public bool DebugLogIncludeSecrets { get; init; } = false;
}

public sealed class DialogContextConfig : ILlmConfig
{
    public bool Enabled { get; init; } = false;
    public string LlmEndpoint { get; init; } = "https://openrouter.ai/api/v1/chat/completions";
    public string LlmApiKey { get; init; } = "";
    public string LlmModel { get; init; } = "google/gemini-3-flash-preview";
    public float LlmTemperature { get; init; } = 0.2f;
    public string LlmReasoningEffort { get; init; } = "low";
    public bool LlmLogIncludeSecrets { get; init; } = false;

    // 单次输入给 LLM 的最大字符数（防止超长文本拖慢）
    public int SourceMaxChars { get; init; } = 800;
    // 短文本不做摘要更新，避免噪声
    public int MinUpdateChars { get; init; } = 8;
    // 对话上下文摘要最大长度
    public int MaxSummaryChars { get; init; } = 200;
    // 上下文过期时间（分钟）
    public int TtlMinutes { get; init; } = 240;
}

public sealed class ProofreadConfig
    : ILlmConfig
{
    public bool Enabled { get; init; } = true;
    public string LlmEndpoint { get; init; } = "https://openrouter.ai/api/v1/chat/completions";
    public string LlmApiKey { get; init; } = "";
    public string LlmModel { get; init; } = "google/gemini-3-flash-preview";
    public float LlmTemperature { get; init; } = 0.2f;
    public string LlmReasoningEffort { get; init; } = "low";
    public bool LlmLogIncludeSecrets { get; init; } = false;

    // 单次输入给 LLM 的最大字符数（防止超长文本拖慢）
    public int SourceMaxChars { get; init; } = 800;
}

public interface ILlmConfig
{
    string LlmEndpoint { get; }
    string LlmApiKey { get; }
    string LlmModel { get; }
    float LlmTemperature { get; }
    string LlmReasoningEffort { get; }
    bool LlmLogIncludeSecrets { get; }
}
