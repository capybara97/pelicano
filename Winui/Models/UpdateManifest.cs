namespace Pelicano.Models;

/// <summary>
/// 원격 매니페스트에서 해석한 Pelicano 업데이트 정보다.
/// </summary>
internal sealed class UpdateManifest
{
    public string VersionText { get; init; } = string.Empty;
    public Version Version { get; init; } = new(0, 0, 0, 0);
    public string InstallerUrl { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
}

/// <summary>
/// 업데이트 확인 결과 상태 코드다.
/// </summary>
internal enum UpdateCheckState
{
    UpToDate,
    UpdateAvailable,
    Failed
}

/// <summary>
/// 최신 버전 확인 결과와 사용자용 메시지를 함께 담는다.
/// </summary>
internal sealed class UpdateCheckResult
{
    public UpdateCheckState State { get; init; }
    public Version CurrentVersion { get; init; } = new(0, 0, 0, 0);
    public Version? LatestVersion { get; init; }
    public UpdateManifest? Manifest { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 다운로드가 끝난 설치 파일 정보를 나타낸다.
/// </summary>
internal sealed class DownloadedUpdatePackage
{
    public string InstallerPath { get; init; } = string.Empty;
    public Version Version { get; init; } = new(0, 0, 0, 0);
    public string Sha256 { get; init; } = string.Empty;
}

/// <summary>
/// 업데이트 다운로드 진행률과 사용자 표시 문구를 함께 전달한다.
/// </summary>
internal readonly record struct UpdateProgressInfo(
    string Message,
    double? ProgressRatio,
    bool IsIndeterminate = false);
