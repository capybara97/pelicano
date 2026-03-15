using System.Numerics;
using System.Globalization;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using Pelicano.Models;
using Windows.System;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Pelicano;

/// <summary>
/// Fluent 기반 WinUI 메인 창이다.
/// 히스토리 목록, 미리보기, 설정 대화상자를 한 화면에서 제공한다.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly bool ShowSelectionPreview = false;
    private const double WidePreviewHeight = 96;
    private readonly PelicanoHost _host;
    private readonly Action _exitAction;
    private readonly Func<bool> _isBackgroundModeAvailable;
    private readonly UISettings _uiSettings = new();
    private readonly AppWindow _appWindow;
    private readonly IntPtr _windowHandle;
    private readonly SolidColorBrush _shellBrush = new(Color.FromArgb(0, 0, 0, 0));
    private readonly SolidColorBrush _panelBrush = new(Color.FromArgb(214, 19, 23, 30));
    private readonly SolidColorBrush _surfaceBrush = new(Color.FromArgb(228, 31, 38, 48));
    private readonly SolidColorBrush _accentBrush = new(Color.FromArgb(0, 0, 0, 0));
    private readonly SolidColorBrush _accentMutedBrush = new(Color.FromArgb(0, 0, 0, 0));
    private readonly SolidColorBrush _itemDividerBrush = new(Color.FromArgb(34, 255, 255, 255));
    private readonly SolidColorBrush _folderTabBrush = new(Color.FromArgb(236, 223, 185, 104));
    private readonly SolidColorBrush _folderShellBrush = new(Color.FromArgb(244, 248, 231, 193));
    private readonly SolidColorBrush _folderPocketBrush = new(Color.FromArgb(252, 255, 255, 255));
    private readonly SolidColorBrush _folderTileBrush = new(Color.FromArgb(242, 255, 249, 239));
    private readonly SolidColorBrush _folderTileStrokeBrush = new(Color.FromArgb(24, 69, 51, 18));
    private readonly SolidColorBrush _folderBadgeBrush = new(Color.FromArgb(220, 106, 72, 24));
    private Grid RootGrid = null!;
    private Border AppTitleBar = null!;
    private Grid TitleBarLayout = null!;
    private Grid ShellLayout = null!;
    private Border CommandCard = null!;
    private Grid CommandTopLayout = null!;
    private Grid HistorySplitLayout = null!;
    private Border ClipboardCard = null!;
    private Border ImageCard = null!;
    private Border PreviewCard = null!;
    private TextBox SearchBox = null!;
    private Grid SearchLayout = null!;
    private StackPanel ActionStack = null!;
    private ListViewBase ClipboardHistoryList = null!;
    private ListViewBase ImageHistoryList = null!;
    private TextBlock ClipboardSummaryText = null!;
    private TextBlock ImageSummaryText = null!;
    private TextBlock PreviewTitleText = null!;
    private TextBlock PreviewMetaText = null!;
    private TextBox PreviewTextBox = null!;
    private Image PreviewImage = null!;
    private Border PreviewImageCard = null!;
    private Border StatusBanner = null!;
    private TextBlock StatusText = null!;
    private ProgressBar StatusProgressBar = null!;
    private List<ClipboardItem> _allItems = [];
    private bool _allowClose;
    private bool _isSynchronizingSelection;
    private int _appliedTextCellScalePercent = -1;

    internal MainWindow(PelicanoHost host, Action exitAction, Func<bool> isBackgroundModeAvailable)
    {
        _host = host;
        _exitAction = exitAction;
        _isBackgroundModeAvailable = isBackgroundModeAvailable;
        InitializeComponent();
        EnsureSharedThemeResources();
        BuildUi();

        _windowHandle = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_windowHandle));

        Title = "Pelicano";

        _host.HistoryChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshFromHost);
        _host.SettingsChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshFromHost);

        _uiSettings.ColorValuesChanged += HandleAccentChanged;
        _appWindow.Closing += HandleAppWindowClosing;
        _appWindow.Changed += HandleAppWindowChanged;
        Closed += HandleClosed;
        RootGrid.Loaded += HandleLoaded;
        SizeChanged += HandleSizeChanged;

        ConfigureWindow();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ApplyBackdrop();
        RefreshAppearance();
        RefreshFromHost();
    }

    public void PrepareForExit()
    {
        _allowClose = true;
    }

    internal IntPtr WindowHandle => _windowHandle;

    public void ShowWindow()
    {
        _appWindow.Show();
        Activate();
        NativeMethods.SetForegroundWindow(_windowHandle);
        SearchBox.Focus(FocusState.Programmatic);
    }

    public void ShowStartupState()
    {
        if (!string.IsNullOrWhiteSpace(_host.StartupWarningMessage))
        {
            ShowStatus(_host.StartupWarningMessage!, InfoBarSeverity.Warning);
        }
    }

    public async Task ShowSettingsAsync()
    {
        try
        {
            var dialog = new SettingsDialog(_host.Settings)
            {
                XamlRoot = (Content as FrameworkElement)?.XamlRoot,
                RequestedTheme = RootGrid.RequestedTheme
            };
            dialog.ManualUpdateCheckAsync = () => _host.CheckForUpdatesAsync();
            dialog.ManualUpdateInstallAsync = (result, progress) => InstallApprovedUpdateAsync(result, progress);

            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary || dialog.EditedSettings is null)
            {
                return;
            }

            _host.ApplySettings(dialog.EditedSettings);
            ShowStatus("설정을 저장했습니다.", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowStatus($"설정창을 여는 중 문제가 발생했습니다. {exception.Message}", InfoBarSeverity.Error);
        }
    }

    public async Task ConfirmAndClearHistoryAsync()
    {
        if (_allItems.Count == 0)
        {
            ShowStatus("삭제할 히스토리가 없습니다.", InfoBarSeverity.Informational);
            return;
        }

        var confirmed = await ShowConfirmDialogAsync(
            "히스토리 전체 삭제",
            "저장된 클립보드 히스토리를 모두 삭제할까요?",
            "삭제",
            "취소");

        if (!confirmed)
        {
            return;
        }

        _host.ClearHistoryConfirmed();
        ShowStatus("히스토리를 모두 삭제했습니다.", InfoBarSeverity.Success);
    }

    private void BuildUi()
    {
        RootGrid = new Grid();
        RootGrid.Background = _shellBrush;
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        AppTitleBar = new Border
        {
            MinHeight = 42,
            Padding = new Thickness(20, 8, 20, 6),
            Background = _shellBrush
        };

        TitleBarLayout = new Grid
        {
            ColumnSpacing = 0
        };
        TitleBarLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var titleStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(new TextBlock
        {
            Text = "Pelicano",
            Style = GetResource<Style>("TitleTextBlockStyle")
        });
        TitleBarLayout.Children.Add(titleStack);
        AppTitleBar.Child = TitleBarLayout;

        ShellLayout = new Grid
        {
            Margin = new Thickness(20, 0, 20, 20)
        };
        ShellLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ShellLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ShellLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        CommandCard = CreateGlassCard(new Thickness(12), 22);
        var commandLayout = new Grid
        {
            RowSpacing = 6
        };
        commandLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        commandLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        CommandTopLayout = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 6
        };
        CommandTopLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        CommandTopLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        CommandTopLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        CommandTopLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        SearchLayout = new Grid
        {
            ColumnSpacing = 10
        };
        SearchLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        SearchBox = new TextBox
        {
            MinHeight = 44,
            PlaceholderText = "복사 기록 검색",
            Padding = new Thickness(12, 7, 12, 1),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        SearchBox.TextChanged += SearchBox_TextChanged;

        SearchLayout.Children.Add(SearchBox);
        Grid.SetRow(SearchLayout, 0);
        Grid.SetColumn(SearchLayout, 0);
        Grid.SetColumnSpan(SearchLayout, 2);

        ActionStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        ActionStack.Children.Add(CreateButton("설정", SettingsButton_Click));
        ActionStack.Children.Add(CreateButton("삭제", DeleteButton_Click));
        ActionStack.Children.Add(CreateButton("전체 비우기", ClearButton_Click));
        ActionStack.Children.Add(CreateButton("종료", ExitButton_Click));
        Grid.SetRow(ActionStack, 1);
        Grid.SetColumn(ActionStack, 0);
        Grid.SetColumnSpan(ActionStack, 2);

        CommandTopLayout.Children.Add(SearchLayout);
        CommandTopLayout.Children.Add(ActionStack);

        StatusBanner = CreateStatusBanner(out StatusText, out StatusProgressBar);
        StatusBanner.Margin = new Thickness(0, 2, 0, 2);
        Grid.SetRow(StatusBanner, 1);

        commandLayout.Children.Add(CommandTopLayout);
        commandLayout.Children.Add(StatusBanner);
        CommandCard.Child = commandLayout;
        Grid.SetRow(CommandCard, 0);

        HistorySplitLayout = new Grid
        {
            ColumnSpacing = 18,
            RowSpacing = 0
        };
        HistorySplitLayout.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        HistorySplitLayout.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        HistorySplitLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(HistorySplitLayout, 1);

        ClipboardCard = CreateGlassCard(new Thickness(12), 22);
        var clipboardLayout = new Grid
        {
            RowSpacing = 6
        };
        clipboardLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        clipboardLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        clipboardLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        clipboardLayout.Children.Add(CreateSectionHeader("클립보드", "텍스트 기록"));

        ClipboardHistoryList = CreateHistoryList(assetMode: false);
        Grid.SetRow(ClipboardHistoryList, 1);

        ClipboardSummaryText = CreateSummaryText("텍스트 항목");
        Grid.SetRow(ClipboardSummaryText, 2);

        clipboardLayout.Children.Add(ClipboardHistoryList);
        clipboardLayout.Children.Add(ClipboardSummaryText);
        ClipboardCard.Child = clipboardLayout;
        Grid.SetColumn(ClipboardCard, 0);
        Grid.SetRow(ClipboardCard, 0);

        ImageCard = CreateGlassCard(new Thickness(12), 22);
        var fileLayout = new Grid
        {
            RowSpacing = 6
        };
        fileLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fileLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        fileLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fileLayout.Children.Add(CreateSectionHeader("파일", "이미지와 파일 기록"));

        ImageHistoryList = CreateHistoryList(assetMode: true);
        Grid.SetRow(ImageHistoryList, 1);

        ImageSummaryText = CreateSummaryText("파일 항목");
        Grid.SetRow(ImageSummaryText, 2);

        fileLayout.Children.Add(ImageHistoryList);
        fileLayout.Children.Add(ImageSummaryText);
        ImageCard.Child = fileLayout;
        Grid.SetColumn(ImageCard, 1);
        Grid.SetRow(ImageCard, 0);

        HistorySplitLayout.Children.Add(ClipboardCard);
        HistorySplitLayout.Children.Add(ImageCard);

        PreviewCard = CreateGlassCard(new Thickness(12), 22);
        PreviewCard.Height = WidePreviewHeight;
        PreviewCard.Visibility = ShowSelectionPreview ? Visibility.Visible : Visibility.Collapsed;
        var previewLayout = new Grid
        {
            RowSpacing = 10
        };
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var previewHeading = new StackPanel
        {
            Spacing = 4
        };
        PreviewTitleText = new TextBlock
        {
            Text = "미리보기 없음",
            Style = GetResource<Style>("SubtitleTextBlockStyle")
        };
        PreviewMetaText = new TextBlock
        {
            Text = "클립보드 히스토리",
            Opacity = 0.68
        };
        previewHeading.Children.Add(PreviewTitleText);
        previewHeading.Children.Add(PreviewMetaText);

        PreviewImage = new Image
        {
            MaxHeight = 56,
            Stretch = Stretch.Uniform
        };
        PreviewImageCard = CreateSurfacePanel(new Thickness(8), 18);
        PreviewImageCard.Child = PreviewImage;
        PreviewImageCard.Visibility = Visibility.Collapsed;
        Grid.SetRow(PreviewImageCard, 1);

        PreviewTextBox = new TextBox
        {
            AcceptsReturn = true,
            Background = _surfaceBrush,
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            IsReadOnly = true,
            Padding = new Thickness(10),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(PreviewTextBox, 2);

        previewLayout.Children.Add(previewHeading);
        previewLayout.Children.Add(PreviewImageCard);
        previewLayout.Children.Add(PreviewTextBox);
        PreviewCard.Child = previewLayout;
        Grid.SetRow(PreviewCard, 2);

        RootGrid.Children.Add(AppTitleBar);
        RootGrid.Children.Add(ShellLayout);
        Grid.SetRow(ShellLayout, 1);
        ShellLayout.Children.Add(CommandCard);
        ShellLayout.Children.Add(HistorySplitLayout);
        ShellLayout.Children.Add(PreviewCard);
        Content = RootGrid;
    }

    private void ConfigureWindow()
    {
        _appWindow.Resize(new SizeInt32(1480, 940));
        UpdateResponsiveLayout(1480, 940);
    }

    private void ApplyBackdrop()
    {
        if (MicaController.IsSupported())
        {
            SystemBackdrop = new MicaBackdrop
            {
                Kind = MicaKind.BaseAlt
            };
            return;
        }

        if (DesktopAcrylicController.IsSupported())
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
    }

    private void RefreshFromHost()
    {
        RefreshAppearance();
        ApplyTextHistorySizing(_host.Settings);
        _allItems = _host.HistoryItems.ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var keyword = SearchBox.Text?.Trim() ?? string.Empty;
        var selectedItemId = GetSelectedItems().FirstOrDefault()?.Id;
        var filteredItems = string.IsNullOrWhiteSpace(keyword)
            ? _allItems
            : _allItems.Where(item =>
                    item.SearchIndex.Contains(keyword, StringComparison.CurrentCultureIgnoreCase) ||
                    item.Title.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        var clipboardItems = filteredItems
            .Where(item => item.ItemKind == ClipboardItemKind.Text)
            .ToList();
        var imageItems = filteredItems
            .Where(item => item.ItemKind != ClipboardItemKind.Text)
            .ToList();

        ClipboardHistoryList.ItemsSource = clipboardItems;
        ImageHistoryList.ItemsSource = imageItems;

        ClipboardSummaryText.Text = string.IsNullOrWhiteSpace(keyword)
            ? $"텍스트 {clipboardItems.Count}개"
            : $"텍스트 검색 {clipboardItems.Count}개";
        ImageSummaryText.Text = string.IsNullOrWhiteSpace(keyword)
            ? $"파일 {imageItems.Count}개"
            : $"파일 검색 {imageItems.Count}개";

        ApplyPreferredSelection(selectedItemId, clipboardItems, imageItems);

        UpdatePreview();
    }

    private async Task<StatusFeedback> InstallApprovedUpdateAsync(
        UpdateCheckResult result,
        IProgress<UpdateProgressInfo>? externalProgress)
    {
        if (result.Manifest is null || result.LatestVersion is null)
        {
            return new StatusFeedback("업데이트 정보를 읽지 못했습니다.", InfoBarSeverity.Error);
        }

        var latestVersionText = AppVersionInfo.ToDisplayString(result.LatestVersion);
        var progress = CreateCompositeUpdateProgressReporter(
            externalProgress,
            includeMainWindowStatus: false);

        try
        {
            progress.Report(new UpdateProgressInfo(
                $"Pelicano {latestVersionText} 업데이트를 다운로드하는 중입니다...",
                null,
                true));
            var package = await _host.DownloadInstallerAsync(result.Manifest, progress);
            _host.PrepareForUpdateInstall();
            progress.Report(new UpdateProgressInfo(
                $"Pelicano {latestVersionText} 업데이트를 적용합니다...",
                1d));
            _host.LaunchInstallerAndRequestExit(package.InstallerPath);
            return new StatusFeedback(
                $"Pelicano {latestVersionText} 업데이트를 시작합니다.",
                InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            return new StatusFeedback(
                $"업데이트 설치 파일을 준비하지 못했습니다. {exception.Message}",
                InfoBarSeverity.Error);
        }
    }

    private async Task<bool> ShowConfirmDialogAsync(
        string title,
        string message,
        string primaryButtonText,
        string closeButtonText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Primary
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void UpdatePreview()
    {
        if (!ShowSelectionPreview)
        {
            return;
        }

        var selectedItems = GetSelectedItems();

        if (selectedItems.Count == 0)
        {
            PreviewTitleText.Text = "미리보기 없음";
            PreviewMetaText.Text = "클립보드 히스토리";
            PreviewTextBox.Text = string.Empty;
            PreviewImage.Source = null;
            PreviewImageCard.Visibility = Visibility.Collapsed;
            return;
        }

        if (selectedItems.Count > 1)
        {
            PreviewTitleText.Text = $"{selectedItems.Count}개 선택됨";
            PreviewMetaText.Text = "복수 선택";
            PreviewTextBox.Text = string.Join(
                Environment.NewLine,
                selectedItems.Select(item => $"{item.KindLabel} | {item.Title}"));
            PreviewImage.Source = null;
            PreviewImageCard.Visibility = Visibility.Collapsed;
            return;
        }

        var item = selectedItems[0];
        PreviewTitleText.Text = item.Title;
        PreviewMetaText.Text = BuildPreviewSubtitle(item);

        switch (item.ItemKind)
        {
            case ClipboardItemKind.Text:
                PreviewTextBox.Text = item.PlainText;
                PreviewImage.Source = null;
                PreviewImageCard.Visibility = Visibility.Collapsed;
                break;

            case ClipboardItemKind.Image:
                PreviewTextBox.Text = item.ImagePath ?? string.Empty;
                ShowPreviewImage(item.ImagePath);
                break;

            case ClipboardItemKind.FileDrop:
                PreviewTextBox.Text = BuildFileDropPreviewText(item.FileDropPaths);
                ShowPreviewFileDrop(item.FileDropPaths);
                break;
        }
    }

    private void ShowPreviewImage(string? imagePath)
    {
        ShowPreviewImage(imagePath, allowExternalPath: false);
    }

    private void ShowPreviewImage(string? imagePath, bool allowExternalPath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) ||
            !File.Exists(imagePath))
        {
            PreviewImage.Source = null;
            PreviewImageCard.Visibility = Visibility.Collapsed;
            return;
        }

        if (!allowExternalPath && !AppPaths.IsManagedImagePath(imagePath))
        {
            PreviewImage.Source = null;
            PreviewImageCard.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewImage.Source = new BitmapImage(new Uri(imagePath));
        PreviewImageCard.Visibility = Visibility.Visible;
    }

    private void ShowPreviewFileDrop(IReadOnlyList<string> filePaths)
    {
        var previewPath = filePaths.FirstOrDefault(IsPreviewImagePath);
        ShowPreviewImage(previewPath, allowExternalPath: true);
    }

    private List<ClipboardItem> GetSelectedItems()
    {
        if (ClipboardHistoryList.SelectedItems.Count > 0)
        {
            return ClipboardHistoryList.SelectedItems
                .Cast<ClipboardItem>()
                .ToList();
        }

        return ImageHistoryList.SelectedItems
            .Cast<ClipboardItem>()
            .ToList();
    }

    private IProgress<UpdateProgressInfo> CreateCompositeUpdateProgressReporter(
        IProgress<UpdateProgressInfo>? externalProgress,
        bool includeMainWindowStatus)
    {
        return new Progress<UpdateProgressInfo>(info =>
        {
            externalProgress?.Report(info);

            if (includeMainWindowStatus)
            {
                ShowStatus(info.Message, InfoBarSeverity.Informational, info.ProgressRatio, info.IsIndeterminate);
            }
        });
    }

    private void ShowStatus(
        string message,
        InfoBarSeverity severity,
        double? progressRatio = null,
        bool isProgressIndeterminate = false)
    {
        StatusText.Text = message;
        StatusBanner.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
        StatusBanner.Background = CreateStatusBackground(severity);

        if (isProgressIndeterminate || progressRatio.HasValue)
        {
            StatusProgressBar.Visibility = Visibility.Visible;
            StatusProgressBar.IsIndeterminate = isProgressIndeterminate;

            if (!isProgressIndeterminate)
            {
                StatusProgressBar.Value = Math.Clamp(progressRatio.GetValueOrDefault() * 100d, 0d, 100d);
            }
        }
        else
        {
            HideStatusProgress();
        }
    }

    private void HideStatusProgress()
    {
        StatusProgressBar.IsIndeterminate = false;
        StatusProgressBar.Value = 0;
        StatusProgressBar.Visibility = Visibility.Collapsed;
    }

    private void RefreshAccentResources()
    {
        var accent = _uiSettings.GetColorValue(UIColorType.Accent);
        _accentBrush.Color = accent;
        _accentMutedBrush.Color = Color.FromArgb(180, accent.R, accent.G, accent.B);
    }

    private void HandleAccentChanged(UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(RefreshAppearance);
    }

    private void HandleAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange && !args.DidPresenterChange)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(UpdateTitleBarChrome);
    }

    private void HandleAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        if (!_isBackgroundModeAvailable())
        {
            return;
        }

        args.Cancel = true;
        _appWindow.Hide();
    }

    private void HandleClosed(object sender, WindowEventArgs args)
    {
        _uiSettings.ColorValuesChanged -= HandleAccentChanged;
        _appWindow.Closing -= HandleAppWindowClosing;
        _appWindow.Changed -= HandleAppWindowChanged;
        RootGrid.Loaded -= HandleLoaded;
        SizeChanged -= HandleSizeChanged;
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowSettingsAsync();
        }
        catch (Exception exception)
        {
            ShowStatus($"설정창 처리 중 오류가 발생했습니다. {exception.Message}", InfoBarSeverity.Error);
        }
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        await ConfirmAndClearHistoryAsync();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _exitAction();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedItems();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs args)
    {
        ApplyFilter();
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingSelection && sender is ListViewBase sourceList && sourceList.SelectedItems.Count > 0)
        {
            var otherList = ReferenceEquals(sourceList, ClipboardHistoryList)
                ? ImageHistoryList
                : ClipboardHistoryList;

            _isSynchronizingSelection = true;
            otherList.SelectedItems.Clear();
            _isSynchronizingSelection = false;
        }

        UpdatePreview();
    }

    private void HistoryList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        CopySelectedItems(showImmediateCopyMessage: true);
    }

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

    private static bool IsPreviewImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeClipboardPath(string path)
    {
        var typeLabel = Directory.Exists(path) ? "[폴더]" : "[파일]";
        return $"{typeLabel} {path}";
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveLayout(
            Content is FrameworkElement element ? element.ActualWidth : 1440,
            Content is FrameworkElement sizedElement ? sizedElement.ActualHeight : 900);
        UpdateTitleBarChrome();
        StartEntranceAnimations();
    }

    private void HandleSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        UpdateResponsiveLayout(args.Size.Width, args.Size.Height);
    }

    private void RefreshAppearance()
    {
        ApplySystemTheme();
        RefreshAccentResources();
        UpdateMaterialResources();
        UpdateTitleBarChrome();
    }

    private void ApplySystemTheme()
    {
        RootGrid.RequestedTheme = GetRequestedTheme(_host.Settings.ThemeMode);
    }

    private void UpdateMaterialResources()
    {
        var isDark = RootGrid.RequestedTheme != ElementTheme.Light;
        _shellBrush.Color = isDark
            ? Color.FromArgb(28, 15, 19, 26)
            : Color.FromArgb(255, 255, 255, 255);
        _panelBrush.Color = isDark
            ? Color.FromArgb(214, 19, 23, 30)
            : Color.FromArgb(244, 255, 255, 255);
        _surfaceBrush.Color = isDark
            ? Color.FromArgb(228, 31, 38, 48)
            : Color.FromArgb(252, 255, 255, 255);
        _itemDividerBrush.Color = isDark
            ? Color.FromArgb(48, 255, 255, 255)
            : Color.FromArgb(26, 28, 39, 56);
        _folderTabBrush.Color = isDark
            ? Color.FromArgb(224, 144, 108, 54)
            : Color.FromArgb(236, 223, 185, 104);
        _folderShellBrush.Color = isDark
            ? Color.FromArgb(214, 72, 55, 33)
            : Color.FromArgb(244, 248, 231, 193);
        _folderPocketBrush.Color = isDark
            ? Color.FromArgb(232, 24, 29, 36)
            : Color.FromArgb(252, 255, 255, 255);
        _folderTileBrush.Color = isDark
            ? Color.FromArgb(210, 36, 43, 53)
            : Color.FromArgb(242, 255, 249, 239);
        _folderTileStrokeBrush.Color = isDark
            ? Color.FromArgb(58, 255, 248, 231)
            : Color.FromArgb(28, 69, 51, 18);
        _folderBadgeBrush.Color = isDark
            ? Color.FromArgb(230, 244, 227, 188)
            : Color.FromArgb(220, 106, 72, 24);
        SyncSharedThemeResources(isDark);
    }

    private void EnsureSharedThemeResources()
    {
        try
        {
            Application.Current.Resources["PelicanoPanelBrush"] = _panelBrush;
            Application.Current.Resources["PelicanoSurfaceBrush"] = _surfaceBrush;
            Application.Current.Resources["PelicanoItemDividerBrush"] = _itemDividerBrush;
            Application.Current.Resources["PelicanoFolderTabBrush"] = _folderTabBrush;
            Application.Current.Resources["PelicanoFolderShellBrush"] = _folderShellBrush;
            Application.Current.Resources["PelicanoFolderPocketBrush"] = _folderPocketBrush;
            Application.Current.Resources["PelicanoFolderTileBrush"] = _folderTileBrush;
            Application.Current.Resources["PelicanoFolderTileStrokeBrush"] = _folderTileStrokeBrush;
            Application.Current.Resources["PelicanoFolderBadgeBrush"] = _folderBadgeBrush;
        }
        catch
        {
        }
    }

    private void SyncSharedThemeResources(bool isDark)
    {
        try
        {
            Application.Current.Resources["PelicanoPanelBrush"] = _panelBrush;
            Application.Current.Resources["PelicanoSurfaceBrush"] = _surfaceBrush;
            Application.Current.Resources["PelicanoItemDividerBrush"] = _itemDividerBrush;
            Application.Current.Resources["PelicanoFolderTabBrush"] = _folderTabBrush;
            Application.Current.Resources["PelicanoFolderShellBrush"] = _folderShellBrush;
            Application.Current.Resources["PelicanoFolderPocketBrush"] = _folderPocketBrush;
            Application.Current.Resources["PelicanoFolderTileBrush"] = _folderTileBrush;
            Application.Current.Resources["PelicanoFolderTileStrokeBrush"] = _folderTileStrokeBrush;
            Application.Current.Resources["PelicanoFolderBadgeBrush"] = _folderBadgeBrush;
            Application.Current.Resources["PelicanoThemeIsDark"] = isDark;
        }
        catch
        {
        }
    }

    private void UpdateResponsiveLayout(double width, double height)
    {
        ApplyTitleBarPadding(new Thickness(20, 8, 20, 6));
        ShellLayout.Margin = new Thickness(20, 0, 20, 20);

        CommandCard.Padding = new Thickness(12);
        ClipboardCard.Padding = new Thickness(12);
        ImageCard.Padding = new Thickness(12);
        PreviewCard.Padding = new Thickness(12);

        SearchBox.MinHeight = 44;
        ActionStack.Spacing = 8;

        foreach (var button in ActionStack.Children.OfType<Button>())
        {
            button.MinHeight = 44;
            button.Padding = new Thickness(14, 0, 14, 0);
        }

        Grid.SetRow(ActionStack, 1);
        Grid.SetColumn(ActionStack, 0);
        Grid.SetColumnSpan(ActionStack, 2);
        Grid.SetColumnSpan(SearchLayout, 2);
        CommandTopLayout.ColumnDefinitions[1].Width = new GridLength(0);

        PreviewCard.Height = WidePreviewHeight;
        PreviewImage.MaxHeight = 56;
        PreviewMetaText.Visibility = Visibility.Visible;
    }

    private void UpdateTitleBarChrome()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBar = _appWindow.TitleBar;
        var isDark = RootGrid.RequestedTheme != ElementTheme.Light;
        var transparent = Color.FromArgb(0, 0, 0, 0);
        var activeForeground = isDark
            ? Color.FromArgb(255, 244, 247, 252)
            : Color.FromArgb(255, 22, 27, 34);
        var inactiveForeground = isDark
            ? Color.FromArgb(208, 220, 226, 234)
            : Color.FromArgb(192, 74, 82, 92);
        var hoverBackground = isDark
            ? Color.FromArgb(54, 255, 255, 255)
            : Color.FromArgb(20, 17, 24, 39);
        var pressedBackground = isDark
            ? Color.FromArgb(82, 255, 255, 255)
            : Color.FromArgb(40, 17, 24, 39);

        titleBar.BackgroundColor = transparent;
        titleBar.InactiveBackgroundColor = transparent;
        titleBar.ForegroundColor = activeForeground;
        titleBar.InactiveForegroundColor = inactiveForeground;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = activeForeground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverForegroundColor = activeForeground;
        titleBar.ButtonPressedForegroundColor = activeForeground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
    }

    private void ApplyTitleBarPadding(Thickness basePadding)
    {
        var leftInset = 0d;
        var rightInset = 0d;

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            leftInset = _appWindow.TitleBar.LeftInset;
            rightInset = _appWindow.TitleBar.RightInset;
        }

        AppTitleBar.Padding = new Thickness(
            Math.Max(basePadding.Left, leftInset + 12),
            basePadding.Top,
            Math.Max(basePadding.Right, rightInset + 12),
            basePadding.Bottom);
    }

    private void StartEntranceAnimations()
    {
        if (!_uiSettings.AnimationsEnabled)
        {
            return;
        }

        AnimateEntrance(AppTitleBar, 0, 16);
        AnimateEntrance(ClipboardCard, 70, 26);
        AnimateEntrance(ImageCard, 120, 28);

        if (ShowSelectionPreview)
        {
            AnimateEntrance(PreviewCard, 180, 30);
        }
    }

    private static void AnimateEntrance(UIElement element, int delayMilliseconds, float initialOffsetY)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        visual.Opacity = 0;
        visual.Offset = new Vector3(0, initialOffsetY, 0);

        var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
        fadeAnimation.InsertKeyFrame(1f, 1f);
        fadeAnimation.Duration = TimeSpan.FromMilliseconds(220);
        fadeAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMilliseconds);

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.InsertKeyFrame(1f, Vector3.Zero);
        offsetAnimation.Duration = TimeSpan.FromMilliseconds(320);
        offsetAnimation.DelayTime = TimeSpan.FromMilliseconds(delayMilliseconds);

        visual.StartAnimation("Opacity", fadeAnimation);
        visual.StartAnimation("Offset", offsetAnimation);
    }

    private static ElementTheme DetectPreferredTheme()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = personalizeKey?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }
        catch
        {
            return ElementTheme.Default;
        }
    }

    private static ElementTheme GetRequestedTheme(AppThemeMode themeMode)
    {
        return themeMode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => DetectPreferredTheme()
        };
    }

    private Border CreateGlassCard(Thickness padding, double cornerRadius)
    {
        return new Border
        {
            Padding = padding,
            CornerRadius = new CornerRadius(cornerRadius),
            Background = _panelBrush,
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private ListViewBase CreateHistoryList(bool assetMode)
    {
        ListViewBase list = new ListView
        {
            IsMultiSelectCheckBoxEnabled = false
        };

        list.SelectionMode = ListViewSelectionMode.Extended;
        list.ItemTemplate = assetMode
            ? CreateAssetHistoryItemTemplate()
            : CreateHistoryItemTemplate(12);
        list.ItemContainerStyle = assetMode
            ? CreateAssetHistoryItemContainerStyle()
            : CreateHistoryItemContainerStyle(18, 0);
        list.SelectionChanged += HistoryList_SelectionChanged;
        list.DoubleTapped += HistoryList_DoubleTapped;
        list.KeyDown += HistoryList_KeyDown;
        return list;
    }

    private void HistoryList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CopySelectedItems(showImmediateCopyMessage: true);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Delete)
        {
            DeleteSelectedItems();
            e.Handled = true;
        }
    }

    private void CopySelectedItems(bool showImmediateCopyMessage)
    {
        var selectedItems = GetSelectedItems();

        if (selectedItems.Count == 0)
        {
            ShowStatus("복사할 항목을 먼저 선택해 주세요.", InfoBarSeverity.Informational);
            return;
        }

        _host.CopySelection(selectedItems);
        ShowStatus(
            showImmediateCopyMessage
                ? "선택한 항목을 즉시 복사했습니다."
                : $"{selectedItems.Count}개 항목을 다시 클립보드에 복사했습니다.",
            InfoBarSeverity.Success);
    }

    private void DeleteSelectedItems()
    {
        var selectedItems = GetSelectedItems();

        if (selectedItems.Count == 0)
        {
            ShowStatus("삭제할 항목을 먼저 선택해 주세요.", InfoBarSeverity.Informational);
            return;
        }

        if (selectedItems.Count == 1)
        {
            _host.DeleteItem(selectedItems[0]);
        }
        else
        {
            _host.DeleteSelection(selectedItems);
        }

        ShowStatus($"{selectedItems.Count}개 항목을 삭제했습니다.", InfoBarSeverity.Success);
    }

    private void ApplyTextHistorySizing(AppSettings settings)
    {
        var scalePercent = Math.Clamp(
            settings.TextCellScalePercent,
            AppSettings.MinimumTextCellScalePercent,
            AppSettings.MaximumTextCellScalePercent);

        if (_appliedTextCellScalePercent == scalePercent)
        {
            return;
        }

        var scale = scalePercent / 100d;
        var fontSize = Math.Round(12d * scale, 1);
        var minHeight = Math.Max(18, (int)Math.Round(18d * scale));
        var verticalPadding = scalePercent switch
        {
            >= 145 => 3,
            >= 130 => 2,
            >= 115 => 1,
            _ => 0
        };

        ClipboardHistoryList.ItemTemplate = CreateHistoryItemTemplate(fontSize);
        ClipboardHistoryList.ItemContainerStyle = CreateHistoryItemContainerStyle(minHeight, verticalPadding);
        _appliedTextCellScalePercent = scalePercent;
    }

    private static DataTemplate CreateHistoryItemTemplate(double fontSize)
    {
        var fontSizeText = fontSize.ToString("0.##", CultureInfo.InvariantCulture);
        var templateXaml =
            $$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid Padding="0">
                    <TextBlock Text="{Binding Title}"
                               VerticalAlignment="Center"
                               MaxLines="1"
                               TextTrimming="CharacterEllipsis"
                               FontSize="{{fontSizeText}}" />
                    <Border
                            Height="1"
                            VerticalAlignment="Bottom"
                            Margin="4,0,4,0"
                            Background="{StaticResource PelicanoItemDividerBrush}" />
                </Grid>
            </DataTemplate>
            """;

        return (DataTemplate)XamlReader.Load(templateXaml);
    }

    private static Style CreateHistoryItemContainerStyle(int minHeight, int verticalPadding)
    {
        var minHeightText = minHeight.ToString(CultureInfo.InvariantCulture);
        var paddingText = $"2,{verticalPadding.ToString(CultureInfo.InvariantCulture)},2,{verticalPadding.ToString(CultureInfo.InvariantCulture)}";
        var styleXaml =
            $$"""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
                <Setter Property="MinHeight" Value="{{minHeightText}}" />
                <Setter Property="Padding" Value="{{paddingText}}" />
                <Setter Property="Margin" Value="0" />
                <Setter Property="HorizontalContentAlignment" Value="Stretch" />
            </Style>
            """;

        return (Style)XamlReader.Load(styleXaml);
    }

    private static DataTemplate CreateAssetHistoryItemTemplate()
    {
        const string templateXaml =
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid Padding="0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="48" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <Border Width="44"
                            Height="44"
                            CornerRadius="12"
                            Background="{StaticResource PelicanoSurfaceBrush}"
                            BorderBrush="{StaticResource PelicanoPanelBrush}"
                            BorderThickness="1"
                            HorizontalAlignment="Center"
                            VerticalAlignment="Center">
                        <Grid>
                            <Border CornerRadius="11"
                                    Background="{StaticResource PelicanoSurfaceBrush}">
                                <Border.Background>
                                    <ImageBrush ImageSource="{Binding ThumbnailBitmap}"
                                                Stretch="UniformToFill"
                                                AlignmentX="Center"
                                                AlignmentY="Center" />
                                </Border.Background>
                            </Border>
                            <TextBlock Text="{Binding PreviewBadgeText}"
                                       HorizontalAlignment="Center"
                                       VerticalAlignment="Center"
                                       MaxLines="1"
                                       TextTrimming="CharacterEllipsis"
                                       FontSize="10"
                                       FontWeight="SemiBold"
                                       Opacity="0.82"
                                       Margin="4,0,4,0" />
                        </Grid>
                    </Border>
                    <StackPanel Grid.Column="1"
                                VerticalAlignment="Center">
                        <TextBlock Text="{Binding Title}"
                                   MaxLines="1"
                                   TextTrimming="CharacterEllipsis"
                                   FontSize="12"
                                   FontWeight="SemiBold" />
                    </StackPanel>
                    <Border Grid.ColumnSpan="2"
                            Height="1"
                            VerticalAlignment="Bottom"
                            Margin="4,0,4,0"
                            Background="{StaticResource PelicanoItemDividerBrush}" />
                </Grid>
            </DataTemplate>
            """;

        return (DataTemplate)XamlReader.Load(templateXaml);
    }

    private static Style CreateAssetHistoryItemContainerStyle()
    {
        const string styleXaml =
            """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
                <Setter Property="MinHeight" Value="46" />
                <Setter Property="Padding" Value="4,2" />
                <Setter Property="Margin" Value="0" />
                <Setter Property="HorizontalContentAlignment" Value="Stretch" />
            </Style>
            """;

        return (Style)XamlReader.Load(styleXaml);
    }

    private static StackPanel CreateSectionHeader(string title, string subtitle)
    {
        var header = new StackPanel
        {
            Spacing = 1
        };
        header.Children.Add(new TextBlock
        {
            Text = title,
            Style = GetResource<Style>("SubtitleTextBlockStyle")
        });
        header.Children.Add(new TextBlock
        {
            Text = subtitle,
            Opacity = 0.68
        });
        return header;
    }

    private static TextBlock CreateSummaryText(string placeholder)
    {
        return new TextBlock
        {
            Margin = new Thickness(1, 0, 0, 0),
            Opacity = 0.72,
            FontSize = 10,
            Text = placeholder
        };
    }

    private void ApplyPreferredSelection(
        Guid? selectedItemId,
        IReadOnlyList<ClipboardItem> clipboardItems,
        IReadOnlyList<ClipboardItem> imageItems)
    {
        var selectedClipboardItem = selectedItemId.HasValue
            ? clipboardItems.FirstOrDefault(item => item.Id == selectedItemId.Value)
            : null;
        var selectedImageItem = selectedItemId.HasValue
            ? imageItems.FirstOrDefault(item => item.Id == selectedItemId.Value)
            : null;

        _isSynchronizingSelection = true;

        ClipboardHistoryList.SelectedItems.Clear();
        ImageHistoryList.SelectedItems.Clear();

        if (selectedClipboardItem is not null)
        {
            ClipboardHistoryList.SelectedItem = selectedClipboardItem;
        }
        else if (selectedImageItem is not null)
        {
            ImageHistoryList.SelectedItem = selectedImageItem;
        }
        else if (clipboardItems.Count > 0)
        {
            ClipboardHistoryList.SelectedItem = clipboardItems[0];
        }
        else if (imageItems.Count > 0)
        {
            ImageHistoryList.SelectedItem = imageItems[0];
        }

        _isSynchronizingSelection = false;
    }

    private Border CreateSurfacePanel(Thickness padding, double cornerRadius)
    {
        return new Border
        {
            Padding = padding,
            CornerRadius = new CornerRadius(cornerRadius),
            Background = _surfaceBrush,
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private static Button CreateButton(string label, RoutedEventHandler clickHandler, bool accent = false)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = 44
        };
        button.Click += clickHandler;

        if (accent && GetResource<Style>("AccentButtonStyle") is Style style)
        {
            button.Style = style;
        }

        return button;
    }

    private Border CreateStatusBanner(out TextBlock messageText, out ProgressBar progressBar)
    {
        messageText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        progressBar = new ProgressBar
        {
            Visibility = Visibility.Collapsed,
            MinHeight = 6,
            Height = 6,
            Minimum = 0,
            Maximum = 100
        };

        var contentStack = new StackPanel
        {
            Spacing = 10
        };
        contentStack.Children.Add(messageText);
        contentStack.Children.Add(progressBar);

        return new Border
        {
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(18),
            Background = CreateStatusBackground(InfoBarSeverity.Informational),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Child = contentStack
        };
    }

    private Brush CreateStatusBackground(InfoBarSeverity severity)
    {
        var isDark = RootGrid.RequestedTheme != ElementTheme.Light;

        return severity switch
        {
            InfoBarSeverity.Success => new SolidColorBrush(isDark
                ? Color.FromArgb(180, 33, 95, 62)
                : Color.FromArgb(242, 244, 252, 246)),
            InfoBarSeverity.Warning => new SolidColorBrush(isDark
                ? Color.FromArgb(185, 117, 73, 18)
                : Color.FromArgb(244, 255, 250, 242)),
            InfoBarSeverity.Error => new SolidColorBrush(isDark
                ? Color.FromArgb(185, 114, 44, 51)
                : Color.FromArgb(244, 254, 244, 244)),
            _ => new SolidColorBrush(isDark
                ? Color.FromArgb(150, 48, 66, 92)
                : Color.FromArgb(242, 246, 249, 253))
        };
    }

    private static Brush GetBrush(string key)
    {
        try
        {
            return Application.Current.Resources[key] as Brush
                ?? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }
        catch
        {
            return new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }
    }

    private static T? GetResource<T>(string key) where T : class
    {
        return Application.Current.Resources.ContainsKey(key)
            ? Application.Current.Resources[key] as T
            : null;
    }

}
