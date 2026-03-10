using Spaste.Models;

namespace Spaste;

/// <summary>
/// 시스템 트레이 아이콘과 우클릭 메뉴를 관리한다.
/// 운영 중 자주 쓰는 토글은 트레이에서 바로 접근할 수 있게 유지한다.
/// </summary>
internal sealed class TrayManager : IDisposable
{
    private readonly Icon _appIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _plainTextItem;
    private readonly ToolStripMenuItem _captureFilesItem;
    private readonly System.Windows.Forms.Timer _visibilityTimer;
    private readonly TaskbarCreatedWindow _taskbarCreatedWindow;
    private readonly string _notifyText = "Pelicano - 클립보드 히스토리";
    private int _visibilityRefreshCount;

    /// <summary>
    /// 트레이 아이콘과 메뉴를 초기화한다.
    /// </summary>
    public TrayManager(
        Icon appIcon,
        AppSettings settings,
        Action showHistoryAction,
        Action togglePlainTextAction,
        Action toggleCaptureFilesAction,
        Action openSettingsAction,
        Action clearHistoryAction,
        Action exitAction)
    {
        _appIcon = appIcon;
        _plainTextItem = new ToolStripMenuItem("Plain Text Only")
        {
            CheckOnClick = false
        };
        _plainTextItem.Click += (_, _) => togglePlainTextAction();

        _captureFilesItem = new ToolStripMenuItem("파일/이미지 캡처")
        {
            CheckOnClick = false
        };
        _captureFilesItem.Click += (_, _) => toggleCaptureFilesAction();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(new ToolStripMenuItem("히스토리 열기", null, (_, _) => showHistoryAction()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_plainTextItem);
        _menu.Items.Add(_captureFilesItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("설정", null, (_, _) => openSettingsAction()));
        _menu.Items.Add(new ToolStripMenuItem("전체 비우기", null, (_, _) => clearHistoryAction()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("종료", null, (_, _) => exitAction()));

        _notifyIcon = new NotifyIcon
        {
            Icon = appIcon,
            Visible = false,
            Text = _notifyText,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                showHistoryAction();
            }
        };
        _notifyIcon.DoubleClick += (_, _) => showHistoryAction();

        _taskbarCreatedWindow = new TaskbarCreatedWindow(RefreshTrayIcon);
        _visibilityTimer = new System.Windows.Forms.Timer
        {
            Interval = 1200
        };
        _visibilityTimer.Tick += (_, _) =>
        {
            RefreshTrayIcon();
            _visibilityRefreshCount += 1;

            if (_visibilityRefreshCount >= 3)
            {
                _visibilityTimer.Stop();
            }
        };

        RefreshState(settings);
        RefreshTrayIcon();
        _visibilityTimer.Start();

        if (!settings.StartWithWindows)
        {
            ShowStartupHint();
        }
    }

    /// <summary>
    /// 현재 설정 상태를 트레이 체크 메뉴에 반영한다.
    /// </summary>
    public void RefreshState(AppSettings settings)
    {
        _plainTextItem.Checked = settings.EnforcePlainTextOnly;
        _captureFilesItem.Checked = settings.CaptureImages;
    }

    /// <summary>
    /// 트레이 아이콘과 컨텍스트 메뉴를 해제한다.
    /// </summary>
    public void Dispose()
    {
        _visibilityTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _visibilityTimer.Dispose();
        _taskbarCreatedWindow.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 트레이 아이콘 표시를 다시 토글해 초기 표시 누락과 Explorer 재시작을 복구한다.
    /// </summary>
    private void RefreshTrayIcon()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = _appIcon;
        _notifyIcon.Text = _notifyText;
        _notifyIcon.Visible = true;
    }

    /// <summary>
    /// 첫 실행 시 트레이 위치와 단축키를 간단히 안내한다.
    /// </summary>
    private void ShowStartupHint()
    {
        try
        {
            _notifyIcon.BalloonTipTitle = "Pelicano";
            _notifyIcon.BalloonTipText = "트레이에서 실행 중입니다. 클릭하거나 Ctrl+Shift+V로 열 수 있습니다.";
            _notifyIcon.ShowBalloonTip(2500);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Explorer가 다시 시작되면 TaskbarCreated 메시지를 받아 트레이 아이콘을 복구한다.
    /// </summary>
    private sealed class TaskbarCreatedWindow : NativeWindow, IDisposable
    {
        private readonly Action _restoreTrayAction;
        private readonly int _taskbarCreatedMessage;

        public TaskbarCreatedWindow(Action restoreTrayAction)
        {
            _restoreTrayAction = restoreTrayAction;
            _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == _taskbarCreatedMessage)
            {
                _restoreTrayAction();
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
