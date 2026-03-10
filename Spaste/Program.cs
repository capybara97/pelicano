using System.Text;

namespace Spaste;

/// <summary>
/// Spaste의 프로세스 진입점이다.
/// WinForms 앱은 STA 스레드가 필수이므로 메인 스레드에서 앱 컨텍스트를 바로 실행한다.
/// </summary>
internal static class Program
{
    /// <summary>
    /// 앱 시작 시 고 DPI, 기본 폰트, 예외 처리 흐름을 초기화한 뒤
    /// 트레이 기반 애플리케이션 컨텍스트를 실행한다.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new SpasteApplicationContext());
        }
        catch (Exception exception)
        {
            ReportStartupFailure(exception);
            MessageBox.Show(
                "Pelicano가 시작 중 오류로 종료되었습니다.\r\n로그 파일을 확인해 주세요.",
                "Pelicano",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 시작 단계 예외를 로컬 로그 파일에 남겨 무반응 종료를 추적 가능하게 한다.
    /// </summary>
    private static void ReportStartupFailure(Exception exception)
    {
        try
        {
            var dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Pelicano");
            var logsRoot = Path.Combine(dataRoot, "logs");
            Directory.CreateDirectory(logsRoot);
            var crashPath = Path.Combine(logsRoot, $"{DateTime.Now:yyyyMMdd}.startup.log");
            var content =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [FATAL] Startup failure{Environment.NewLine}{exception}{Environment.NewLine}";
            File.AppendAllText(crashPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
        }
    }
}
