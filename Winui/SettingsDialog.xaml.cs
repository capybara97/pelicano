using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pelicano.Models;
using Windows.UI;

namespace Pelicano;

/// <summary>
/// WinUI ContentDialog 기반 설정 편집 창이다.
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private static readonly int[] TextCellScalePresets = [100, 115, 130, 145];
    private UpdateCheckResult? _pendingUpdateResult;
    private bool _isUpdateActionRunning;
    private ScrollViewer DialogScrollViewer = null!;
    private ComboBox ThemeModeBox = null!;
    private ComboBox TextCellSizeBox = null!;
    private ToggleSwitch PlainTextOnlySwitch = null!;
    private ToggleSwitch CaptureAssetsSwitch = null!;
    private ToggleSwitch AuditLoggingSwitch = null!;
    private ToggleSwitch StartWithWindowsSwitch = null!;
    private TextBox MaxHistoryItemsBox = null!;
    private Button CheckUpdatesButton = null!;
    private Button ApproveUpdateButton = null!;
    private Border UpdateStatusBanner = null!;
    private TextBlock UpdateStatusText = null!;
    private ProgressBar UpdateProgressBar = null!;

    public SettingsDialog(AppSettings currentSettings)
    {
        InitializeComponent();
        BuildUi();
        Title = "설정";
        PrimaryButtonText = "저장";
        CloseButtonText = "취소";
        DefaultButton = ContentDialogButton.Primary;
        MaxWidth = 720;
        PrimaryButtonClick += ContentDialog_PrimaryButtonClick;
        Opened += SettingsDialog_Opened;
        EditedSettings = currentSettings.Clone();
        ThemeModeBox.SelectedIndex = GetThemeModeIndex(currentSettings.ThemeMode);
        TextCellSizeBox.SelectedIndex = GetTextCellSizeIndex(currentSettings.TextCellScalePercent);
        PlainTextOnlySwitch.IsOn = currentSettings.EnforcePlainTextOnly;
        CaptureAssetsSwitch.IsOn = currentSettings.CaptureFileDrops || currentSettings.CaptureImages;
        AuditLoggingSwitch.IsOn = currentSettings.EnableAuditLogging;
        StartWithWindowsSwitch.IsOn = currentSettings.StartWithWindows;
        MaxHistoryItemsBox.Text = currentSettings.MaxHistoryItems.ToString();
        RefreshDialogSizing();
        RefreshUpdateActionState();
    }

    /// <summary>
    /// 수동 업데이트 확인 로직을 외부에서 주입한다.
    /// </summary>
    internal Func<Task<UpdateCheckResult>>? ManualUpdateCheckAsync { get; set; }

    /// <summary>
    /// 사용자 승인 후 실제 업데이트를 다운로드하고 설치한다.
    /// </summary>
    internal Func<UpdateCheckResult, IProgress<UpdateProgressInfo>, Task<StatusFeedback>>? ManualUpdateInstallAsync { get; set; }

    /// <summary>
    /// 저장 직전 검증된 설정 결과다.
    /// </summary>
    public AppSettings? EditedSettings { get; private set; }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ManualUpdateCheckAsync is null)
        {
            return;
        }

        if (!TryBuildSettings(out _, out var validationMessage))
        {
            ShowStatus(validationMessage, InfoBarSeverity.Warning);
            return;
        }

        _pendingUpdateResult = null;
        _isUpdateActionRunning = true;
        RefreshUpdateActionState();
        ShowStatus("업데이트를 확인하는 중입니다...", InfoBarSeverity.Informational);
        ShowProgress(new UpdateProgressInfo("업데이트를 확인하는 중입니다...", null, true));

        try
        {
            var result = await ManualUpdateCheckAsync();
            switch (result.State)
            {
                case UpdateCheckState.UpToDate:
                    ShowStatus(result.Message, InfoBarSeverity.Success);
                    break;

                case UpdateCheckState.Failed:
                    ShowStatus(result.Message, InfoBarSeverity.Error);
                    break;

                case UpdateCheckState.UpdateAvailable:
                    _pendingUpdateResult = result;
                    var versionText = result.LatestVersion is null
                        ? result.Manifest?.VersionText ?? "새 버전"
                        : AppVersionInfo.ToDisplayString(result.LatestVersion);
                    ShowStatus(
                        $"Pelicano {versionText} 업데이트가 준비되었습니다. 업데이트 시작을 눌러 진행하세요.",
                        InfoBarSeverity.Warning);
                    break;
            }
        }
        catch (Exception exception)
        {
            ShowStatus($"업데이트 확인에 실패했습니다. {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            _isUpdateActionRunning = false;
            HideProgress();
            RefreshUpdateActionState();
        }
    }

    private async void ApproveUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdateResult is null || ManualUpdateInstallAsync is null)
        {
            return;
        }

        _isUpdateActionRunning = true;
        RefreshUpdateActionState();
        ShowStatus("업데이트를 준비하는 중입니다...", InfoBarSeverity.Informational);
        ShowProgress(new UpdateProgressInfo("업데이트를 준비하는 중입니다...", null, true));

        try
        {
            var progress = new Progress<UpdateProgressInfo>(ShowProgress);
            var feedback = await ManualUpdateInstallAsync(_pendingUpdateResult, progress);
            if (!string.IsNullOrWhiteSpace(feedback.Message))
            {
                ShowStatus(feedback.Message, feedback.Severity);
            }
        }
        catch (Exception exception)
        {
            ShowStatus($"업데이트 설치를 시작하지 못했습니다. {exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            _isUpdateActionRunning = false;
            HideProgress();
            RefreshUpdateActionState();
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (TryBuildSettings(out var settings, out var validationMessage))
        {
            EditedSettings = settings;
            return;
        }

        args.Cancel = true;
        ShowStatus(validationMessage, InfoBarSeverity.Warning);
    }

    private bool TryBuildSettings(out AppSettings settings, out string validationMessage)
    {
        if (!int.TryParse(MaxHistoryItemsBox.Text.Trim(), out var maxHistoryItems))
        {
            settings = EditedSettings?.Clone() ?? new AppSettings();
            validationMessage = "최대 히스토리 개수는 20에서 500 사이 숫자로 입력해 주세요.";
            return false;
        }

        maxHistoryItems = Math.Clamp(maxHistoryItems, 20, 500);

        var previousSettings = EditedSettings?.Clone() ?? new AppSettings();

        settings = new AppSettings
        {
            ThemeMode = GetSelectedThemeMode(),
            EnforcePlainTextOnly = PlainTextOnlySwitch.IsOn,
            CaptureFileDrops = CaptureAssetsSwitch.IsOn,
            CaptureImages = CaptureAssetsSwitch.IsOn,
            CapturePaused = previousSettings.CapturePaused,
            EnableAuditLogging = AuditLoggingSwitch.IsOn,
            StartWithWindows = StartWithWindowsSwitch.IsOn,
            MaxHistoryItems = maxHistoryItems,
            TextCellScalePercent = GetSelectedTextCellScalePercent(),
            AutoExpireMinutes = previousSettings.AutoExpireMinutes
        };
        validationMessage = string.Empty;
        return true;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        UpdateStatusText.Text = message;
        UpdateStatusBanner.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateStatusBanner.Background = CreateStatusBackground(severity);
    }

    private void ShowProgress(UpdateProgressInfo progressInfo)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowProgress(progressInfo));
            return;
        }

        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.IsIndeterminate = progressInfo.IsIndeterminate;

        if (!progressInfo.IsIndeterminate && progressInfo.ProgressRatio.HasValue)
        {
            UpdateProgressBar.Value = Math.Clamp(progressInfo.ProgressRatio.Value * 100d, 0d, 100d);
        }

        if (!string.IsNullOrWhiteSpace(progressInfo.Message))
        {
            ShowStatus(progressInfo.Message, InfoBarSeverity.Informational);
        }
    }

    private void HideProgress()
    {
        UpdateProgressBar.IsIndeterminate = false;
        UpdateProgressBar.Value = 0;
        UpdateProgressBar.Visibility = Visibility.Collapsed;
    }

    private void BuildUi()
    {
        var contentStack = new StackPanel
        {
            Spacing = 24,
            MaxWidth = 680
        };

        contentStack.Children.Add(CreateHeaderSurface());

        ThemeModeBox = CreateAlignedComboBox("테마를 선택하세요");
        ThemeModeBox.Items.Add(CreateThemeOption("시스템 따라가기", AppThemeMode.System));
        ThemeModeBox.Items.Add(CreateThemeOption("라이트", AppThemeMode.Light));
        ThemeModeBox.Items.Add(CreateThemeOption("다크", AppThemeMode.Dark));

        MaxHistoryItemsBox = CreateAlignedTextBox("20 ~ 500", width: 180);
        TextCellSizeBox = CreateAlignedComboBox("기본");
        TextCellSizeBox.Items.Add(CreateTextCellSizeOption("기본", 100));
        TextCellSizeBox.Items.Add(CreateTextCellSizeOption("조금 크게", 115));
        TextCellSizeBox.Items.Add(CreateTextCellSizeOption("크게", 130));
        TextCellSizeBox.Items.Add(CreateTextCellSizeOption("아주 크게", 145));

        PlainTextOnlySwitch = CreateToggle();
        CaptureAssetsSwitch = CreateToggle();
        AuditLoggingSwitch = CreateToggle();
        StartWithWindowsSwitch = CreateToggle();

        var generalRows = new UIElement[]
        {
            CreateSettingRow(
                "테마",
                string.Empty,
                ThemeModeBox),
            CreateSettingRow(
                "최대 히스토리 개수",
                string.Empty,
                MaxHistoryItemsBox),
            CreateSettingRow(
                "텍스트 셀 크기",
                string.Empty,
                TextCellSizeBox),
            CreateToggleRow(
                "서식 제거 후 복사",
                string.Empty,
                PlainTextOnlySwitch),
            CreateToggleRow(
                "이미지와 파일도 저장",
                string.Empty,
                CaptureAssetsSwitch),
            CreateToggleRow(
                "진단 로그 저장",
                string.Empty,
                AuditLoggingSwitch),
            CreateToggleRow(
                "Windows 시작 시 실행",
                string.Empty,
                StartWithWindowsSwitch)
        };

        var generalSection = CreateSectionCard(
            "일반",
            string.Empty,
            CreateSettingsGroup(generalRows));

        CheckUpdatesButton = new Button
        {
            Content = "지금 확인",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinHeight = 44,
            MinWidth = 120
        };
        if (GetResource<Style>("DefaultButtonStyle") is Style defaultButtonStyle)
        {
            CheckUpdatesButton.Style = defaultButtonStyle;
        }
        CheckUpdatesButton.Click += CheckUpdatesButton_Click;

        ApproveUpdateButton = new Button
        {
            Content = "업데이트 시작",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinHeight = 44,
            MinWidth = 132,
            Visibility = Visibility.Collapsed
        };
        if (GetResource<Style>("AccentButtonStyle") is Style accentButtonStyle)
        {
            ApproveUpdateButton.Style = accentButtonStyle;
        }
        ApproveUpdateButton.Click += ApproveUpdateButton_Click;

        UpdateStatusBanner = CreateStatusBanner(out UpdateStatusText);
        UpdateProgressBar = new ProgressBar
        {
            Visibility = Visibility.Collapsed,
            MinHeight = 6,
            Height = 6,
            Minimum = 0,
            Maximum = 100
        };

        var updateContent = new StackPanel
        {
            Spacing = 12
        };
        var updateActionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        updateActionRow.Children.Add(CheckUpdatesButton);
        updateActionRow.Children.Add(ApproveUpdateButton);
        updateContent.Children.Add(new Border
        {
            Padding = new Thickness(18, 16, 18, 16),
            Child = updateActionRow
        });
        updateContent.Children.Add(UpdateStatusBanner);
        updateContent.Children.Add(UpdateProgressBar);
        updateContent.Children.Add(new TextBlock
        {
            Text = $"현재 버전 v{AppVersionInfo.CurrentVersionText}",
            Foreground = GetBrush("TextFillColorSecondaryBrush"),
            FontSize = 12.5,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4, 0, 0, 0)
        });

        var updateSection = CreateSectionCard(
            "업데이트",
            string.Empty,
            updateContent);

        contentStack.Children.Add(generalSection);
        contentStack.Children.Add(updateSection);

        DialogScrollViewer = new ScrollViewer
        {
            Content = contentStack,
            Padding = new Thickness(6, 0, 6, 0),
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        Content = DialogScrollViewer;
    }

    private static Border CreateGlassCard()
    {
        return new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(24),
            Background = GetBrush("PelicanoPanelBrush"),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private void SettingsDialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        RefreshDialogSizing();
    }

    private void RefreshDialogSizing()
    {
        var viewportWidth = XamlRoot?.Size.Width ?? 1280;
        var viewportHeight = XamlRoot?.Size.Height ?? 900;

        MaxWidth = Math.Clamp(viewportWidth * 0.74, 560, 760);
        var dialogHeight = Math.Clamp(viewportHeight * 0.8, 470, 760);
        DialogScrollViewer.Height = dialogHeight;
        DialogScrollViewer.MaxHeight = dialogHeight;
    }

    private void RefreshUpdateActionState()
    {
        var hasPendingUpdate = _pendingUpdateResult is { State: UpdateCheckState.UpdateAvailable, Manifest: not null };
        CheckUpdatesButton.IsEnabled = !_isUpdateActionRunning;
        ApproveUpdateButton.Visibility = hasPendingUpdate ? Visibility.Visible : Visibility.Collapsed;
        ApproveUpdateButton.IsEnabled = !_isUpdateActionRunning && hasPendingUpdate;
    }

    private static Border CreateHeaderSurface()
    {
        var stack = new StackPanel
        {
            Spacing = 10
        };

        stack.Children.Add(new TextBlock
        {
            Text = "Pelicano",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = GetBrush("TextFillColorSecondaryBrush")
        });

        stack.Children.Add(new TextBlock
        {
            Text = "설정",
            Style = GetResource<Style>("TitleTextBlockStyle"),
            FontSize = 28
        });

        var border = CreateGlassCard();
        border.Padding = new Thickness(22, 20, 22, 20);
        border.Child = stack;
        return border;
    }

    private static Border CreateSectionCard(
        string title,
        string description,
        UIElement content)
    {
        var card = CreateGlassCard();
        card.Padding = new Thickness(18, 18, 18, 18);

        var stack = new StackPanel
        {
            Spacing = 10
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Style = GetResource<Style>("SubtitleTextBlockStyle"),
            FontSize = 20
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            stack.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GetBrush("TextFillColorSecondaryBrush"),
                FontSize = 13.2
            });
        }
        stack.Children.Add(content);
        card.Child = stack;
        return card;
    }

    private static Border CreateSettingsGroup(params UIElement[] rows)
    {
        var border = new Border
        {
            Background = GetBrush("PelicanoSurfaceBrush"),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22)
        };

        var stack = new StackPanel
        {
            Spacing = 0
        };

        for (var i = 0; i < rows.Length; i++)
        {
            stack.Children.Add(rows[i]);
            if (i < rows.Length - 1)
            {
                stack.Children.Add(CreateGroupDivider());
            }
        }

        border.Child = stack;
        return border;
    }

    private static Border CreateSettingRow(string title, string description, UIElement control)
    {
        var stack = new StackPanel
        {
            Spacing = 8
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Style = GetResource<Style>("BodyStrongTextBlockStyle") ?? GetResource<Style>("BodyTextBlockStyle"),
            FontSize = 15
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            stack.Children.Add(CreateSecondaryTextBlock(description));
        }
        stack.Children.Add(control);

        var border = new Border
        {
            Padding = new Thickness(18, 16, 18, 16)
        };
        border.Child = stack;
        return border;
    }

    private static Border CreateToggleRow(string title, string description, ToggleSwitch toggle)
    {
        var grid = new Grid
        {
            ColumnSpacing = 18
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel
        {
            Spacing = 4
        };
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            Style = GetResource<Style>("BodyStrongTextBlockStyle") ?? GetResource<Style>("BodyTextBlockStyle"),
            FontSize = 15
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            textStack.Children.Add(CreateSecondaryTextBlock(description));
        }

        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        toggle.VerticalAlignment = VerticalAlignment.Center;
        toggle.Margin = new Thickness(12, 0, 0, 0);

        Grid.SetColumn(textStack, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(textStack);
        grid.Children.Add(toggle);

        var border = new Border
        {
            Padding = new Thickness(18, 16, 18, 16)
        };
        border.Child = grid;
        return border;
    }

    private static Border CreateActionSurface(Button actionButton, string helperText)
    {
        var stack = new StackPanel
        {
            Spacing = 8
        };

        actionButton.HorizontalAlignment = HorizontalAlignment.Left;
        actionButton.VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(actionButton);
        if (!string.IsNullOrWhiteSpace(helperText))
        {
            stack.Children.Add(CreateSecondaryTextBlock(helperText));
        }

        var border = new Border
        {
            Padding = new Thickness(18, 16, 18, 16)
        };
        border.Child = stack;
        return border;
    }

    private static Border CreateGroupDivider()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(18, 0, 18, 0),
            Background = GetBrush("CardStrokeColorDefaultBrush"),
            Opacity = 0.42
        };
    }

    private static Border CreateSurfaceCard()
    {
        return new Border
        {
            Padding = new Thickness(18, 16, 18, 16),
            CornerRadius = new CornerRadius(22),
            Background = GetBrush("PelicanoSurfaceBrush"),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private static TextBlock CreateSecondaryTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetBrush("TextFillColorSecondaryBrush"),
            FontSize = 13.5
        };
    }

    private static ToggleSwitch CreateToggle()
    {
        return new ToggleSwitch
        {
            MinHeight = 44,
            MinWidth = 88,
            HorizontalAlignment = HorizontalAlignment.Left,
            OnContent = "켜짐",
            OffContent = "꺼짐"
        };
    }

    private static TextBox CreateAlignedTextBox(string placeholderText, double? width = null)
    {
        var textBox = new TextBox
        {
            MinHeight = 44,
            PlaceholderText = placeholderText,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 6, 12, 2),
            HorizontalAlignment = width.HasValue
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Stretch
        };

        if (width.HasValue)
        {
            textBox.Width = width.Value;
        }

        return textBox;
    }

    private static ComboBox CreateAlignedComboBox(string placeholderText)
    {
        return new ComboBox
        {
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = placeholderText,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 4, 12, 4)
        };
    }

    private static ComboBoxItem CreateThemeOption(string label, AppThemeMode themeMode)
    {
        return new ComboBoxItem
        {
            Content = label,
            Tag = themeMode
        };
    }

    private static ComboBoxItem CreateTextCellSizeOption(string label, int scalePercent)
    {
        return new ComboBoxItem
        {
            Content = $"{label} ({scalePercent}%)",
            Tag = scalePercent
        };
    }

    private static int GetThemeModeIndex(AppThemeMode themeMode)
    {
        return themeMode switch
        {
            AppThemeMode.Light => 1,
            AppThemeMode.Dark => 2,
            _ => 0
        };
    }

    private static int GetTextCellSizeIndex(int scalePercent)
    {
        var clampedScale = Math.Clamp(
            scalePercent,
            AppSettings.MinimumTextCellScalePercent,
            AppSettings.MaximumTextCellScalePercent);

        var closestIndex = 0;
        var smallestDistance = int.MaxValue;

        for (var i = 0; i < TextCellScalePresets.Length; i += 1)
        {
            var distance = Math.Abs(TextCellScalePresets[i] - clampedScale);
            if (distance >= smallestDistance)
            {
                continue;
            }

            smallestDistance = distance;
            closestIndex = i;
        }

        return closestIndex;
    }

    private AppThemeMode GetSelectedThemeMode()
    {
        if (ThemeModeBox.SelectedItem is ComboBoxItem item &&
            item.Tag is AppThemeMode themeMode)
        {
            return themeMode;
        }

        return AppThemeMode.System;
    }

    private int GetSelectedTextCellScalePercent()
    {
        if (TextCellSizeBox.SelectedItem is ComboBoxItem item &&
            item.Tag is int scalePercent)
        {
            return Math.Clamp(
                scalePercent,
                AppSettings.MinimumTextCellScalePercent,
                AppSettings.MaximumTextCellScalePercent);
        }

        return AppSettings.DefaultTextCellScalePercent;
    }

    private Border CreateStatusBanner(out TextBlock messageText)
    {
        messageText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        return new Border
        {
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(18),
            Background = CreateStatusBackground(InfoBarSeverity.Informational),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Child = messageText
        };
    }

    private static Brush CreateStatusBackground(InfoBarSeverity severity)
    {
        var isDark = IsDarkThemeActive();

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
                ?? CreateFallbackBrush(key);
        }
        catch
        {
            return CreateFallbackBrush(key);
        }
    }

    private static T? GetResource<T>(string key) where T : class
    {
        try
        {
            return Application.Current.Resources.ContainsKey(key)
                ? Application.Current.Resources[key] as T
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static Brush CreateFallbackBrush(string key)
    {
        var isDark = IsDarkThemeActive();

        return key switch
        {
            "PelicanoPanelBrush" => new SolidColorBrush(isDark
                ? Color.FromArgb(214, 19, 23, 30)
                : Color.FromArgb(244, 255, 255, 255)),
            "PelicanoSurfaceBrush" => new SolidColorBrush(isDark
                ? Color.FromArgb(228, 31, 38, 48)
                : Color.FromArgb(252, 255, 255, 255)),
            "CardStrokeColorDefaultBrush" => new SolidColorBrush(isDark
                ? Color.FromArgb(58, 255, 255, 255)
                : Color.FromArgb(32, 18, 28, 45)),
            "AccentFillColorDefaultBrush" => new SolidColorBrush(isDark
                ? Color.FromArgb(255, 97, 123, 255)
                : Color.FromArgb(255, 41, 95, 255)),
            "AccentFillColorSecondaryBrush" => new SolidColorBrush(isDark
                ? Color.FromArgb(62, 97, 123, 255)
                : Color.FromArgb(34, 41, 95, 255)),
            "AccentTextFillColorPrimaryBrush" => new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            "AccentStrokeColorOuterBrush" => new SolidColorBrush(isDark
                ? Color.FromArgb(82, 97, 123, 255)
                : Color.FromArgb(48, 41, 95, 255)),
            "TextFillColorPrimaryBrush" => new SolidColorBrush(isDark
                ? Color.FromArgb(255, 245, 247, 250)
                : Color.FromArgb(255, 24, 28, 34)),
            "TextFillColorSecondaryBrush" => new SolidColorBrush(isDark
                ? Color.FromArgb(255, 182, 191, 202)
                : Color.FromArgb(255, 94, 102, 114)),
            _ => new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
        };
    }

    private static bool IsDarkThemeActive()
    {
        try
        {
            if (Application.Current.Resources.ContainsKey("PelicanoThemeIsDark") &&
                Application.Current.Resources["PelicanoThemeIsDark"] is bool isDark)
            {
                return isDark;
            }

            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
        catch
        {
            return false;
        }
    }
}
