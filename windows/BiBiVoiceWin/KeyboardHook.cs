using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BiBiVoiceWin;

/// <summary>
/// 低级键盘钩子（WH_KEYBOARD_LL）：用于监听按键按下/抬起。
/// 说明：
/// - 适合实现“按住说话，松开停止”的模式
/// - 可选择是否吞掉原始按键事件（避免空格被插入）
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int LLKHF_INJECTED = 0x00000010;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _disposed;

    public event EventHandler<KeyboardHookEventArgs>? KeyEvent;

    public KeyboardHook()
    {
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            var err = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            throw new InvalidOperationException($"安装键盘钩子失败：{err}");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && KeyEvent is not null)
        {
            var msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYDOWN || msg == WM_SYSKEYUP)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var isInjected = (info.flags & LLKHF_INJECTED) != 0;
                var args = new KeyboardHookEventArgs((Keys)info.vkCode, msg, isInjected);
                try { KeyEvent.Invoke(this, args); } catch { }
                if (args.Suppress)
                {
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook); } catch { }
        _hook = IntPtr.Zero;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    public sealed class KeyboardHookEventArgs : EventArgs
    {
        public Keys Key { get; }
        public bool IsKeyDown { get; }
        public bool IsKeyUp { get; }
        public bool IsInjected { get; }
        public bool Suppress { get; set; }

        public KeyboardHookEventArgs(Keys key, int msg, bool isInjected)
        {
            Key = key;
            IsKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            IsKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            IsInjected = isInjected;
        }
    }
}
