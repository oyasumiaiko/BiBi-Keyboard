using System.Windows.Forms;

namespace BiBiVoiceWin;

public readonly record struct HotkeySpec(uint Modifiers, Keys Key)
{
    public override string ToString()
    {
        // 仅用于显示；不追求完全可逆。
        var parts = new List<string>();
        if ((Modifiers & Win32.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & Win32.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & Win32.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & Win32.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}

public static class HotkeySpecParser
{
    /// <summary>
    /// 解析形如 "Ctrl+Alt+Space" 的全局热键配置。
    /// </summary>
    public static bool TryParse(string? text, out HotkeySpec spec, out string error)
    {
        spec = default;
        error = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "热键为空";
            return false;
        }

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = $"无法解析热键：{text}";
            return false;
        }

        uint modifiers = 0;
        Keys? key = null;

        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;

            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= Win32.MOD_CONTROL;
                    continue;
                case "alt":
                    modifiers |= Win32.MOD_ALT;
                    continue;
                case "shift":
                    modifiers |= Win32.MOD_SHIFT;
                    continue;
                case "win":
                case "windows":
                    modifiers |= Win32.MOD_WIN;
                    continue;
                case "spacebar":
                    key = Keys.Space;
                    continue;
            }

            if (Enum.TryParse<Keys>(token, ignoreCase: true, out var parsed))
            {
                key = parsed;
                continue;
            }

            if (token.Length == 1)
            {
                var ch = token[0];
                if (ch is >= 'a' and <= 'z') ch = char.ToUpperInvariant(ch);
                if (ch is >= 'A' and <= 'Z')
                {
                    key = (Keys)Enum.Parse(typeof(Keys), ch.ToString());
                    continue;
                }
                if (ch is >= '0' and <= '9')
                {
                    key = (Keys)Enum.Parse(typeof(Keys), "D" + ch);
                    continue;
                }
            }

            error = $"无法识别热键字段：{token}";
            return false;
        }

        if (key is null)
        {
            error = $"热键缺少主键：{text}";
            return false;
        }

        spec = new HotkeySpec(modifiers, key.Value);
        return true;
    }
}

