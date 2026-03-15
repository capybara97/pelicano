using System.ComponentModel;
using System.Runtime.InteropServices;
using Pelicano.Models;

namespace Pelicano;

/// <summary>
/// Win32 Shell_NotifyIcon 기반 시스템 트레이를 관리한다.
/// WinForms 없이도 트레이 아이콘, 메뉴, Explorer 재시작 복구를 지원한다.
/// </summary>
internal sealed class TrayManager : IDisposable
{
    private const uint TrayIconId = 1;
    private const uint TrayCallbackMessage = NativeMethods.WM_APP + 41;
    private const uint ShowMenuId = 1001;
    private const uint PlainTextMenuId = 1002;
    private const uint CaptureAssetsMenuId = 1003;
    private const uint SettingsMenuId = 1004;
    private const uint ClearHistoryMenuId = 1005;
    private const uint ExitMenuId = 1006;
    private readonly Action _showHistoryAction;
    private readonly Action _togglePlainTextAction;
    private readonly Action _toggleCaptureAssetsAction;
    private readonly Action _openSettingsAction;
    private readonly Action _clearHistoryAction;
    private readonly Action _exitAction;
    private readonly HiddenMessageWindow _window;
    private readonly IntPtr _iconHandle;
    private readonly bool _ownsIconHandle;
    private readonly int _taskbarCreatedMessage;
    private bool _captureAssetsEnabled;
    private bool _disposed;
    private bool _plainTextOnlyEnabled;

    public TrayManager(
        AppSettings settings,
        Action showHistoryAction,
        Action togglePlainTextAction,
        Action toggleCaptureAssetsAction,
        Action openSettingsAction,
        Action clearHistoryAction,
        Action exitAction)
    {
        _showHistoryAction = showHistoryAction;
        _togglePlainTextAction = togglePlainTextAction;
        _toggleCaptureAssetsAction = toggleCaptureAssetsAction;
        _openSettingsAction = openSettingsAction;
        _clearHistoryAction = clearHistoryAction;
        _exitAction = exitAction;
        _window = new HiddenMessageWindow(HandleWindowMessage);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _iconHandle = IconHelper.LoadApplicationIconHandle(out _ownsIconHandle);

        RefreshState(settings);
        AddTrayIcon();

        if (!settings.StartWithWindows)
        {
            ShowStartupHint();
        }
    }

    public bool IsAvailable => !_disposed;

    public void RefreshState(AppSettings settings)
    {
        _plainTextOnlyEnabled = settings.EnforcePlainTextOnly;
        _captureAssetsEnabled = settings.CaptureFileDrops || settings.CaptureImages;

        if (!_disposed)
        {
            UpdateTrayIcon();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DeleteTrayIcon();

        if (_ownsIconHandle && _iconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_iconHandle);
        }

        _window.Dispose();
        GC.SuppressFinalize(this);
    }

    private void AddTrayIcon()
    {
        var data = CreateNotifyIconData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "트레이 아이콘을 등록하지 못했습니다.");
        }

        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
    }

    private void UpdateTrayIcon()
    {
        var data = CreateNotifyIconData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private void DeleteTrayIcon()
    {
        var data = CreateNotifyIconData(0);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
    }

    private NativeMethods.NOTIFYICONDATA CreateNotifyIconData(uint flags)
    {
        return new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _window.Handle,
            uID = TrayIconId,
            uFlags = flags,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _iconHandle,
            szTip = "Pelicano - 클립보드 히스토리"
        };
    }

    private void ShowStartupHint()
    {
        var data = CreateNotifyIconData(
            NativeMethods.NIF_MESSAGE |
            NativeMethods.NIF_ICON |
            NativeMethods.NIF_TIP |
            NativeMethods.NIF_INFO);
        data.szInfoTitle = "Pelicano";
        data.szInfo = "트레이에서 실행 중입니다. 클릭하거나 Ctrl+Shift+V로 열 수 있습니다.";
        data.dwInfoFlags = NativeMethods.NIIF_INFO;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private void HandleWindowMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == (uint)_taskbarCreatedMessage)
        {
            AddTrayIcon();
            return;
        }

        if (message != TrayCallbackMessage)
        {
            return;
        }

        switch ((uint)lParam.ToInt64())
        {
            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.WM_LBUTTONDBLCLK:
                _showHistoryAction();
                break;

            case NativeMethods.WM_RBUTTONUP:
            case NativeMethods.WM_CONTEXTMENU:
                ShowContextMenu();
                break;
        }
    }

    private void ShowContextMenu()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var menuHandle = NativeMethods.CreatePopupMenu();
        if (menuHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenuItem(menuHandle, NativeMethods.MF_STRING, ShowMenuId, "히스토리 열기");
            AppendMenuItem(menuHandle, NativeMethods.MF_SEPARATOR, 0, null);
            AppendMenuItem(
                menuHandle,
                NativeMethods.MF_STRING | (_plainTextOnlyEnabled ? NativeMethods.MF_CHECKED : NativeMethods.MF_UNCHECKED),
                PlainTextMenuId,
                "Plain Text Only");
            AppendMenuItem(
                menuHandle,
                NativeMethods.MF_STRING | (_captureAssetsEnabled ? NativeMethods.MF_CHECKED : NativeMethods.MF_UNCHECKED),
                CaptureAssetsMenuId,
                "파일/이미지 캡처");
            AppendMenuItem(menuHandle, NativeMethods.MF_SEPARATOR, 0, null);
            AppendMenuItem(menuHandle, NativeMethods.MF_STRING, SettingsMenuId, "설정");
            AppendMenuItem(menuHandle, NativeMethods.MF_STRING, ClearHistoryMenuId, "전체 비우기");
            AppendMenuItem(menuHandle, NativeMethods.MF_SEPARATOR, 0, null);
            AppendMenuItem(menuHandle, NativeMethods.MF_STRING, ExitMenuId, "종료");

            NativeMethods.SetForegroundWindow(_window.Handle);
            var command = NativeMethods.TrackPopupMenuEx(
                menuHandle,
                NativeMethods.TPM_LEFTALIGN |
                NativeMethods.TPM_BOTTOMALIGN |
                NativeMethods.TPM_RIGHTBUTTON |
                NativeMethods.TPM_RETURNCMD,
                cursor.X,
                cursor.Y,
                _window.Handle,
                IntPtr.Zero);

            if (command > 0)
            {
                ExecuteMenuCommand(command);
            }

            NativeMethods.PostMessage(_window.Handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.DestroyMenu(menuHandle);
        }
    }

    private static void AppendMenuItem(IntPtr menuHandle, uint flags, uint id, string? text)
    {
        if (!NativeMethods.AppendMenu(menuHandle, flags, (nuint)id, text))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "트레이 메뉴를 구성하지 못했습니다.");
        }
    }

    private void ExecuteMenuCommand(uint command)
    {
        switch (command)
        {
            case ShowMenuId:
                _showHistoryAction();
                break;

            case PlainTextMenuId:
                _togglePlainTextAction();
                break;

            case CaptureAssetsMenuId:
                _toggleCaptureAssetsAction();
                break;

            case SettingsMenuId:
                _openSettingsAction();
                break;

            case ClearHistoryMenuId:
                _clearHistoryAction();
                break;

            case ExitMenuId:
                _exitAction();
                break;
        }
    }

    private sealed class HiddenMessageWindow : IDisposable
    {
        private const int ErrorClassAlreadyExists = 1410;
        private const uint WmNcDestroy = 0x0082;
        private const string WindowClassName = "Pelicano.Tray.HiddenMessageWindow";
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
                    "트레이 숨김 메시지 윈도우를 생성하지 못했습니다.");
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
                        throw new Win32Exception(error, "트레이 숨김 윈도우 클래스를 등록하지 못했습니다.");
                    }
                }

                _classRegistered = true;
            }
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

            window?._messageHandler(message, wParam, lParam);
            return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
        }
    }
}
