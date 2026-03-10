namespace Spaste;

/// <summary>
/// Ctrl+Shift+V 전역 단축키를 등록해 히스토리 창을 빠르게 여는 관리자다.
/// </summary>
internal sealed class HotKeyManager : IDisposable
{
    private const int HotKeyId = 20260310;
    private readonly HotKeyWindow _window;

    /// <summary>
    /// 단축키 등록 성공 여부다.
    /// </summary>
    public bool IsRegistered { get; }

    /// <summary>
    /// 단축키가 눌렸을 때 발생한다.
    /// </summary>
    public event EventHandler? HotKeyPressed;

    /// <summary>
    /// 지정한 조합을 전역 단축키로 등록한다.
    /// </summary>
    public HotKeyManager(uint modifiers, Keys key)
    {
        _window = new HotKeyWindow(this);
        IsRegistered = NativeMethods.RegisterHotKey(_window.Handle, HotKeyId, modifiers, (uint)key);
    }

    /// <summary>
    /// 내부 메시지 윈도우가 단축키 이벤트를 외부로 전달한다.
    /// </summary>
    private void RaiseHotKeyPressed()
    {
        HotKeyPressed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 단축키 등록을 해제한다.
    /// </summary>
    public void Dispose()
    {
        if (_window.Handle != IntPtr.Zero && IsRegistered)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, HotKeyId);
        }

        _window.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// WM_HOTKEY를 수신하는 숨김 윈도우다.
    /// </summary>
    private sealed class HotKeyWindow : NativeWindow, IDisposable
    {
        private readonly HotKeyManager _owner;

        public HotKeyWindow(HotKeyManager owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                _owner.RaiseHotKeyPressed();
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }

            GC.SuppressFinalize(this);
        }
    }
}
