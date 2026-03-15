using System.Diagnostics;
using System.Text;

namespace Pelicano.Services;

/// <summary>
/// 현재 프로세스가 종료된 뒤 설치 파일을 실행하도록 별도 PowerShell 프로세스를 띄운다.
/// </summary>
internal static class UpdateInstallerLauncher
{
    public static void LaunchAfterExit(string installerPath, int processId)
    {
        var script =
            $"$installerPath = '{EscapeSingleQuoted(installerPath)}'{Environment.NewLine}" +
            $"$pidToWait = {processId}{Environment.NewLine}" +
            "try { Wait-Process -Id $pidToWait -ErrorAction Stop } catch { }" + Environment.NewLine +
            "Start-Sleep -Milliseconds 800" + Environment.NewLine +
            "Start-Process -FilePath $installerPath -ArgumentList '/SP-','/CLOSEAPPLICATIONS','/NORESTART'";

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("업데이트 설치 프로세스를 시작하지 못했습니다.");
        }
    }

    /// <summary>
    /// PowerShell 단일 인용 문자열 안에서 경로를 안전하게 이스케이프한다.
    /// </summary>
    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
