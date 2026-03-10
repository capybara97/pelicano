using Spaste.Models;

namespace Spaste;

/// <summary>
/// 클립보드 히스토리를 보여주는 메인 창이다.
/// 좌측 목록과 우측 미리보기 패널을 동시에 제공해 텍스트/이미지 모두 빠르게 확인할 수 있게 한다.
/// </summary>
internal sealed class MainForm : Form
{
    private const int DesiredPanel1MinSize = 420;
    private const int DesiredPanel2MinSize = 220;
    private static readonly string[] PreviewImageExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff",
        ".webp"
    ];

    private readonly TextBox _searchTextBox;
    private readonly Label _summaryLabel;
    private readonly DataGridView _historyGrid;
    private readonly TextBox _previewTextBox;
    private readonly PictureBox _previewPictureBox;
    private readonly Label _previewCaptionLabel;
    private readonly Label _previewMetaLabel;
    private readonly Button _deleteButton;
    private readonly Button _settingsButton;
    private readonly Button _clearButton;
    private readonly Button _exitButton;
    private readonly Button _toggleResultsButton;
    private readonly SplitContainer _splitContainer;
    private readonly TableLayoutPanel _previewLayout;
    private List<ClipboardItem> _allItems = [];
    private AppSettings _settings;
    private bool _allowClose;
    private bool _initialSplitApplied;
    private bool _suppressSelectionCopy;
    private bool _showAllItems;

    /// <summary>
    /// 텍스트 또는 원본 형식으로 다시 복사할 때 발생한다.
    /// </summary>
    public event EventHandler<ClipboardItem>? CopyPlainRequested;

    /// <summary>
    /// 현재 선택된 항목들을 즉시 다시 복사해 달라고 요청할 때 발생한다.
    /// </summary>
    public event EventHandler? SelectionCopyRequested;

    /// <summary>
    /// 선택 항목 삭제를 요청할 때 발생한다.
    /// </summary>
    public event EventHandler<ClipboardItem>? DeleteRequested;

    /// <summary>
    /// 설정 창 열기를 요청할 때 발생한다.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// 전체 히스토리 삭제를 요청할 때 발생한다.
    /// </summary>
    public event EventHandler? ClearRequested;

    /// <summary>
    /// 앱 종료를 요청할 때 발생한다.
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// 현재 설정을 반영해 메인 창을 초기화한다.
    /// </summary>
    public MainForm(AppSettings settings)
    {
        _settings = settings.Clone();
        Text = "Pelicano";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 600);
        Size = new Size(1100, 720);
        KeyPreview = true;
        Shown += (_, _) => EnsureSplitLayout();
        Resize += (_, _) => EnsureSplitLayout();

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 16, 18, 14)
        };
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 6,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            Text = "Pelicano",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
            Anchor = AnchorStyles.Left
        };

        _searchTextBox = new TextBox
        {
            PlaceholderText = "내용, 파일명, 포맷으로 검색",
            Dock = DockStyle.Fill,
            Margin = new Padding(14, 8, 16, 8),
            Font = new Font("Segoe UI", 15F, FontStyle.Regular)
        };
        _searchTextBox.TextChanged += (_, _) => ApplyFilter();

        _deleteButton = CreateActionButton("선택 삭제", primary: false);
        _deleteButton.Click += (_, _) =>
        {
            if (SelectedItem is not null)
            {
                DeleteRequested?.Invoke(this, SelectedItem);
            }
        };

        _settingsButton = CreateActionButton("설정", primary: false);
        _settingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        _clearButton = CreateActionButton("전체 비우기", primary: false);
        _clearButton.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);

        _exitButton = CreateActionButton("종료", primary: false);
        _exitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _toggleResultsButton = CreateActionButton("더 보기", primary: false);
        _toggleResultsButton.Visible = false;
        _toggleResultsButton.Click += (_, _) =>
        {
            _showAllItems = !_showAllItems;
            ApplyFilter();
        };

        headerLayout.Controls.Add(titleLabel, 0, 0);
        headerLayout.Controls.Add(_searchTextBox, 1, 0);
        headerLayout.Controls.Add(_deleteButton, 2, 0);
        headerLayout.Controls.Add(_settingsButton, 3, 0);
        headerLayout.Controls.Add(_clearButton, 4, 0);
        headerLayout.Controls.Add(_exitButton, 5, 0);

        _splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill
        };
        _splitContainer.Panel2Collapsed = true;

        _historyGrid = CreateHistoryGrid();
        _historyGrid.SelectionChanged += HandleGridSelectionChanged;
        _historyGrid.CellDoubleClick += (_, _) =>
        {
            if (SelectedItem is not null)
            {
                CopyPlainRequested?.Invoke(this, SelectedItem);
            }
        };

        _splitContainer.Panel1.Controls.Add(_historyGrid);

        _previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 12, 14, 12)
        };
        _previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _previewLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
        _previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _previewCaptionLabel = new Label
        {
            AutoSize = true,
            Text = "미리보기 없음",
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold)
        };

        _previewMetaLabel = new Label
        {
            AutoSize = true,
            Text = "클립보드 히스토리",
            Margin = new Padding(0, 4, 0, 0)
        };

        _previewPictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 8, 0, 8)
        };

        _previewTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle
        };

        _previewLayout.Controls.Add(_previewCaptionLabel, 0, 0);
        _previewLayout.Controls.Add(_previewMetaLabel, 0, 1);
        _previewLayout.Controls.Add(_previewPictureBox, 0, 2);
        _previewLayout.Controls.Add(_previewTextBox, 0, 3);
        _splitContainer.Panel2.Controls.Add(_previewLayout);

        _summaryLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "최근 복사 항목",
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            Margin = new Padding(2, 10, 2, 0)
        };

        var footerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerLayout.Controls.Add(_summaryLabel, 0, 0);
        footerLayout.Controls.Add(_toggleResultsButton, 1, 0);

        rootLayout.Controls.Add(headerLayout, 0, 0);
        rootLayout.Controls.Add(_splitContainer, 0, 1);
        rootLayout.Controls.Add(footerLayout, 0, 2);
        Controls.Add(rootLayout);

        ApplySettings(_settings);
        SetPreviewImageVisible(false);
    }

    /// <summary>
    /// 현재 선택된 히스토리 항목을 반환한다.
    /// </summary>
    public ClipboardItem? SelectedItem =>
        _historyGrid.CurrentRow?.Tag as ClipboardItem;

    /// <summary>
    /// 현재 다중 선택된 히스토리 항목 목록을 반환한다.
    /// 현재 셀이 가리키는 항목을 맨 앞에 둬서 복사 우선순위를 맞춘다.
    /// </summary>
    public IReadOnlyList<ClipboardItem> SelectedItems
    {
        get
        {
            var selectedItems = _historyGrid.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(row => row.Tag as ClipboardItem)
                .Where(item => item is not null)
                .Cast<ClipboardItem>()
                .ToList();

            if (SelectedItem is not null && selectedItems.Remove(SelectedItem))
            {
                selectedItems.Insert(0, SelectedItem);
            }

            return selectedItems;
        }
    }

    /// <summary>
    /// 히스토리 전체 목록을 바꾼 뒤 현재 검색 조건으로 다시 그린다.
    /// </summary>
    public void SetItems(IReadOnlyList<ClipboardItem> items)
    {
        _allItems = items.ToList();
        ApplyFilter();
    }

    /// <summary>
    /// 새 설정을 반영하고 테마/버튼 상태를 다시 계산한다.
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        ThemeHelper.Apply(this, _settings.DarkMode);
        ApplyFilter();
    }

    /// <summary>
    /// 종료 직전에는 숨김 처리 대신 실제 종료가 되도록 플래그를 세운다.
    /// </summary>
    public void PrepareForExit()
    {
        _allowClose = true;
    }

    /// <summary>
    /// 검색 입력으로 포커스를 이동해 빠른 검색을 돕는다.
    /// </summary>
    public void FocusSearchBox()
    {
        _searchTextBox.Focus();
        _searchTextBox.SelectAll();
    }

    /// <summary>
    /// 사용자가 닫기 버튼을 눌렀을 때 창을 숨기고 트레이 앱으로 유지한다.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        DisposePreviewImage();
        base.OnFormClosing(e);
    }

    /// <summary>
    /// 초기 레이아웃 이후에만 안전한 분할 비율을 적용한다.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        EnsureSplitLayout();
    }

    /// <summary>
    /// ESC 키를 누르면 창을 숨긴다.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        if (keyData == (Keys.Control | Keys.C) &&
            SelectedItems.Count > 0 &&
            !IsTextInputCopyContext())
        {
            SelectionCopyRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 테이블 필터링과 그리드 재구성을 수행한다.
    /// </summary>
    private void ApplyFilter()
    {
        var keyword = _searchTextBox.Text.Trim();
        var filteredItems = string.IsNullOrWhiteSpace(keyword)
            ? (_showAllItems ? _allItems : _allItems.Take(5).ToList())
            : _allItems.Where(item =>
                item.SearchIndex.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                item.Title.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        PopulateGrid(filteredItems);
        var isSearchMode = !string.IsNullOrWhiteSpace(keyword);
        _toggleResultsButton.Visible = !isSearchMode && _allItems.Count > 5;
        _toggleResultsButton.Text = _showAllItems ? "최근 5개만" : "더 보기";
        _summaryLabel.Text = isSearchMode
            ? $"검색 결과 {filteredItems.Count}개"
            : _showAllItems
                ? $"전체 {_allItems.Count}개"
                : $"최근 5개 / 전체 {_allItems.Count}개";
    }

    /// <summary>
    /// 현재 필터 결과를 DataGridView에 채운다.
    /// </summary>
    private void PopulateGrid(IReadOnlyList<ClipboardItem> items)
    {
        _suppressSelectionCopy = true;
        _historyGrid.Rows.Clear();

        foreach (var item in items)
        {
            var rowIndex = _historyGrid.Rows.Add(
                GetKindLabel(item.ItemKind),
                item.Title);

            _historyGrid.Rows[rowIndex].Tag = item;
        }

        if (_historyGrid.Rows.Count > 0)
        {
            _historyGrid.ClearSelection();
            _historyGrid.Rows[0].Selected = true;
            _historyGrid.CurrentCell = _historyGrid.Rows[0].Cells[0];
            UpdatePreview();
        }
        else
        {
            UpdatePreview();
        }

        _suppressSelectionCopy = false;
    }

    /// <summary>
    /// 행 선택이 바뀌면 미리보기를 갱신하고, 사용자 선택일 때는 즉시 복사를 요청한다.
    /// </summary>
    private void HandleGridSelectionChanged(object? sender, EventArgs e)
    {
        UpdatePreview();

        if (_suppressSelectionCopy || SelectedItems.Count == 0)
        {
            return;
        }

        SelectionCopyRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 선택 항목 변경 시 우측 미리보기 패널을 갱신한다.
    /// </summary>
    private void UpdatePreview()
    {
        var selectedItems = SelectedItems;
        _deleteButton.Enabled = selectedItems.Count > 0;

        if (selectedItems.Count == 0 || SelectedItem is null)
        {
            _previewCaptionLabel.Text = "미리보기 없음";
            _previewMetaLabel.Text = "클립보드 히스토리";
            _previewTextBox.Text = string.Empty;
            DisposePreviewImage();
            SetPreviewImageVisible(false);
            return;
        }

        if (selectedItems.Count > 1)
        {
            _previewCaptionLabel.Text = $"{selectedItems.Count}개 선택됨";
            _previewMetaLabel.Text = "선택 즉시 복사 | Ctrl 또는 Shift로 복수 선택";
            _previewTextBox.Text = string.Join(
                Environment.NewLine,
                selectedItems.Select(item => $"{GetKindLabel(item.ItemKind)} | {item.Title}"));
            DisposePreviewImage();
            SetPreviewImageVisible(false);
            return;
        }

        var item = SelectedItem;
        _previewCaptionLabel.Text = item.Title;
        _previewMetaLabel.Text = BuildPreviewSubtitle(item);

        switch (item.ItemKind)
        {
            case ClipboardItemKind.Text:
                DisposePreviewImage();
                SetPreviewImageVisible(false);
                _previewTextBox.Text = item.PlainText;
                break;

            case ClipboardItemKind.Image:
                _previewTextBox.Text = item.ImagePath ?? string.Empty;
                ShowPreviewImage(item.ImagePath);
                break;

            case ClipboardItemKind.FileDrop:
                _previewTextBox.Text = BuildFileDropPreviewText(item.FileDropPaths);
                ShowPreviewFileDrop(item.FileDropPaths);
                break;
        }
    }

    /// <summary>
    /// 이미지 경로가 유효하면 미리보기 영역에 로드한다.
    /// </summary>
    private void ShowPreviewImage(string? imagePath)
    {
        DisposePreviewImage();

        if (string.IsNullOrWhiteSpace(imagePath) ||
            !File.Exists(imagePath) ||
            !IsPreviewImagePath(imagePath))
        {
            SetPreviewImageVisible(false);
            return;
        }

        try
        {
            using var source = Image.FromFile(imagePath);
            _previewPictureBox.Image = new Bitmap(source);
            SetPreviewImageVisible(true);
        }
        catch
        {
            SetPreviewImageVisible(false);
        }
    }

    /// <summary>
    /// 이전 미리보기 이미지를 해제해 GDI 리소스 누수를 막는다.
    /// </summary>
    private void DisposePreviewImage()
    {
        if (_previewPictureBox.Image is not null)
        {
            var image = _previewPictureBox.Image;
            _previewPictureBox.Image = null;
            image.Dispose();
        }
    }

    /// <summary>
    /// 파일 드롭 항목에서 첫 번째 이미지 파일이 있으면 미리보기를 표시한다.
    /// </summary>
    private void ShowPreviewFileDrop(IReadOnlyList<string> filePaths)
    {
        var previewPath = filePaths.FirstOrDefault(IsPreviewImagePath);
        ShowPreviewImage(previewPath);
    }

    /// <summary>
    /// 현재 미리보기 이미지 영역의 표시 여부와 레이아웃 비중을 조정한다.
    /// </summary>
    private void SetPreviewImageVisible(bool visible)
    {
        _previewPictureBox.Visible = visible;
        _previewLayout.RowStyles[2].SizeType = visible ? SizeType.Percent : SizeType.Absolute;
        _previewLayout.RowStyles[2].Height = visible ? 52F : 0F;
        _previewLayout.RowStyles[3].SizeType = SizeType.Percent;
        _previewLayout.RowStyles[3].Height = visible ? 48F : 100F;
    }

    /// <summary>
    /// 현재 컨테이너 폭에 맞는 안전한 SplitterDistance를 계산한다.
    /// </summary>
    private void EnsureSplitLayout()
    {
        if (_splitContainer.Panel2Collapsed)
        {
            return;
        }

        if (_splitContainer.Width <= 0)
        {
            return;
        }

        var availableWidth = _splitContainer.ClientSize.Width - _splitContainer.SplitterWidth;

        if (availableWidth <= 0)
        {
            return;
        }

        var minimumPanel1 = Math.Min(DesiredPanel1MinSize, Math.Max(0, availableWidth - 1));
        var minimumPanel2 = Math.Min(DesiredPanel2MinSize, Math.Max(0, availableWidth - minimumPanel1));
        var maximumPanel1 = Math.Max(
            minimumPanel1,
            availableWidth - minimumPanel2);

        if (maximumPanel1 < minimumPanel1)
        {
            maximumPanel1 = minimumPanel1;
        }

        var preferred = _initialSplitApplied
            ? _splitContainer.SplitterDistance
            : (int)Math.Round(availableWidth * 0.64);

        _splitContainer.Panel1MinSize = 0;
        _splitContainer.Panel2MinSize = 0;
        _splitContainer.SplitterDistance = Math.Clamp(preferred, minimumPanel1, maximumPanel1);
        _splitContainer.Panel1MinSize = minimumPanel1;
        _splitContainer.Panel2MinSize = minimumPanel2;
        _initialSplitApplied = true;
    }

    /// <summary>
    /// 공통 액션 버튼을 생성한다.
    /// </summary>
    private static Button CreateActionButton(string text, bool primary)
    {
        var button = new Button
        {
            AutoSize = true,
            Text = text,
            Margin = new Padding(8, 4, 0, 4),
            Padding = new Padding(12, 7, 12, 7)
        };

        ThemeHelper.StyleActionButton(button, primary);
        return button;
    }

    /// <summary>
    /// 히스토리 목록 그리드를 생성한다.
    /// </summary>
    private static DataGridView CreateHistoryGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "KindColumn",
            HeaderText = "유형",
            FillWeight = 18
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "TitleColumn",
            HeaderText = "내용",
            FillWeight = 82
        });
        grid.ColumnHeadersVisible = false;

        return grid;
    }

    /// <summary>
    /// 항목 종류를 한국어 레이블로 변환한다.
    /// </summary>
    private static string GetKindLabel(ClipboardItemKind kind)
    {
        return kind switch
        {
            ClipboardItemKind.Text => "텍스트",
            ClipboardItemKind.Image => "이미지",
            ClipboardItemKind.FileDrop => "파일",
            _ => "기타"
        };
    }

    /// <summary>
    /// 파일 드롭 미리보기 텍스트를 사람이 읽기 좋은 형식으로 구성한다.
    /// </summary>
    private static string BuildFileDropPreviewText(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>
        {
            $"총 {filePaths.Count}개 항목",
            string.Empty
        };
        lines.AddRange(filePaths.Select(DescribeClipboardPath));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 파일 경로가 이미지 미리보기에 적합한지 판정한다.
    /// </summary>
    private static bool IsPreviewImagePath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               File.Exists(path) &&
               PreviewImageExtensions.Contains(
                   Path.GetExtension(path),
                   StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 파일 또는 폴더 경로 앞에 간단한 유형 표기를 붙인다.
    /// </summary>
    private static string DescribeClipboardPath(string path)
    {
        var typeLabel = Directory.Exists(path) ? "[폴더]" : "[파일]";
        return $"{typeLabel} {path}";
    }

    /// <summary>
    /// 검색창/미리보기 텍스트박스에 포커스가 있으면 기본 Ctrl+C 동작을 우선한다.
    /// </summary>
    private bool IsTextInputCopyContext()
    {
        return _searchTextBox.ContainsFocus || _previewTextBox.ContainsFocus;
    }

    /// <summary>
    /// 우측 미리보기의 보조 설명 문자열을 만든다.
    /// </summary>
    private static string BuildPreviewSubtitle(ClipboardItem item)
    {
        return item.ItemKind switch
        {
            ClipboardItemKind.Text => "텍스트",
            ClipboardItemKind.Image => "이미지",
            ClipboardItemKind.FileDrop => item.FileDropPaths.Count > 1
                ? $"파일 {item.FileDropPaths.Count}개"
                : "파일",
            _ => item.SourceFormat
        };
    }
}
