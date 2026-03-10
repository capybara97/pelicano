using Spaste.Models;

namespace Spaste;

/// <summary>
/// 사용자 설정을 수정하는 모달 창이다.
/// 기업 환경에서 자주 바뀌는 옵션만 최소 UI로 노출해 운영을 단순화한다.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly CheckBox _plainTextOnlyCheckBox;
    private readonly CheckBox _captureImagesCheckBox;
    private readonly CheckBox _auditLoggingCheckBox;
    private readonly CheckBox _startWithWindowsCheckBox;
    private readonly CheckBox _darkModeCheckBox;
    private readonly NumericUpDown _maxHistoryCountNumeric;

    /// <summary>
    /// 사용자가 저장한 설정 결과다.
    /// </summary>
    public AppSettings EditedSettings { get; private set; }

    /// <summary>
    /// 현재 설정 값을 받아 편집 화면을 구성한다.
    /// </summary>
    public SettingsForm(AppSettings currentSettings)
    {
        EditedSettings = currentSettings.Clone();
        Text = "Pelicano 설정";
        Icon = IconHelper.LoadApplicationIcon();
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 430);
        MinimumSize = new Size(560, 430);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(20),
            AutoScroll = true
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = "보안/운영 설정",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        };

        _plainTextOnlyCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "복사 즉시 Plain Text Only 강제 적용",
            Checked = currentSettings.EnforcePlainTextOnly,
            Margin = new Padding(0, 10, 0, 0)
        };

        _captureImagesCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "파일 및 이미지 복사 히스토리 저장",
            Checked = currentSettings.CaptureImages,
            Margin = new Padding(0, 10, 0, 0)
        };

        _auditLoggingCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "로그 활성화",
            Checked = currentSettings.EnableAuditLogging,
            Margin = new Padding(0, 10, 0, 0)
        };

        _startWithWindowsCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Windows 시작 시 자동 실행",
            Checked = currentSettings.StartWithWindows,
            Margin = new Padding(0, 10, 0, 0)
        };

        var historyPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 16, 0, 0)
        };
        historyPanel.Controls.Add(new Label
        {
            Text = "최대 히스토리 개수:",
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0)
        });
        _maxHistoryCountNumeric = new NumericUpDown
        {
            Minimum = 20,
            Maximum = 500,
            Increment = 10,
            Value = currentSettings.MaxHistoryItems,
            Width = 90
        };
        historyPanel.Controls.Add(_maxHistoryCountNumeric);

        var descriptionLabel = new Label
        {
            AutoSize = true,
            Text =
                "단축키는 Ctrl+Shift+V로 고정됩니다.\r\n" +
                "목록은 한 번 클릭하면 바로 복사되고, 복수 선택도 지원합니다.\r\n" +
                "화이트 테마를 기본으로 사용하며, 설정 변경은 즉시 반영됩니다.",
            MaximumSize = new Size(480, 0),
            Margin = new Padding(0, 18, 0, 0)
        };

        _darkModeCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "화이트 테마 고정",
            Checked = true,
            Enabled = false,
            Margin = new Padding(0, 10, 0, 0)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 20, 0, 0)
        };

        var saveButton = new Button
        {
            Text = "저장",
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Padding = new Padding(18, 8, 18, 8)
        };
        ThemeHelper.StyleActionButton(saveButton, primary: true);
        saveButton.Click += (_, _) => SaveSettings();

        var cancelButton = new Button
        {
            Text = "취소",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
            Padding = new Padding(18, 8, 18, 8)
        };
        ThemeHelper.StyleActionButton(cancelButton, primary: false);

        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(_plainTextOnlyCheckBox, 0, 1);
        layout.Controls.Add(_captureImagesCheckBox, 0, 2);
        layout.Controls.Add(_auditLoggingCheckBox, 0, 3);
        layout.Controls.Add(_startWithWindowsCheckBox, 0, 4);
        layout.Controls.Add(historyPanel, 0, 5);
        layout.Controls.Add(_darkModeCheckBox, 0, 6);
        layout.Controls.Add(descriptionLabel, 0, 7);
        layout.Controls.Add(buttonPanel, 0, 8);

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        ThemeHelper.Apply(this, darkMode: false);
    }

    /// <summary>
    /// 컨트롤 값을 읽어 EditedSettings에 반영한다.
    /// </summary>
    private void SaveSettings()
    {
        EditedSettings = new AppSettings
        {
            EnforcePlainTextOnly = _plainTextOnlyCheckBox.Checked,
            CaptureImages = _captureImagesCheckBox.Checked,
            EnableAuditLogging = _auditLoggingCheckBox.Checked,
            StartWithWindows = _startWithWindowsCheckBox.Checked,
            DarkMode = false,
            EnableMarkdownButtons = false,
            MaxHistoryItems = decimal.ToInt32(_maxHistoryCountNumeric.Value)
        };
    }
}
