namespace Spaste;

/// <summary>
/// Pelicano가 실행 중 참조하는 경로를 한곳에서 관리한다.
/// 사용자별 로컬 저장소는 %APPDATA%\Pelicano 아래로 고정해 보안팀 요구사항을 맞춘다.
/// </summary>
internal static class AppPaths
{
    private static readonly string LegacyDataRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spaste");

    /// <summary>
    /// 사용자별 앱 데이터 루트 경로다.
    /// </summary>
    public static readonly string DataRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pelicano");

    /// <summary>
    /// 클립보드 이미지 저장 폴더다.
    /// </summary>
    public static readonly string ImagesRoot = Path.Combine(DataRoot, "images");

    /// <summary>
    /// 감사 및 장애 로그 저장 폴더다.
    /// </summary>
    public static readonly string LogsRoot = Path.Combine(DataRoot, "logs");

    /// <summary>
    /// 앱 설정 JSON 파일 경로다.
    /// </summary>
    public static readonly string SettingsPath = Path.Combine(DataRoot, "settings.json");

    /// <summary>
    /// 히스토리 DB 파일 경로다.
    /// </summary>
    public static readonly string DatabasePath = Path.Combine(DataRoot, "history.db");

    /// <summary>
    /// 배포된 실행 파일 기준 PNG 아이콘 경로다.
    /// </summary>
    public static readonly string AppPngPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.png");

    /// <summary>
    /// 배포된 실행 파일 기준 ICO 아이콘 경로다.
    /// </summary>
    public static readonly string AppIcoPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");

    /// <summary>
    /// 앱이 직접 관리하는 저장 이미지 경로인지 확인한다.
    /// DB 변조 등으로 외부 파일을 삭제/로드하는 일을 막는다.
    /// </summary>
    public static bool IsManagedImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var imagesRoot = Path.GetFullPath(ImagesRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            return fullPath.StartsWith(imagesRoot, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// AppData 하위 필수 폴더를 미리 생성한다.
    /// </summary>
    public static void EnsureDirectories()
    {
        TryMigrateLegacyData();
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(ImagesRoot);
        Directory.CreateDirectory(LogsRoot);
    }

    /// <summary>
    /// 이전 Spaste 데이터 폴더가 있으면 Pelicano 폴더로 1회 이관한다.
    /// </summary>
    private static void TryMigrateLegacyData()
    {
        try
        {
            if (!Directory.Exists(LegacyDataRoot) || Directory.Exists(DataRoot))
            {
                return;
            }

            Directory.Move(LegacyDataRoot, DataRoot);
        }
        catch
        {
        }
    }
}
