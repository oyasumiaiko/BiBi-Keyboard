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

    public VolcConfig Volc { get; init; } = new();
    public DialogContextConfig DialogContext { get; init; } = new();

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

        return (config, configPath, false);
    }

    public static void Save(AppConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json);
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
                Language = ""
            },
            DialogContext = new DialogContextConfig
            {
                Enabled = false,
                LlmEndpoint = "https://api.openai.com/v1/chat/completions",
                LlmApiKey = "",
                LlmModel = "gpt-4o-mini",
                LlmTemperature = 0.2f,
                SourceMaxChars = 800,
                MinUpdateChars = 8,
                MaxSummaryChars = 200,
                TtlMinutes = 240
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
}

public sealed class DialogContextConfig
{
    public bool Enabled { get; init; } = false;
    public string LlmEndpoint { get; init; } = "https://api.openai.com/v1/chat/completions";
    public string LlmApiKey { get; init; } = "";
    public string LlmModel { get; init; } = "gpt-4o-mini";
    public float LlmTemperature { get; init; } = 0.2f;

    // 单次输入给 LLM 的最大字符数（防止超长文本拖慢）
    public int SourceMaxChars { get; init; } = 800;
    // 短文本不做摘要更新，避免噪声
    public int MinUpdateChars { get; init; } = 8;
    // 对话上下文摘要最大长度
    public int MaxSummaryChars { get; init; } = 200;
    // 上下文过期时间（分钟）
    public int TtlMinutes { get; init; } = 240;
}
