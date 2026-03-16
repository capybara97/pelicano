using System.ComponentModel;
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
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            throw new FileNotFoundException("업데이트 설치 파일을 찾지 못했습니다.", installerPath);
        }

        if (TryLaunchInstallerNow(installerPath))
        {
            return;
        }

        var installerDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory;
        var script =
            $"$installerPath = '{EscapeSingleQuoted(installerPath)}'{Environment.NewLine}" +
            $"$installerDirectory = '{EscapeSingleQuoted(installerDirectory)}'{Environment.NewLine}" +
            $"$pidToWait = {processId}{Environment.NewLine}" +
            "try { Wait-Process -Id $pidToWait -ErrorAction Stop } catch { }" + Environment.NewLine +
            "Start-Sleep -Milliseconds 800" + Environment.NewLine +
            "Start-Process -FilePath $installerPath -WorkingDirectory $installerDirectory -ArgumentList '/SP-','/CLOSEAPPLICATIONS','/NORESTART'";

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = GetPowerShellExecutablePath(),
            Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("업데이트 설치 프로세스를 시작하지 못했습니다.");
        }

        if (process.WaitForExit(500))
        {
            throw new InvalidOperationException(
                $"업데이트 설치 예약 프로세스가 즉시 종료되었습니다. 종료 코드: {process.ExitCode}");
        }
    }

    private static bool TryLaunchInstallerNow(string installerPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SP- /CLOSEAPPLICATIONS /NORESTART",
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            // 설치기가 바로 종료되면 시작 실패로 보고 종료 후 helper 경로로 되돌린다.
            return !process.WaitForExit(1500);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("업데이트 설치 실행이 취소되었습니다.", exception);
        }
    }

    private static string GetPowerShellExecutablePath()
    {
        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        return File.Exists(windowsPowerShell)
            ? windowsPowerShell
            : "powershell.exe";
    }

    /// <summary>
    /// PowerShell 단일 인용 문자열 안에서 경로를 안전하게 이스케이프한다.
    /// </summary>
    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
