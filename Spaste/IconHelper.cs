namespace Spaste;

/// <summary>
/// 앱 아이콘 로딩을 담당한다.
/// ICO가 있으면 우선 사용하고, 없으면 PNG를 임시 HICON으로 변환해 트레이 아이콘으로 사용한다.
/// </summary>
internal static class IconHelper
{
    /// <summary>
    /// 현재 실행 환경에서 사용 가능한 앱 아이콘을 반환한다.
    /// </summary>
    public static Icon LoadApplicationIcon()
    {
        if (File.Exists(Application.ExecutablePath))
        {
            using var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            if (executableIcon is not null)
            {
                return (Icon)executableIcon.Clone();
            }
        }

        if (File.Exists(AppPaths.AppIcoPath))
        {
            return new Icon(AppPaths.AppIcoPath);
        }

        if (File.Exists(AppPaths.AppPngPath))
        {
            return LoadIconFromPng(AppPaths.AppPngPath);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    /// <summary>
    /// PNG 파일에서 임시 HICON을 생성한 뒤 복제본을 반환한다.
    /// </summary>
    private static Icon LoadIconFromPng(string pngPath)
    {
        using var bitmap = new Bitmap(pngPath);
        var handle = bitmap.GetHicon();

        try
        {
            using var temporaryIcon = Icon.FromHandle(handle);
            return (Icon)temporaryIcon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
