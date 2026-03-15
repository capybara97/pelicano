namespace Pelicano;

/// <summary>
/// 표시용 버전 문자열과 비교용 버전 값을 한 곳에서 관리한다.
/// </summary>
internal static class AppVersionInfo
{
    private static readonly Version CachedCurrentVersion = ParseOrDefault(
        System.Diagnostics.FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? string.Empty).ProductVersion);

    /// <summary>
    /// 현재 실행 중인 Pelicano의 정규화된 버전이다.
    /// </summary>
    public static Version CurrentVersion => CachedCurrentVersion;

    /// <summary>
    /// UI에 표시하기 좋은 현재 버전 문자열이다.
    /// </summary>
    public static string CurrentVersionText => ToDisplayString(CachedCurrentVersion);

    /// <summary>
    /// 문자열 버전을 4단계 숫자 버전으로 정규화해 비교 가능하게 만든다.
    /// </summary>
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var separatorIndex = normalized.IndexOfAny(['-', '+']);

        if (separatorIndex >= 0)
        {
            normalized = normalized[..separatorIndex];
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length is 0 or > 4)
        {
            return false;
        }

        var numbers = new int[4];

        for (var index = 0; index < parts.Length; index += 1)
        {
            if (!int.TryParse(parts[index], out numbers[index]) || numbers[index] < 0)
            {
                return false;
            }
        }

        version = parts.Length switch
        {
            1 => new Version(numbers[0], 0),
            2 => new Version(numbers[0], numbers[1]),
            3 => new Version(numbers[0], numbers[1], numbers[2]),
            _ => new Version(numbers[0], numbers[1], numbers[2], numbers[3])
        };
        return true;
    }

    /// <summary>
    /// 버전 객체를 사용자가 읽기 좋은 길이로 축약해 표시한다.
    /// </summary>
    public static string ToDisplayString(Version version)
    {
        if (version.Revision > 0)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        if (version.Build > 0)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        return $"{version.Major}.{version.Minor}";
    }

    /// <summary>
    /// 파싱 실패 시 0.0 버전으로 안전하게 내려간다.
    /// </summary>
    private static Version ParseOrDefault(string? value)
    {
        return TryParse(value, out var version)
            ? version
            : new Version(0, 0, 0, 0);
    }
}
