using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BiBiVoiceWin;

/// <summary>
/// 全局热键注册器（RegisterHotKey）。
/// - 采用隐藏消息窗口接收 WM_HOTKEY
/// - 不会抢占焦点，适合“对前台应用输入文字”的场景
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly int _id;
    private bool _disposed;

    public event EventHandler? Pressed;

    public HotkeySpec Spec { get; }

    public HotkeyManager(HotkeySpec spec, int id = 1)
    {
        Spec = spec;
        _id = id;
        _window = new MessageWindow(OnWndProcHotkey);

        if (!Win32.RegisterHotKey(_window.Handle, _id, spec.Modifiers, (uint)spec.Key))
        {
            var err = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            throw new InvalidOperationException($"注册全局热键失败：{spec}（{err}）");
        }
    }

    private void OnWndProcHotkey(int id)
    {
        if (id != _id) return;
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Win32.UnregisterHotKey(_window.Handle, _id);
        }
        catch
        {
            // 忽略；退出时尽量不阻塞
        }
        _window.Dispose();
    }

    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        private readonly Action<int> _onHotkey;

        public MessageWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_HOTKEY)
            {
                _onHotkey((int)m.WParam);
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }
}

