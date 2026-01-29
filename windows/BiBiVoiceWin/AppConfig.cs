using System.Text.Json;

namespace BiBiVoiceWin;

public sealed class AppConfig
{
    public string Hotkey { get; init; } = "Alt+Space";
    public string InsertMode { get; init; } = "Clipboard";
    public bool AppendSpace { get; init; } = false;

    public bool HoldToTalkEnabled { get; init; } = true;
    public string HoldToTalkKey { get; init; } = "Space";
    public int HoldToTalkMinHoldMs { get; init; } = 200;

    public int TargetSampleRate { get; init; } = 16000;
    public int MaxRecordSeconds { get; init; } = 60;

    public bool AutoStopEnabled { get; init; } = true;
    public int AutoStopSilenceMs { get; init; } = 1200;
    public double AutoStopThresholdDb { get; init; } = -35;

    public VolcConfig Volc { get; init; } = new();

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

    private static AppConfig CreateExample()
    {
        return new AppConfig
        {
            Hotkey = "Alt+Space",
            InsertMode = "Clipboard",
            AppendSpace = false,
            HoldToTalkEnabled = true,
            HoldToTalkKey = "Space",
            HoldToTalkMinHoldMs = 200,
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
                EnableDdc = false
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
    public bool EnableDdc { get; init; } = false;
}
