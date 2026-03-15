using Microsoft.Win32;
using Pelicano.Services;

namespace Pelicano;

/// <summary>
/// 현재 사용자 기준 시작 프로그램 등록을 관리한다.
/// 관리자 권한 없이도 동작하도록 HKCU Run 키만 사용한다.
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Pelicano";

    /// <summary>
    /// 설정 값에 따라 자동 시작 등록을 추가하거나 제거한다.
    /// </summary>
    public static void Apply(bool enabled, Logger logger)
    {
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                runKey?.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                runKey?.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception exception)
        {
            logger.Error("자동 시작 레지스트리 갱신에 실패했다.", exception);
        }
    }
}
