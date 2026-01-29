using System.Runtime.InteropServices;
using System.Text;

namespace BiBiVoiceWin;

internal static class Win32
{
    public const int WM_HOTKEY = 0x0312;
    public const int WM_PASTE = 0x0302;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    public const uint INPUT_KEYBOARD = 1;

    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static int LastSendInputError { get; private set; }

    public static bool TrySetForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        try { return SetForegroundWindow(hWnd); } catch { return false; }
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return "";
        var len = GetWindowTextLength(hWnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 2);
        var read = GetWindowText(hWnd, sb, sb.Capacity);
        if (read <= 0) return "";
        return sb.ToString();
    }

    public static bool SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;

        // Windows 的 KEYEVENTF_UNICODE 接受 UTF-16 code unit；因此直接按 char 发送即可。
        var inputs = new INPUT[text.Length * 2];
        var i = 0;
        foreach (var ch in text)
        {
            inputs[i++] = INPUT.KeyboardUnicode(ch, keyUp: false);
            inputs[i++] = INPUT.KeyboardUnicode(ch, keyUp: true);
        }

        return SendInputInternal(inputs);
    }

    public static bool SendCtrlV()
    {
        // Ctrl down, V down, V up, Ctrl up
        var inputs = new[]
        {
            INPUT.KeyboardVk(0x11, keyUp: false), // VK_CONTROL
            INPUT.KeyboardVk(0x56, keyUp: false), // 'V'
            INPUT.KeyboardVk(0x56, keyUp: true),
            INPUT.KeyboardVk(0x11, keyUp: true),
        };
        return SendInputInternal(inputs);
    }

    public static bool SendBackspace(int count)
    {
        if (count <= 0) return true;
        var inputs = new INPUT[count * 2];
        var i = 0;
        for (var n = 0; n < count; n++)
        {
            inputs[i++] = INPUT.KeyboardVk(0x08, keyUp: false); // VK_BACK
            inputs[i++] = INPUT.KeyboardVk(0x08, keyUp: true);
        }
        return SendInputInternal(inputs);
    }

    /// <summary>
    /// 尝试对目标窗口的“焦点控件”发送 WM_PASTE，作为 Ctrl+V 的兜底。
    /// </summary>
    public static bool SendPasteMessage(IntPtr targetWindow)
    {
        var focus = GetFocusWindow(targetWindow);
        if (focus == IntPtr.Zero) return false;
        var ok = SendMessageTimeout(focus, WM_PASTE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 500, out _);
        return ok != IntPtr.Zero;
    }

    private static IntPtr GetFocusWindow(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero) return IntPtr.Zero;
        var threadId = GetWindowThreadProcessId(targetWindow, out _);
        if (threadId == 0) return IntPtr.Zero;
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(threadId, ref info)) return IntPtr.Zero;
        if (info.hwndFocus != IntPtr.Zero) return info.hwndFocus;
        if (info.hwndActive != IntPtr.Zero) return info.hwndActive;
        return targetWindow;
    }

    private static bool SendInputInternal(INPUT[] inputs)
    {
        LastSendInputError = 0;
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            LastSendInputError = Marshal.GetLastWin32Error();
        }
        return sent == inputs.Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;

        public static INPUT KeyboardUnicode(char ch, bool keyUp)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        public static INPUT KeyboardVk(ushort vk, bool keyUp)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = 0,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
