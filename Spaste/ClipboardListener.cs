namespace Spaste;

/// <summary>
/// WM_CLIPBOARDUPDATE 메시지를 받기 위한 숨김 윈도우 래퍼다.
/// 별도 폼을 만들지 않고도 클립보드 변경을 안정적으로 감지한다.
/// </summary>
internal sealed class ClipboardListener : IDisposable
{
    private readonly ClipboardWindow _window;

    /// <summary>
    /// 클립보드 내용이 바뀌었을 때 발생한다.
    /// </summary>
    public event EventHandler? ClipboardChanged;

    /// <summary>
    /// 숨김 핸들을 생성하고 클립보드 리스너를 등록한다.
    /// </summary>
    public ClipboardListener()
    {
        _window = new ClipboardWindow(this);
    }

    /// <summary>
    /// 내부 메시지 윈도우가 이벤트를 외부로 전달한다.
    /// </summary>
    private void RaiseClipboardChanged()
    {
        ClipboardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 등록을 해제하고 핸들을 정리한다.
    /// </summary>
    public void Dispose()
    {
        _window.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 실제 Win32 메시지를 받는 숨김 윈도우다.
    /// </summary>
    private sealed class ClipboardWindow : NativeWindow, IDisposable
    {
        private readonly ClipboardListener _owner;

        public ClipboardWindow(ClipboardListener owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams());
            NativeMethods.AddClipboardFormatListener(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                _owner.RaiseClipboardChanged();
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }

            GC.SuppressFinalize(this);
        }
    }
}
