using System.Windows.Automation;

namespace BiBiVoiceWin;

/// <summary>
/// 尝试读取当前焦点输入控件的文本，用于提供“输入上下文”给校对模型。
/// 说明：不同应用对 UIA 支持不一致，失败时直接返回 null。
/// </summary>
internal static class InputContextReader
{
    public static string? TryReadFocusedText()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null) return null;

            var isPassword = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty) as bool? == true;
            if (isPassword) return null;

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObj))
            {
                var textPattern = (TextPattern)textPatternObj;
                var text = textPattern.DocumentRange.GetText(-1);
                return Normalize(text);
            }

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObj))
            {
                var valuePattern = (ValuePattern)valuePatternObj;
                if (valuePattern.Current.IsReadOnly) return null;
                return Normalize(valuePattern.Current.Value);
            }
        }
        catch
        {
            // UIA 在某些应用中可能抛异常，忽略即可。
        }

        return null;
    }

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
