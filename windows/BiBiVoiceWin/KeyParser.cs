using System.Windows.Forms;

namespace BiBiVoiceWin;

/// <summary>
/// 按键字符串解析器（仅支持单键，用于“按住说话”）。
/// </summary>
public static class KeyParser
{
    public static bool TryParseSingleKey(string? text, out Keys key, out string error)
    {
        key = Keys.None;
        error = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "按键为空";
            return false;
        }

        var raw = text.Trim();
        if (raw.Contains('+'))
        {
            error = "按住说话只支持单键（不要包含 Ctrl/Alt/Shift）";
            return false;
        }

        // 常见别名
        var normalized = raw.ToLowerInvariant() switch
        {
            "space" => "Space",
            "spacebar" => "Space",
            "空格" => "Space",
            _ => raw
        };

        if (Enum.TryParse(normalized, true, out Keys parsed) && parsed != Keys.None)
        {
            key = parsed;
            return true;
        }

        error = $"无法识别按键：{text}";
        return false;
    }
}
