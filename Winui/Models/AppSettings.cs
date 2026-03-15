namespace Pelicano.Models;

/// <summary>
/// 사용자가 조정할 수 있는 Pelicano 설정 모델이다.
/// JSON 저장을 전제로 두고 기본값을 즉시 실행 가능한 보수적 설정으로 채운다.
/// </summary>
public sealed class AppSettings
{
    public const int DefaultTextCellScalePercent = 100;
    public const int MinimumTextCellScalePercent = 100;
    public const int MaximumTextCellScalePercent = 145;

    /// <summary>
    /// 앱 테마 적용 방식이다.
    /// </summary>
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    /// <summary>
    /// Pelicano에서 다시 복사할 때 일반 텍스트만 출력할지 여부다.
    /// </summary>
    public bool EnforcePlainTextOnly { get; set; } = true;

    /// <summary>
    /// 탐색기 파일/폴더 복사 경로를 히스토리에 포함할지 여부다.
    /// </summary>
    public bool CaptureFileDrops { get; set; } = true;

    /// <summary>
    /// 비트맵 이미지를 히스토리에 포함할지 여부다.
    /// </summary>
    public bool CaptureImages { get; set; } = true;

    /// <summary>
    /// 클립보드 감시를 일시 중지할지 여부다.
    /// </summary>
    public bool CapturePaused { get; set; }

    /// <summary>
    /// 감사 로그를 남길지 여부다.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;

    /// <summary>
    /// Windows 시작 시 자동 실행할지 여부다.
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// 최대 보관 히스토리 개수다.
    /// </summary>
    public int MaxHistoryItems { get; set; } = 200;

    /// <summary>
    /// 텍스트 히스토리 셀 확대 비율이다.
    /// </summary>
    public int TextCellScalePercent { get; set; } = DefaultTextCellScalePercent;

    /// <summary>
    /// 0보다 크면 지정한 분보다 오래된 항목을 자동 만료한다.
    /// </summary>
    public int AutoExpireMinutes { get; set; }

    /// <summary>
    /// 현재 인스턴스를 수정용 복사본으로 반환한다.
    /// </summary>
    public AppSettings Clone()
    {
        return new AppSettings
        {
            ThemeMode = ThemeMode,
            EnforcePlainTextOnly = EnforcePlainTextOnly,
            CaptureFileDrops = CaptureFileDrops,
            CaptureImages = CaptureImages,
            CapturePaused = CapturePaused,
            EnableAuditLogging = EnableAuditLogging,
            StartWithWindows = StartWithWindows,
            MaxHistoryItems = MaxHistoryItems,
            TextCellScalePercent = TextCellScalePercent,
            AutoExpireMinutes = AutoExpireMinutes
        };
    }
}
