using Pelicano.Models;
using Pelicano.Services;

namespace Pelicano;

/// <summary>
/// WinUI 셸과 분리된 Pelicano 런타임 호스트다.
/// 트레이, 전역 단축키, 클립보드 감시, 히스토리 저장을 통합 관리한다.
/// </summary>
internal sealed class PelicanoHost : IDisposable
{
    private const int ClipboardCaptureRetryCount = 8;
    private const int ClipboardCaptureRetryDelayMs = 60;
    private readonly record struct ClipboardCaptureResult(
        ClipboardItem Item,
        string? PlainTextRewrite,
        string SourceFormat);
    private readonly Logger _logger;
    private readonly TextStripper _textStripper;
    private readonly HistoryManager _historyManager;
    private readonly UpdateService _updateService;
    private readonly Dictionary<string, DateTimeOffset> _suppressedClipboardHashes =
        new(StringComparer.OrdinalIgnoreCase);
    private WindowMessageMonitor? _windowMessageMonitor;
    private AppSettings _settings;
    private List<ClipboardItem> _historyItems;
    private bool _isShuttingDown;
    private int _updateCheckInProgress;

    public PelicanoHost()
    {
        AppPaths.EnsureDirectories();

        var bootstrapLogger = new Logger(AppPaths.LogsRoot);
        _settings = SettingsStore.Load(AppPaths.SettingsPath, bootstrapLogger);
        _logger = new Logger(AppPaths.LogsRoot, () => _settings.EnableAuditLogging);
        _updateService = new UpdateService(_logger);
        _textStripper = new TextStripper();
        _historyManager = new HistoryManager(AppPaths.DatabasePath, _logger);
        _historyItems = _historyManager.LoadAll(_settings.MaxHistoryItems).ToList();
        if (NormalizeLoadedTextTitles())
        {
            PersistHistory();
        }

        StartupManager.Apply(_settings.StartWithWindows, _logger);
        _logger.Info("Pelicano 호스트가 시작되었다.");
    }

    /// <summary>
    /// 히스토리 내용이 바뀌었을 때 발생한다.
    /// </summary>
    public event EventHandler? HistoryChanged;

    /// <summary>
    /// 설정이 바뀌었을 때 발생한다.
    /// </summary>
    public event EventHandler? SettingsChanged;

    /// <summary>
    /// 메인 창 표시를 요청할 때 발생한다.
    /// </summary>
    public event EventHandler? ShowWindowRequested;

    /// <summary>
    /// 설정 창 표시를 요청할 때 발생한다.
    /// </summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// 전체 삭제 확인 UI를 요청할 때 발생한다.
    /// </summary>
    public event EventHandler? ClearHistoryRequested;

    /// <summary>
    /// 앱 종료를 요청할 때 발생한다.
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// 초기 상태에서 사용자에게 보여줄 경고 문구다.
    /// </summary>
    public string? StartupWarningMessage { get; private set; }

    /// <summary>
    /// 현재 설정의 복사본을 반환한다.
    /// </summary>
    public AppSettings Settings => _settings.Clone();

    /// <summary>
    /// 현재 히스토리 목록의 복사본을 반환한다.
    /// </summary>
    public IReadOnlyList<ClipboardItem> HistoryItems => _historyItems.ToList();

    /// <summary>
    /// 현재 창 핸들에 백그라운드 메시지 수신을 연결했는지 나타낸다.
    /// </summary>
    public bool IsBackgroundModeAvailable => _windowMessageMonitor?.IsHotKeyRegistered ?? false;

    /// <summary>
    /// 시작 단계 비치명 경고를 누적해 초기 UI에 노출한다.
    /// </summary>
    public void AddStartupWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        StartupWarningMessage = string.IsNullOrWhiteSpace(StartupWarningMessage)
            ? message.Trim()
            : $"{StartupWarningMessage}{Environment.NewLine}{message.Trim()}";
    }

    /// <summary>
    /// 메인 WinUI 창 핸들을 연결해 클립보드 감시와 전역 단축키를 활성화한다.
    /// </summary>
    public void AttachWindow(IntPtr windowHandle)
    {
        if (_windowMessageMonitor is not null)
        {
            return;
        }

        _windowMessageMonitor = new WindowMessageMonitor(windowHandle);
        _windowMessageMonitor.ClipboardUpdated += (_, _) => ProcessClipboardUpdate();
        _windowMessageMonitor.HotKeyPressed += (_, _) => RaiseShowWindowRequested();

        if (!_windowMessageMonitor.IsHotKeyRegistered)
        {
            AddStartupWarning("Ctrl+Shift+V 전역 단축키 등록에 실패했습니다. 다른 앱과 충돌할 수 있습니다.");
            _logger.Info("Ctrl+Shift+V 전역 단축키 등록에 실패했다. 다른 프로그램과 충돌할 수 있다.");
        }
    }

    /// <summary>
    /// 최신 버전 확인을 수행한다.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        if (Interlocked.Exchange(ref _updateCheckInProgress, 1) == 1)
        {
            return new UpdateCheckResult
            {
                State = UpdateCheckState.Failed,
                CurrentVersion = AppVersionInfo.CurrentVersion,
                Message = "이미 업데이트를 확인하고 있습니다."
            };
        }

        try
        {
            return await _updateService.CheckForUpdatesAsync(CancellationToken.None);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckInProgress, 0);
        }
    }

    /// <summary>
    /// 설치 파일을 다운로드하고 해시를 검증한다.
    /// </summary>
    public Task<DownloadedUpdatePackage> DownloadInstallerAsync(UpdateManifest manifest)
    {
        return _updateService.DownloadInstallerAsync(manifest, progress: null, CancellationToken.None);
    }

    /// <summary>
    /// 설치 파일 다운로드 중 진행률을 UI에 보고한다.
    /// </summary>
    public Task<DownloadedUpdatePackage> DownloadInstallerAsync(
        UpdateManifest manifest,
        IProgress<UpdateProgressInfo> progress)
    {
        return _updateService.DownloadInstallerAsync(manifest, progress, CancellationToken.None);
    }

    /// <summary>
    /// 설치 파일 실행을 예약하고 앱 종료를 요청한다.
    /// </summary>
    public void LaunchInstallerAndRequestExit(string installerPath)
    {
        UpdateInstallerLauncher.LaunchAfterExit(installerPath, Environment.ProcessId);
        RaiseExitRequested();
    }

    /// <summary>
    /// 업데이트로 앱을 재시작하기 전 설정과 히스토리를 한 번 더 디스크에 기록한다.
    /// </summary>
    public void PrepareForUpdateInstall()
    {
        SettingsStore.Save(AppPaths.SettingsPath, _settings, _logger);
        PersistHistory();
        _logger.Info("업데이트 설치 전 현재 히스토리와 설정을 저장했다.");
    }

    /// <summary>
    /// 설정을 저장하고 런타임에 즉시 반영한다.
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        SettingsStore.Save(AppPaths.SettingsPath, _settings, _logger);
        StartupManager.Apply(_settings.StartWithWindows, _logger);
        TrimHistoryToLimit();
        PersistHistory();
        _logger.Audit("SETTINGS", "사용자 설정이 변경되었다.");
        RaiseSettingsChanged();
        RaiseHistoryChanged();
    }

    /// <summary>
    /// Plain Text Only 빠른 토글을 적용한다.
    /// </summary>
    public void TogglePlainTextOnly()
    {
        var updatedSettings = _settings.Clone();
        updatedSettings.EnforcePlainTextOnly = !updatedSettings.EnforcePlainTextOnly;
        ApplySettings(updatedSettings);
    }

    /// <summary>
    /// 파일/이미지 캡처를 동시에 켜거나 끈다.
    /// </summary>
    public void ToggleCaptureAssets()
    {
        var updatedSettings = _settings.Clone();
        var nextValue = !(updatedSettings.CaptureFileDrops || updatedSettings.CaptureImages);
        updatedSettings.CaptureFileDrops = nextValue;
        updatedSettings.CaptureImages = nextValue;
        ApplySettings(updatedSettings);
    }

    /// <summary>
    /// 선택 항목을 다시 클립보드에 복사한다.
    /// </summary>
    public void CopySelection(IReadOnlyList<ClipboardItem> items)
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
    /// 단일 항목을 삭제한다.
    /// </summary>
    public void DeleteItem(ClipboardItem item)
    {
        _historyItems = _historyItems.Where(entry => entry.Id != item.Id).ToList();
        DeleteUnusedImageFile(item);
        PersistHistory();
        _logger.Audit("DELETE", item.ItemKind.ToString());
        RaiseHistoryChanged();
    }

    /// <summary>
    /// 여러 항목을 삭제한다.
    /// </summary>
    public void DeleteSelection(IReadOnlyList<ClipboardItem> items)
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
        _logger.Audit("DELETE_MULTI", $"{items.Count}개 항목 삭제");
        RaiseHistoryChanged();
    }

    /// <summary>
    /// 전체 히스토리를 비운다.
    /// </summary>
    public void ClearHistoryConfirmed()
    {
        if (_historyItems.Count == 0)
        {
            return;
        }

        foreach (var item in _historyItems.ToList())
        {
            DeleteUnusedImageFile(item, force: true);
        }

        _historyItems.Clear();
        PersistHistory();
        _logger.Audit("CLEAR", "히스토리 전체 삭제");
        RaiseHistoryChanged();
    }

    public void Dispose()
    {
        _isShuttingDown = true;
        _windowMessageMonitor?.Dispose();
        _logger.Info("Pelicano 호스트가 종료되었다.");
        GC.SuppressFinalize(this);
    }

    private void RaiseHistoryChanged()
    {
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseSettingsChanged()
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseShowWindowRequested()
    {
        ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseSettingsRequested()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseClearHistoryRequested()
    {
        ClearHistoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseExitRequested()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ProcessClipboardUpdate()
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (_settings.CapturePaused)
        {
            return;
        }

        try
        {
            PruneSuppressedClipboardHashes();
            var captureResult = CaptureClipboardItem();

            if (captureResult is null)
            {
                return;
            }

            var capturedItem = captureResult.Value.Item;
            var shouldPersist = !ShouldSuppressCapturedItem(capturedItem) && !IsDuplicate(capturedItem);

            if (shouldPersist)
            {
                _historyItems.Insert(0, capturedItem);
                TrimHistoryToLimit();
                PersistHistory();
                _logger.Audit("CAPTURE", captureResult.Value.SourceFormat);
                RaiseHistoryChanged();
            }

            ApplyPendingPlainTextRewrite(captureResult.Value);
        }
        catch (Exception exception)
        {
            _logger.Error("클립보드 캡처 처리 중 오류가 발생했다.", exception);
        }
    }

    private ClipboardCaptureResult? CaptureClipboardItem()
    {
        for (var attempt = 0; attempt < ClipboardCaptureRetryCount; attempt += 1)
        {
            var item = CaptureClipboardItemOnce();
            if (item is not null)
            {
                return item;
            }

            if (attempt < ClipboardCaptureRetryCount - 1)
            {
                Thread.Sleep(ClipboardCaptureRetryDelayMs);
            }
        }

        return null;
    }

    private ClipboardCaptureResult? CaptureClipboardItemOnce()
    {
        if (_settings.CaptureFileDrops && ClipboardService.TryGetFileDropList(out var filePaths))
        {
            return new ClipboardCaptureResult(
                CreateFileDropItem(filePaths),
                null,
                ClipboardItemKind.FileDrop.ToString());
        }

        // Browsers and chat apps often publish bitmap + text/html together.
        // Prefer the richer bitmap payload so images stay in the image/file lane.
        if (_settings.CaptureImages && ClipboardService.TryGetImage(out var image))
        {
            return new ClipboardCaptureResult(
                CreateImageItem(image),
                null,
                ClipboardItemKind.Image.ToString());
        }

        if (ClipboardService.TryGetText(out var clipboardText, out var hasRichFormatting))
        {
            var plainText = _textStripper.NormalizePlainText(clipboardText);

            if (string.IsNullOrWhiteSpace(plainText))
            {
                return null;
            }

            return new ClipboardCaptureResult(
                CreateTextItem(plainText, hasRichFormatting ? "UnicodeText+Rich" : "UnicodeText"),
                _settings.EnforcePlainTextOnly && hasRichFormatting ? plainText : null,
                ClipboardItemKind.Text.ToString());
        }

        return null;
    }

    private ClipboardItem CreateTextItem(string plainText, string sourceFormat)
    {
        var displayText = _textStripper.BuildDisplayText(plainText);

        return new ClipboardItem
        {
            ItemKind = ClipboardItemKind.Text,
            Title = _textStripper.BuildTitle(plainText),
            PlainText = plainText,
            NormalizedText = plainText,
            SourceFormat = sourceFormat,
            ContentHash = _textStripper.ComputeHash(plainText),
            CapturedAt = DateTimeOffset.Now,
            SearchIndex = _textStripper.BuildSearchIndex([displayText, sourceFormat])
        };
    }

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

    private ClipboardItem CreateImageItem(ClipboardImageData image)
    {
        var imageId = Guid.NewGuid();
        var imagePath = Path.Combine(AppPaths.ImagesRoot, $"{imageId:N}.png");
        var imageBytes = image.Bytes;
        File.WriteAllBytes(imagePath, imageBytes);

        return new ClipboardItem
        {
            Id = imageId,
            ItemKind = ClipboardItemKind.Image,
            Title = $"이미지 {image.Width}x{image.Height}",
            ImagePath = imagePath,
            ImageBytes = imageBytes,
            SourceFormat = "Bitmap",
            ContentHash = _textStripper.ComputeHash(imageBytes),
            CapturedAt = DateTimeOffset.Now,
            SearchIndex = _textStripper.BuildSearchIndex([$"이미지 {image.Width}x{image.Height}", "bitmap"])
        };
    }

    private bool IsDuplicate(ClipboardItem candidate)
    {
        var latest = _historyItems.FirstOrDefault();
        return latest is not null &&
               latest.ItemKind == candidate.ItemKind &&
               latest.ContentHash == candidate.ContentHash;
    }

    private void ApplyPendingPlainTextRewrite(ClipboardCaptureResult captureResult)
    {
        if (string.IsNullOrWhiteSpace(captureResult.PlainTextRewrite))
        {
            return;
        }

        try
        {
            SetClipboardTextInternal(
                captureResult.PlainTextRewrite,
                auditLabel: "TEXT_STRIPPED",
                contentHash: captureResult.Item.ContentHash);
        }
        catch (Exception exception)
        {
            _logger.Error("서식 제거 텍스트를 클립보드에 다시 쓰는 중 오류가 발생했다.", exception);
        }
    }

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

    private void SetClipboardTextInternal(string text, string auditLabel, string? contentHash = null)
    {
        RegisterSuppressedHash(contentHash ?? _textStripper.ComputeHash(text));
        ClipboardService.SetText(text);
        _logger.Audit(auditLabel, "Text");
    }

    private void SetClipboardImageInternal(string imagePath, string auditLabel, string? contentHash = null)
    {
        if (!string.IsNullOrWhiteSpace(contentHash))
        {
            RegisterSuppressedHash(contentHash);
        }

        ClipboardService.SetImageFromFile(imagePath);
        _logger.Audit(auditLabel, "Image");
    }

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

    private void PersistHistory()
    {
        _historyManager.ReplaceAll(_historyItems);
    }

    private bool NormalizeLoadedTextTitles()
    {
        var updated = false;

        foreach (var item in _historyItems.Where(item =>
                     item.ItemKind == ClipboardItemKind.Text &&
                     !string.IsNullOrWhiteSpace(item.PlainText)))
        {
            var normalizedTitle = _textStripper.BuildTitle(item.PlainText);
            if (string.Equals(item.Title, normalizedTitle, StringComparison.Ordinal))
            {
                continue;
            }

            item.Title = normalizedTitle;
            updated = true;
        }

        return updated;
    }

    private void TrimHistoryToLimit()
    {
        while (_historyItems.Count > _settings.MaxHistoryItems)
        {
            var removed = _historyItems[^1];
            _historyItems.RemoveAt(_historyItems.Count - 1);
            DeleteUnusedImageFile(removed);
        }
    }

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

    private static string GetClipboardPathDisplayName(string path)
    {
        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
    }

    private bool ShouldSuppressCapturedItem(ClipboardItem item)
    {
        if (!_suppressedClipboardHashes.TryGetValue(item.ContentHash, out var expiresAt))
        {
            return false;
        }

        return expiresAt > DateTimeOffset.UtcNow;
    }

    private void RegisterSuppressedHash(string contentHash)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return;
        }

        _suppressedClipboardHashes[contentHash] = DateTimeOffset.UtcNow.AddSeconds(2);
    }

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
