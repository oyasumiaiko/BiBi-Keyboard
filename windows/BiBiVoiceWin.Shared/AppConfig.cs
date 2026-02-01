using System.Collections.Generic;
using System.Linq;
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
    public List<ApiProfile> ApiProfiles { get; init; } = new()
    {
        ApiProfile.CreateDefault()
    };
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

        var normalized = NormalizeConfig(config, content);
        return (normalized, configPath, false);
    }

    public static void Save(AppConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
    }

    public ApiProfile? ResolveApiProfile(string? id)
    {
        if (ApiProfiles is null || ApiProfiles.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(id)) return ApiProfiles[0];
        var trimmed = id.Trim();
        return ApiProfiles.FirstOrDefault(p => string.Equals(p.Id, trimmed, StringComparison.OrdinalIgnoreCase))
               ?? ApiProfiles[0];
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
            ApiProfiles = new List<ApiProfile>
            {
                ApiProfile.CreateDefault()
            },
            DialogContext = new DialogContextConfig
            {
                Enabled = false,
                ApiProfileId = "default",
                SourceMaxChars = 800,
                MinUpdateChars = 8,
                MaxSummaryChars = 200,
                TtlMinutes = 240
            },
            Proofread = new ProofreadConfig
            {
                Enabled = true,
                ApiProfileId = "default",
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

    private static AppConfig NormalizeConfig(AppConfig config, string json)
    {
        var hasProfilesInJson = TryHasProperty(json, "ApiProfiles");
        var profiles = config.ApiProfiles ?? new List<ApiProfile>();
        if (!hasProfilesInJson || profiles.Count == 0)
        {
            profiles = new List<ApiProfile>
            {
                ReadLegacyProfile(json) ?? ApiProfile.CreateDefault()
            };
        }

        var defaultProfileId = profiles[0].Id;
        var dialogCfg = config.DialogContext ?? new DialogContextConfig();
        var proofreadCfg = config.Proofread ?? new ProofreadConfig();
        var dialogApiId = string.IsNullOrWhiteSpace(dialogCfg.ApiProfileId)
            ? defaultProfileId
            : dialogCfg.ApiProfileId;

        var proofreadEnabled = proofreadCfg.Enabled;
        if (TryReadLegacyProofreadEnabled(json, out var legacyEnabled))
        {
            proofreadEnabled = legacyEnabled;
        }

        var proofreadApiId = string.IsNullOrWhiteSpace(proofreadCfg.ApiProfileId)
            ? dialogApiId
            : proofreadCfg.ApiProfileId;

        return new AppConfig
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
            ApiProfiles = profiles,
            DialogContext = new DialogContextConfig
            {
                Enabled = dialogCfg.Enabled,
                ApiProfileId = dialogApiId,
                SourceMaxChars = dialogCfg.SourceMaxChars,
                MinUpdateChars = dialogCfg.MinUpdateChars,
                MaxSummaryChars = dialogCfg.MaxSummaryChars,
                TtlMinutes = dialogCfg.TtlMinutes
            },
            Proofread = new ProofreadConfig
            {
                Enabled = proofreadEnabled,
                ApiProfileId = proofreadApiId,
                SourceMaxChars = proofreadCfg.SourceMaxChars
            }
        };
    }

    private static bool TryHasProperty(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out _);
        }
        catch
        {
            return false;
        }
    }

    private static ApiProfile? ReadLegacyProfile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("DialogContext", out var dialog)) return null;

            var endpoint = dialog.TryGetProperty("LlmEndpoint", out var v) ? v.GetString() : null;
            var apiKey = dialog.TryGetProperty("LlmApiKey", out v) ? v.GetString() : null;
            var model = dialog.TryGetProperty("LlmModel", out v) ? v.GetString() : null;
            var temp = dialog.TryGetProperty("LlmTemperature", out v) ? v.GetSingle() : 0.2f;
            var effort = dialog.TryGetProperty("LlmReasoningEffort", out v) ? v.GetString() : "low";
            var logSecrets = dialog.TryGetProperty("LlmLogIncludeSecrets", out v) && v.ValueKind == JsonValueKind.True;

            if (string.IsNullOrWhiteSpace(endpoint)
                && string.IsNullOrWhiteSpace(apiKey)
                && string.IsNullOrWhiteSpace(model))
            {
                return null;
            }

            return new ApiProfile
            {
                Id = "default",
                Name = "默认",
                Endpoint = endpoint ?? "",
                ApiKey = apiKey ?? "",
                Model = model ?? "",
                Temperature = temp,
                ReasoningEffort = string.IsNullOrWhiteSpace(effort) ? "low" : effort.Trim(),
                LogIncludeSecrets = logSecrets
            };
        }
        catch
        {
            return null;
        }
    }

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

public sealed class ApiProfile : ILlmConfig
{
    public string Id { get; init; } = "default";
    public string Name { get; init; } = "默认";
    public string Endpoint { get; init; } = "https://openrouter.ai/api/v1/chat/completions";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "google/gemini-3-flash-preview";
    public float Temperature { get; init; } = 0.2f;
    public string ReasoningEffort { get; init; } = "low";
    public bool LogIncludeSecrets { get; init; } = false;

    public string LlmEndpoint => Endpoint;
    public string LlmApiKey => ApiKey;
    public string LlmModel => Model;
    public float LlmTemperature => Temperature;
    public string LlmReasoningEffort => ReasoningEffort;
    public bool LlmLogIncludeSecrets => LogIncludeSecrets;

    public static ApiProfile CreateDefault()
    {
        return new ApiProfile
        {
            Id = "default",
            Name = "默认",
            Endpoint = "https://openrouter.ai/api/v1/chat/completions",
            ApiKey = "",
            Model = "google/gemini-3-flash-preview",
            Temperature = 0.2f,
            ReasoningEffort = "low",
            LogIncludeSecrets = false
        };
    }
}

public sealed class DialogContextConfig
{
    public bool Enabled { get; init; } = false;
    public string ApiProfileId { get; init; } = "default";

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
{
    public bool Enabled { get; init; } = true;
    public string ApiProfileId { get; init; } = "default";

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
