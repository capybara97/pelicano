using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pelicano;

/// <summary>
/// WinUI 창과 분리된 숨김 Win32 메시지 윈도우로
/// 클립보드 변경과 전역 단축키를 안정적으로 수신한다.
/// </summary>
internal sealed class WindowMessageMonitor : IDisposable
{
    private const int HotKeyId = 20260314;
    private const nuint ClipboardTimerId = 20260315;
    private const uint ClipboardDispatchDelayMs = 90;
    private readonly HiddenMessageWindow _messageWindow;
    private bool _disposed;

    public WindowMessageMonitor(IntPtr ownerWindowHandle)
    {
        _messageWindow = new HiddenMessageWindow(HandleWindowMessage);

        if (!NativeMethods.AddClipboardFormatListener(_messageWindow.Handle))
        {
            var error = Marshal.GetLastWin32Error();
            _messageWindow.Dispose();
            throw new Win32Exception(error, "Hidden clipboard listener window registration failed.");
        }

        IsHotKeyRegistered = NativeMethods.RegisterHotKey(
            _messageWindow.Handle,
            HotKeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
            NativeMethods.VK_V);
    }

    public bool IsHotKeyRegistered { get; }

    public event EventHandler? ClipboardUpdated;

    public event EventHandler? HotKeyPressed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (IsHotKeyRegistered)
        {
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, HotKeyId);
        }

        NativeMethods.KillTimer(_messageWindow.Handle, ClipboardTimerId);
        NativeMethods.RemoveClipboardFormatListener(_messageWindow.Handle);
        _messageWindow.Dispose();
        GC.SuppressFinalize(this);
    }

    private void HandleWindowMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case NativeMethods.WM_CLIPBOARDUPDATE:
                NativeMethods.KillTimer(_messageWindow.Handle, ClipboardTimerId);
                NativeMethods.SetTimer(
                    _messageWindow.Handle,
                    ClipboardTimerId,
                    ClipboardDispatchDelayMs,
                    IntPtr.Zero);
                break;

            case NativeMethods.WM_TIMER when (nuint)wParam == ClipboardTimerId:
                NativeMethods.KillTimer(_messageWindow.Handle, ClipboardTimerId);
                ClipboardUpdated?.Invoke(this, EventArgs.Empty);
                break;

            case NativeMethods.WM_HOTKEY:
                HotKeyPressed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    /// <summary>
    /// WinUI 메시지 루프에 매달리는 숨김 네이티브 윈도우다.
    /// </summary>
    private sealed class HiddenMessageWindow : IDisposable
    {
        private const int ErrorClassAlreadyExists = 1410;
        private const uint WmNcDestroy = 0x0082;
        private const string WindowClassName = "Pelicano.WinUI.HiddenMessageWindow";
        private static readonly object Sync = new();
        private static readonly Dictionary<IntPtr, HiddenMessageWindow> Windows = [];
        private static readonly NativeMethods.WindowProc WindowProc = StaticWindowProc;
        private static bool _classRegistered;
        private readonly Action<uint, IntPtr, IntPtr> _messageHandler;

        public HiddenMessageWindow(Action<uint, IntPtr, IntPtr> messageHandler)
        {
            _messageHandler = messageHandler;
            EnsureClassRegistered();

            Handle = NativeMethods.CreateWindowEx(
                NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
                WindowClassName,
                string.Empty,
                NativeMethods.WS_OVERLAPPED,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.GetModuleHandle(null),
                IntPtr.Zero);

            if (Handle == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Hidden message window creation failed.");
            }

            lock (Sync)
            {
                Windows[Handle] = this;
            }
        }

        public IntPtr Handle { get; private set; }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero)
            {
                return;
            }

            var handle = Handle;
            Handle = IntPtr.Zero;

            lock (Sync)
            {
                Windows.Remove(handle);
            }

            NativeMethods.DestroyWindow(handle);
            GC.SuppressFinalize(this);
        }

        private static void EnsureClassRegistered()
        {
            lock (Sync)
            {
                if (_classRegistered)
                {
                    return;
                }

                var moduleHandle = NativeMethods.GetModuleHandle(null);
                var windowClass = new NativeMethods.WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                    hInstance = moduleHandle,
                    lpszClassName = WindowClassName,
                    lpfnWndProc = WindowProc
                };

                var atom = NativeMethods.RegisterClassEx(ref windowClass);
                if (atom == 0)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorClassAlreadyExists)
                    {
                        throw new Win32Exception(error, "Hidden message window class registration failed.");
                    }
                }

                _classRegistered = true;
            }
        }

        private void Dispatch(uint message, IntPtr wParam, IntPtr lParam)
        {
            _messageHandler(message, wParam, lParam);
        }

        private static IntPtr StaticWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            HiddenMessageWindow? window = null;

            lock (Sync)
            {
                Windows.TryGetValue(hWnd, out window);
                if (message == WmNcDestroy)
                {
                    Windows.Remove(hWnd);
                }
            }

            window?.Dispatch(message, wParam, lParam);
            return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
        }
    }
}
