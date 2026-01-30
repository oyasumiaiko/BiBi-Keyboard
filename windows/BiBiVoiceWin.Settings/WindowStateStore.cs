using System.Text.Json;
using Windows.Graphics;

namespace BiBiVoiceWin.Settings;

internal static class WindowStateStore
{
    private const int DefaultWidth = 860;
    private const int DefaultHeight = 620;
    private const int MinWidth = 700;
    private const int MinHeight = 520;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    internal sealed class WindowState
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private static string StatePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BiBiVoiceWin",
            "settings-ui.json"
        );

    public static WindowState? Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            var content = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<WindowState>(content, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static SizeInt32 NormalizeSize(WindowState? state)
    {
        var width = state?.Width > 0 ? state!.Width : DefaultWidth;
        var height = state?.Height > 0 ? state!.Height : DefaultHeight;

        if (width < MinWidth) width = MinWidth;
        if (height < MinHeight) height = MinHeight;

        return new SizeInt32(width, height);
    }

    public static void Save(SizeInt32 size)
    {
        try
        {
            var dir = Path.GetDirectoryName(StatePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            var state = new WindowState
            {
                Width = size.Width,
                Height = size.Height
            };
            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(StatePath, json);
        }
        catch
        {
            // 保存失败不影响主流程。
        }
    }
}
