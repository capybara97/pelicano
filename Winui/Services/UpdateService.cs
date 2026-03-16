using System.Security.Cryptography;
using System.Text.Json;
using Pelicano.Models;

namespace Pelicano.Services;

/// <summary>
/// 원격 매니페스트 조회와 설치 파일 다운로드를 담당한다.
/// </summary>
internal sealed class UpdateService
{
    internal const string ManifestUrl = "https://qfqxifaqnympxjbz.public.blob.vercel-storage.com/version.json";
    private const string DefaultInstallerUrl =
        "https://qfqxifaqnympxjbz.public.blob.vercel-storage.com/Pelicano-Installer.exe";
    private static readonly HttpClient ManifestHttpClient = CreateHttpClient(TimeSpan.FromSeconds(15));
    private static readonly HttpClient DownloadHttpClient = CreateHttpClient(TimeSpan.FromMinutes(20));
    private readonly Logger _logger;

    public UpdateService(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 하드코딩된 매니페스트 URL에서 최신 버전 정보를 내려받아 현재 버전과 비교한다.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (!TryCreateHttpUri(ManifestUrl, out var manifestUri))
        {
            return new UpdateCheckResult
            {
                State = UpdateCheckState.Failed,
                CurrentVersion = AppVersionInfo.CurrentVersion,
                Message = "업데이트 서버 주소가 올바르지 않습니다."
            };
        }

        try
        {
            using var response = await ManifestHttpClient.GetAsync(manifestUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);

            var manifest = ParseManifest(document.RootElement, manifestUri);
            var currentVersion = AppVersionInfo.CurrentVersion;

            if (manifest.Version <= currentVersion)
            {
                return new UpdateCheckResult
                {
                    State = UpdateCheckState.UpToDate,
                    CurrentVersion = currentVersion,
                    LatestVersion = manifest.Version,
                    Manifest = manifest,
                    Message = $"현재 최신 버전({AppVersionInfo.ToDisplayString(currentVersion)})을 사용 중입니다."
                };
            }

            return new UpdateCheckResult
            {
                State = UpdateCheckState.UpdateAvailable,
                CurrentVersion = currentVersion,
                LatestVersion = manifest.Version,
                Manifest = manifest,
                Message = $"새 버전 {manifest.VersionText}이(가) 준비되었습니다."
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            TaskCanceledException or
            JsonException or
            InvalidDataException)
        {
            _logger.Error("업데이트 매니페스트를 확인하는 중 오류가 발생했다.", exception);
            return new UpdateCheckResult
            {
                State = UpdateCheckState.Failed,
                CurrentVersion = AppVersionInfo.CurrentVersion,
                Message = $"업데이트 확인에 실패했습니다. {exception.Message}"
            };
        }
    }

    /// <summary>
    /// 원격 설치 파일을 앱 데이터 폴더에 다운로드하고 해시를 검증한다.
    /// </summary>
    public async Task<DownloadedUpdatePackage> DownloadInstallerAsync(
        UpdateManifest manifest,
        IProgress<UpdateProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        if (!TryCreateHttpUri(manifest.InstallerUrl, out var installerUri))
        {
            throw new InvalidDataException("설치 파일 URL 형식이 올바르지 않습니다.");
        }

        Directory.CreateDirectory(AppPaths.UpdatesRoot);

        var extension = Path.GetExtension(installerUri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".exe";
        }

        var versionToken = AppVersionInfo.ToDisplayString(manifest.Version).Replace(' ', '_');
        var finalPath = Path.Combine(AppPaths.UpdatesRoot, $"Pelicano-{versionToken}-Installer{extension}");
        var tempPath = Path.Combine(AppPaths.UpdatesRoot, $"{Guid.NewGuid():N}.download");

        try
        {
            if (File.Exists(finalPath) && !string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                progress?.Report(new UpdateProgressInfo("기존 설치 파일 무결성을 확인하는 중입니다...", null, true));
                var cachedHash = await ComputeFileSha256Async(finalPath, cancellationToken);
                if (string.Equals(cachedHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new UpdateProgressInfo("이미 내려받은 최신 설치 파일을 사용합니다.", 1d));
                    return new DownloadedUpdatePackage
                    {
                        InstallerPath = finalPath,
                        Version = manifest.Version,
                        Sha256 = cachedHash
                    };
                }
            }

            using var response = await DownloadHttpClient.GetAsync(
                installerUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long receivedBytes = 0;

            progress?.Report(new UpdateProgressInfo(
                "업데이트 설치 파일을 다운로드하는 중입니다...",
                totalBytes.HasValue && totalBytes.Value > 0 ? 0d : null,
                !totalBytes.HasValue || totalBytes.Value <= 0));

            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                while (true)
                {
                    var bytesRead = await input.ReadAsync(buffer, cancellationToken);
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    hash.AppendData(buffer, 0, bytesRead);
                    receivedBytes += bytesRead;

                    progress?.Report(BuildDownloadProgress(receivedBytes, totalBytes));
                }

                await output.FlushAsync(cancellationToken);
            }

            var actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            progress?.Report(new UpdateProgressInfo("설치 파일 무결성을 확인하는 중입니다...", 1d));

            if (!string.IsNullOrWhiteSpace(manifest.Sha256) &&
                !string.Equals(actualSha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("다운로드한 설치 파일 해시가 매니페스트와 일치하지 않습니다.");
            }

            File.Move(tempPath, finalPath, overwrite: true);
            progress?.Report(new UpdateProgressInfo("업데이트 준비를 마쳤습니다.", 1d));
            return new DownloadedUpdatePackage
            {
                InstallerPath = finalPath,
                Version = manifest.Version,
                Sha256 = actualSha256
            };
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    /// <summary>
    /// 매니페스트 JSON에서 주요 필드를 유연하게 읽어 업데이트 모델로 변환한다.
    /// </summary>
    private static UpdateManifest ParseManifest(JsonElement root, Uri manifestUri)
    {
        var versionText = GetString(root, "version", "latestVersion", "latest_version", "tag");
        if (!AppVersionInfo.TryParse(versionText, out var version))
        {
            throw new InvalidDataException("매니페스트의 version 값이 비어 있거나 형식이 올바르지 않습니다.");
        }

        var installerUrlText = GetString(
            root,
            "installerUrl",
            "installer_url",
            "downloadUrl",
            "download_url",
            "url");

        if (string.IsNullOrWhiteSpace(installerUrlText))
        {
            installerUrlText = DefaultInstallerUrl;
        }

        var installerUri = Uri.TryCreate(installerUrlText, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(manifestUri, installerUrlText);

        return new UpdateManifest
        {
            VersionText = AppVersionInfo.ToDisplayString(version),
            Version = version,
            InstallerUrl = installerUri.ToString(),
            Sha256 = NormalizeHash(GetString(root, "sha256", "sha256Hex", "sha256_hex", "checksum")),
            ReleaseNotes = GetString(root, "releaseNotes", "release_notes", "notes", "body", "description")
        };
    }

    /// <summary>
    /// 후보 이름 목록 중 처음으로 매칭되는 문자열 값을 반환한다.
    /// </summary>
    private static string GetString(JsonElement root, params string[] propertyNames)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!propertyNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.ToString(),
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    /// <summary>
    /// 캐시 검증용 SHA-256 해시를 파일에서 계산한다.
    /// </summary>
    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[81920];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead <= 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// 해시 문자열에서 공백과 하이픈을 제거해 비교 가능한 소문자로 정규화한다.
    /// </summary>
    private static string NormalizeHash(string hash)
    {
        return hash
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    /// <summary>
    /// 절대 http/https URL인지 확인한다.
    /// </summary>
    private static bool TryCreateHttpUri(string value, out Uri uri)
    {
        uri = null!;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        if (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        uri = parsedUri;
        return true;
    }

    /// <summary>
    /// 바이트 기반 다운로드 상태를 진행률 UI 친화적인 구조로 바꾼다.
    /// </summary>
    private static UpdateProgressInfo BuildDownloadProgress(long receivedBytes, long? totalBytes)
    {
        if (totalBytes.HasValue && totalBytes.Value > 0)
        {
            var progressRatio = Math.Clamp((double)receivedBytes / totalBytes.Value, 0d, 1d);
            return new UpdateProgressInfo(
                $"업데이트 다운로드 중... {Math.Round(progressRatio * 100)}%",
                progressRatio);
        }

        return new UpdateProgressInfo(
            $"업데이트 다운로드 중... {FormatBytes(receivedBytes)}",
            null,
            true);
    }

    private static string FormatBytes(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB" };
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex += 1;
        }

        return unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.0} {units[unitIndex]}";
    }

    /// <summary>
    /// 업데이트 요청용 HttpClient를 한 번만 초기화한다.
    /// </summary>
    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient
        {
            Timeout = timeout
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Pelicano-Updater");
        return client;
    }
}
