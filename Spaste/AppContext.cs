using System.Drawing.Imaging;
using Spaste.Models;
using Spaste.Services;

namespace Spaste;

/// <summary>
/// Pelicano의 전체 런타임 상태를 관리하는 ApplicationContext다.
/// 트레이 아이콘, 폼, 클립보드 감시, 히스토리 저장을 한곳에서 묶어 운영한다.
/// </summary>
internal sealed class SpasteApplicationContext : ApplicationContext
{
    private readonly Logger _logger;
    private readonly TextStripper _textStripper;
    private readonly HistoryManager _historyManager;
    private readonly ClipboardListener _clipboardListener;
    private readonly HotKeyManager _hotKeyManager;
    private readonly TrayManager _trayManager;
    private readonly MainForm _mainForm;
    private readonly Icon _appIcon;
    private readonly Dictionary<string, DateTimeOffset> _suppressedClipboardHashes =
        new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;
    private List<ClipboardItem> _historyItems;
    private bool _isShuttingDown;

    /// <summary>
    /// 앱 시작과 동시에 필요한 서비스와 UI를 초기화한다.
    /// </summary>
    public SpasteApplicationContext()
    {
        AppPaths.EnsureDirectories();

        var bootstrapLogger = new Logger(AppPaths.LogsRoot);
        _settings = SettingsStore.Load(AppPaths.SettingsPath, bootstrapLogger);
        _logger = new Logger(AppPaths.LogsRoot, () => _settings.EnableAuditLogging);
        _textStripper = new TextStripper();
        _historyManager = new HistoryManager(AppPaths.DatabasePath, _logger);
        _historyItems = _historyManager.LoadAll(_settings.MaxHistoryItems).ToList();
        _appIcon = IconHelper.LoadApplicationIcon();

        _mainForm = new MainForm(_settings);
        _mainForm.SetItems(_historyItems);
        _mainForm.SelectionCopyRequested += (_, _) => CopySelection(_mainForm.SelectedItems);
        _mainForm.DeleteRequested += (_, item) => DeleteItem(item);
        _mainForm.SelectionDeleteRequested += (_, _) => DeleteSelection(_mainForm.SelectedItems);
        _mainForm.SettingsRequested += (_, _) => ShowSettings();
        _mainForm.ClearRequested += (_, _) => ClearHistory();
        _mainForm.ExitRequested += (_, _) => ExitThread();

        _trayManager = new TrayManager(
            _appIcon,
            _settings,
            ShowMainWindow,
            TogglePlainTextOnly,
            ToggleFileCapture,
            ShowSettings,
            ClearHistory,
            ExitThread);

        _clipboardListener = new ClipboardListener();
        _clipboardListener.ClipboardChanged += (_, _) => ProcessClipboardUpdate();

        _hotKeyManager = new HotKeyManager(NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Keys.V);
        _hotKeyManager.HotKeyPressed += (_, _) => ShowMainWindow();

        if (!_hotKeyManager.IsRegistered)
        {
            _logger.Info("Ctrl+Shift+V 전역 단축키 등록에 실패했다. 다른 프로그램과 충돌할 수 있다.");
        }

        StartupManager.Apply(_settings.StartWithWindows, _logger);
        _logger.Info("Pelicano 애플리케이션이 시작되었다.");
    }

    /// <summary>
    /// 히스토리 창을 표시하고 최상단으로 가져온다.
    /// </summary>
    private void ShowMainWindow()
    {
        if (_mainForm.Visible)
        {
            _mainForm.FocusSearchBox();
            _mainForm.Activate();
        }
        else
        {
            _mainForm.Show();
            _mainForm.FocusSearchBox();
        }

        _mainForm.WindowState = FormWindowState.Normal;
        NativeMethods.SetForegroundWindow(_mainForm.Handle);
    }

    /// <summary>
    /// 클립보드 변경이 감지되면 텍스트/이미지/파일 복사를 히스토리로 저장한다.
    /// </summary>
    private void ProcessClipboardUpdate()
    {
        if (_isShuttingDown)
        {
            return;
        }

        try
        {
            PruneSuppressedClipboardHashes();
            var capturedItem = CaptureClipboardItem();

            if (capturedItem is null || ShouldSuppressCapturedItem(capturedItem) || IsDuplicate(capturedItem))
            {
                return;
            }

            _historyItems.Insert(0, capturedItem);
            TrimHistoryToLimit();
            PersistHistory();
            _mainForm.SetItems(_historyItems);
            _logger.Audit("CAPTURE", capturedItem.ItemKind.ToString());
        }
        catch (Exception exception)
        {
            _logger.Error("클립보드 캡처 처리 중 오류가 발생했다.", exception);
        }
    }

    /// <summary>
    /// 현재 클립보드 상태를 검사해 히스토리 항목으로 변환한다.
    /// </summary>
    private ClipboardItem? CaptureClipboardItem()
    {
        if (_settings.CaptureImages && ClipboardService.TryGetFileDropList(out var filePaths))
        {
            return CreateFileDropItem(filePaths);
        }

        if (ClipboardService.TryGetText(out var clipboardText, out var hasRichFormatting))
        {
            var plainText = _textStripper.NormalizePlainText(clipboardText);

            if (string.IsNullOrWhiteSpace(plainText))
            {
                return null;
            }

            if (_settings.EnforcePlainTextOnly && hasRichFormatting)
            {
                SetClipboardTextInternal(
                    plainText,
                    auditLabel: "TEXT_STRIPPED",
                    contentHash: _textStripper.ComputeHash(plainText));
            }

            return CreateTextItem(plainText, hasRichFormatting ? "UnicodeText+Rich" : "UnicodeText");
        }

        if (_settings.CaptureImages && ClipboardService.TryGetImage(out var bitmap))
        {
            using (bitmap)
            {
                return CreateImageItem(bitmap);
            }
        }

        return null;
    }

    /// <summary>
    /// 일반 텍스트 히스토리 항목을 생성한다.
    /// </summary>
    private ClipboardItem CreateTextItem(string plainText, string sourceFormat)
    {
        var displayText = _textStripper.BuildDisplayText(plainText);

        return new ClipboardItem
        {
            ItemKind = ClipboardItemKind.Text,
            Title = _textStripper.BuildTitle(plainText),
            PlainText = plainText,
            MarkdownText = string.Empty,
            SourceFormat = sourceFormat,
            ContentHash = _textStripper.ComputeHash(plainText),
            CapturedAt = DateTimeOffset.Now,
            SearchIndex = _textStripper.BuildSearchIndex([displayText, sourceFormat])
        };
    }

    /// <summary>
    /// 탐색기 등에서 복사한 파일/폴더 항목을 생성한다.
    /// </summary>
    private ClipboardItem CreateFileDropItem(List<string> filePaths)
    {
        var displayNames = filePaths
            .Select(GetClipboardPathDisplayName)
            .ToList();
        var title = displayNames.Count == 1
            ? displayNames[0]
            : $"{displayNames[0]} 외 {displayNames.Count - 1}개";

        return new ClipboardItem
        {
            ItemKind = ClipboardItemKind.FileDrop,
            Title = title,
            FileDropPaths = filePaths,
            SourceFormat = "WindowsFileDropList",
            ContentHash = _textStripper.ComputeHash(
                string.Join('|', filePaths.Select(path => path.ToLowerInvariant()))),
            CapturedAt = DateTimeOffset.Now,
            SearchIndex = _textStripper.BuildSearchIndex(
                filePaths.SelectMany(path =>
                    new[]
                    {
                        path,
                        GetClipboardPathDisplayName(path),
                        Path.GetExtension(path),
                        File.Exists(path) ? "file" : "folder"
                    }).Append(title))
        };
    }

    /// <summary>
    /// 비트맵 이미지를 PNG로 저장한 뒤 히스토리 항목을 생성한다.
    /// </summary>
    private ClipboardItem CreateImageItem(Bitmap bitmap)
    {
        var imageId = Guid.NewGuid();
        var imagePath = Path.Combine(AppPaths.ImagesRoot, $"{imageId:N}.png");

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        var imageBytes = memoryStream.ToArray();
        File.WriteAllBytes(imagePath, imageBytes);

        return new ClipboardItem
        {
            Id = imageId,
            ItemKind = ClipboardItemKind.Image,
            Title = $"이미지 {bitmap.Width}x{bitmap.Height}",
            ImagePath = imagePath,
            SourceFormat = "Bitmap",
            ContentHash = _textStripper.ComputeHash(imageBytes),
            CapturedAt = DateTimeOffset.Now,
            SearchIndex = _textStripper.BuildSearchIndex([$"이미지 {bitmap.Width}x{bitmap.Height}", "bitmap"])
        };
    }

    /// <summary>
    /// 같은 내용이 바로 직전에 저장돼 있으면 중복 저장을 막는다.
    /// </summary>
    private bool IsDuplicate(ClipboardItem candidate)
    {
        var latest = _historyItems.FirstOrDefault();
        return latest is not null &&
               latest.ItemKind == candidate.ItemKind &&
               latest.ContentHash == candidate.ContentHash;
    }

    /// <summary>
    /// 선택 항목을 다시 일반 클립보드 형식으로 복사한다.
    /// </summary>
    private void CopyItem(ClipboardItem item)
    {
        try
        {
            switch (item.ItemKind)
            {
                case ClipboardItemKind.Text:
                    SetClipboardTextInternal(item.PlainText, "COPY_TEXT", item.ContentHash);
                    break;

                case ClipboardItemKind.Image when !string.IsNullOrWhiteSpace(item.ImagePath):
                    if (!AppPaths.IsManagedImagePath(item.ImagePath))
                    {
                        _logger.Error(
                            "앱 관리 영역 밖의 이미지 경로가 히스토리 항목에 포함되어 복사를 중단했다.",
                            new InvalidOperationException(item.ImagePath));
                        break;
                    }

                    SetClipboardImageInternal(item.ImagePath!, "COPY_IMAGE", item.ContentHash);
                    break;

                case ClipboardItemKind.FileDrop when item.FileDropPaths.Count > 0:
                    SetClipboardFileDropInternal(item.FileDropPaths, "COPY_FILE", item.ContentHash);
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.Error("선택 항목을 다시 클립보드로 복사하는 중 오류가 발생했다.", exception);
        }
    }

    /// <summary>
    /// 다중 선택 복사를 처리한다.
    /// 텍스트는 합쳐서, 파일은 하나의 드롭 리스트로 묶고, 혼합 선택은 현재 항목 하나를 우선 복사한다.
    /// </summary>
    private void CopySelection(IReadOnlyList<ClipboardItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (items.Count == 1)
        {
            CopyItem(items[0]);
            return;
        }

        try
        {
            if (items.All(item => item.ItemKind == ClipboardItemKind.Text))
            {
                var combinedText = string.Join(
                    $"{Environment.NewLine}{Environment.NewLine}",
                    items.Select(item => item.PlainText).Where(text => !string.IsNullOrWhiteSpace(text)));

                if (!string.IsNullOrWhiteSpace(combinedText))
                {
                    SetClipboardTextInternal(
                        combinedText,
                        "COPY_TEXT_MULTI",
                        _textStripper.ComputeHash(combinedText));
                }

                return;
            }

            if (items.All(item => item.ItemKind == ClipboardItemKind.FileDrop))
            {
                var mergedFilePaths = items
                    .SelectMany(item => item.FileDropPaths)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (mergedFilePaths.Count > 0)
                {
                    SetClipboardFileDropInternal(
                        mergedFilePaths,
                        "COPY_FILE_MULTI",
                        _textStripper.ComputeHash(
                            string.Join('|', mergedFilePaths.Select(path => path.ToLowerInvariant()))));
                }

                return;
            }

            CopyItem(items[0]);
        }
        catch (Exception exception)
        {
            _logger.Error("다중 선택 항목을 클립보드로 복사하는 중 오류가 발생했다.", exception);
        }
    }

    /// <summary>
    /// 내부 로직에서 텍스트를 다시 클립보드에 쓸 때 이벤트 재진입을 억제한다.
    /// </summary>
    private void SetClipboardTextInternal(string text, string auditLabel, string? contentHash = null)
    {
        RegisterSuppressedHash(contentHash ?? _textStripper.ComputeHash(text));
        ClipboardService.SetText(text);
        _logger.Audit(auditLabel, "Text");
    }

    /// <summary>
    /// 내부 로직에서 저장된 이미지를 다시 클립보드에 쓸 때 이벤트 재진입을 억제한다.
    /// </summary>
    private void SetClipboardImageInternal(string imagePath, string auditLabel, string? contentHash = null)
    {
        if (!string.IsNullOrWhiteSpace(contentHash))
        {
            RegisterSuppressedHash(contentHash);
        }

        ClipboardService.SetImageFromFile(imagePath);
        _logger.Audit(auditLabel, "Image");
    }

    /// <summary>
    /// 내부 로직에서 파일 드롭 리스트를 다시 클립보드에 쓸 때 이벤트 재진입을 억제한다.
    /// </summary>
    private void SetClipboardFileDropInternal(
        IEnumerable<string> filePaths,
        string auditLabel,
        string? contentHash = null)
    {
        var filePathList = filePaths.ToList();
        RegisterSuppressedHash(
            contentHash ?? _textStripper.ComputeHash(
                string.Join('|', filePathList.Select(path => path.ToLowerInvariant()))));
        ClipboardService.SetFileDropList(filePathList);
        _logger.Audit(auditLabel, $"{filePathList.Count} files");
    }

    /// <summary>
    /// 선택 항목을 삭제하고 이미지 파일이 더 이상 필요 없으면 함께 정리한다.
    /// </summary>
    private void DeleteItem(ClipboardItem item)
    {
        _historyItems = _historyItems.Where(entry => entry.Id != item.Id).ToList();
        DeleteUnusedImageFile(item);
        PersistHistory();
        _mainForm.SetItems(_historyItems);
        _logger.Audit("DELETE", item.ItemKind.ToString());
    }

    /// <summary>
    /// 여러 선택 항목을 한 번에 삭제한다.
    /// </summary>
    private void DeleteSelection(IReadOnlyList<ClipboardItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        foreach (var item in items)
        {
            _historyItems = _historyItems.Where(entry => entry.Id != item.Id).ToList();
            DeleteUnusedImageFile(item);
        }

        PersistHistory();
        _mainForm.SetItems(_historyItems);
        _logger.Audit("DELETE_MULTI", $"{items.Count}개 항목 삭제");
    }

    /// <summary>
    /// 전체 히스토리를 삭제한다.
    /// </summary>
    private void ClearHistory()
    {
        if (_historyItems.Count == 0)
        {
            return;
        }

        var result = MessageBox.Show(
            _mainForm,
            "저장된 히스토리를 모두 삭제할까요?",
            "Pelicano",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        foreach (var item in _historyItems.ToList())
        {
            DeleteUnusedImageFile(item, force: true);
        }

        _historyItems.Clear();
        PersistHistory();
        _mainForm.SetItems(_historyItems);
        _logger.Audit("CLEAR", "히스토리 전체 삭제");
    }

    /// <summary>
    /// 설정 창을 띄우고 저장된 변경 사항을 런타임에 반영한다.
    /// </summary>
    private void ShowSettings()
    {
        using var settingsForm = new SettingsForm(_settings);

        if (settingsForm.ShowDialog(_mainForm) != DialogResult.OK)
        {
            return;
        }

        _settings = settingsForm.EditedSettings.Clone();
        SettingsStore.Save(AppPaths.SettingsPath, _settings, _logger);
        StartupManager.Apply(_settings.StartWithWindows, _logger);
        _trayManager.RefreshState(_settings);
        _mainForm.ApplySettings(_settings);
        TrimHistoryToLimit();
        PersistHistory();
        _mainForm.SetItems(_historyItems);
        _logger.Audit("SETTINGS", "사용자 설정이 변경되었다.");
    }

    /// <summary>
    /// 트레이 메뉴에서 Plain Text Only 모드를 즉시 토글한다.
    /// </summary>
    private void TogglePlainTextOnly()
    {
        _settings.EnforcePlainTextOnly = !_settings.EnforcePlainTextOnly;
        SettingsStore.Save(AppPaths.SettingsPath, _settings, _logger);
        _trayManager.RefreshState(_settings);
        _mainForm.ApplySettings(_settings);
    }

    /// <summary>
    /// 트레이 메뉴에서 파일/이미지 캡처 모드를 즉시 토글한다.
    /// </summary>
    private void ToggleFileCapture()
    {
        _settings.CaptureImages = !_settings.CaptureImages;
        SettingsStore.Save(AppPaths.SettingsPath, _settings, _logger);
        _trayManager.RefreshState(_settings);
        _mainForm.ApplySettings(_settings);
    }

    /// <summary>
    /// 현재 히스토리를 DB에 반영한다.
    /// </summary>
    private void PersistHistory()
    {
        _historyManager.ReplaceAll(_historyItems);
    }

    /// <summary>
    /// 설정된 최대 개수를 초과한 오래된 항목을 제거한다.
    /// </summary>
    private void TrimHistoryToLimit()
    {
        while (_historyItems.Count > _settings.MaxHistoryItems)
        {
            var removed = _historyItems[^1];
            _historyItems.RemoveAt(_historyItems.Count - 1);
            DeleteUnusedImageFile(removed);
        }
    }

    /// <summary>
    /// 이미지 항목이 더 이상 히스토리에서 참조되지 않으면 파일도 삭제한다.
    /// </summary>
    private void DeleteUnusedImageFile(ClipboardItem item, bool force = false)
    {
        if (item.ItemKind != ClipboardItemKind.Image || string.IsNullOrWhiteSpace(item.ImagePath))
        {
            return;
        }

        var isStillReferenced = _historyItems.Any(entry =>
            entry.Id != item.Id &&
            entry.ItemKind == ClipboardItemKind.Image &&
            string.Equals(entry.ImagePath, item.ImagePath, StringComparison.OrdinalIgnoreCase));

        if (!force && isStillReferenced)
        {
            return;
        }

        if (File.Exists(item.ImagePath))
        {
            if (!AppPaths.IsManagedImagePath(item.ImagePath))
            {
                _logger.Error(
                    "앱 관리 영역 밖 파일 삭제 시도를 차단했다.",
                    new InvalidOperationException(item.ImagePath));
                return;
            }

            File.Delete(item.ImagePath);
        }
    }

    /// <summary>
    /// 종료 시 리소스를 순서대로 정리한다.
    /// </summary>
    protected override void ExitThreadCore()
    {
        _isShuttingDown = true;
        _mainForm.PrepareForExit();
        _mainForm.Close();
        _clipboardListener.Dispose();
        _hotKeyManager.Dispose();
        _trayManager.Dispose();
        _appIcon.Dispose();
        _logger.Info("Pelicano 애플리케이션이 종료되었다.");
        base.ExitThreadCore();
    }

    /// <summary>
    /// 파일 또는 폴더 경로에서 UI용 표시 이름을 안전하게 추출한다.
    /// </summary>
    private static string GetClipboardPathDisplayName(string path)
    {
        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }

    /// <summary>
    /// 내부 복사로 인해 다시 들어오는 동일 콘텐츠는 히스토리에 재삽입하지 않는다.
    /// </summary>
    private bool ShouldSuppressCapturedItem(ClipboardItem item)
    {
        if (!_suppressedClipboardHashes.TryGetValue(item.ContentHash, out var expiresAt))
        {
            return false;
        }

        return expiresAt > DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 내부 복사 예정인 콘텐츠 해시를 등록해 뒤따르는 클립보드 업데이트를 무시한다.
    /// </summary>
    private void RegisterSuppressedHash(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return;
        }

        _suppressedClipboardHashes[contentHash] = DateTimeOffset.UtcNow.AddSeconds(2);
    }

    /// <summary>
    /// 만료된 억제 해시를 정리해 메모리를 작게 유지한다.
    /// </summary>
    private void PruneSuppressedClipboardHashes()
    {
        if (_suppressedClipboardHashes.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var expiredHashes = _suppressedClipboardHashes
            .Where(entry => entry.Value <= now)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var expiredHash in expiredHashes)
        {
            _suppressedClipboardHashes.Remove(expiredHash);
        }
    }
}
