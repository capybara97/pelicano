namespace Spaste.Models;

/// <summary>
/// 사용자가 조정할 수 있는 Spaste 설정 모델이다.
/// JSON 저장을 전제로 두고 기본값을 즉시 실행 가능한 보수적 설정으로 채운다.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 복사 직후 서식을 제거하고 항상 일반 텍스트로 다시 설정할지 여부다.
    /// </summary>
    public bool EnforcePlainTextOnly { get; set; } = true;

    /// <summary>
    /// 탐색기 파일 복사와 비트맵 이미지를 히스토리에 포함할지 여부다.
    /// </summary>
    public bool CaptureImages { get; set; } = true;

    /// <summary>
    /// 감사 로그를 남길지 여부다.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;

    /// <summary>
    /// Windows 시작 시 자동 실행할지 여부다.
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// 다크 모드를 사용할지 여부다.
    /// </summary>
    public bool DarkMode { get; set; }

    /// <summary>
    /// 이전 버전 호환성을 위한 자리만 남긴 값이다.
    /// </summary>
    public bool EnableMarkdownButtons { get; set; }

    /// <summary>
    /// 최대 보관 히스토리 개수다.
    /// </summary>
    public int MaxHistoryItems { get; set; } = 200;

    /// <summary>
    /// 현재 인스턴스를 수정용 복사본으로 반환한다.
    /// </summary>
    public AppSettings Clone()
    {
        return new AppSettings
        {
            EnforcePlainTextOnly = EnforcePlainTextOnly,
            CaptureImages = CaptureImages,
            EnableAuditLogging = EnableAuditLogging,
            StartWithWindows = StartWithWindows,
            DarkMode = DarkMode,
            EnableMarkdownButtons = EnableMarkdownButtons,
            MaxHistoryItems = MaxHistoryItems
        };
    }
}
