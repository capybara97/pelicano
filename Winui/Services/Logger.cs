using System.Text;

namespace Pelicano.Services;

/// <summary>
/// 로컬 파일 기반 로그 기록기다.
/// 네트워크를 사용하지 않고 일별 로그 파일에 운영/감사 로그를 축적한다.
/// </summary>
public sealed class Logger
{
    private readonly string _logDirectory;
    private readonly Func<bool>? _auditEnabledProvider;
    private readonly object _syncRoot = new();

    /// <summary>
    /// 로그 기록기를 초기화한다.
    /// </summary>
    /// <param name="logDirectory">로그가 저장될 폴더다.</param>
    /// <param name="auditEnabledProvider">감사 로그 사용 여부를 동적으로 확인하는 콜백이다.</param>
    public Logger(string logDirectory, Func<bool>? auditEnabledProvider = null)
    {
        _logDirectory = logDirectory;
        _auditEnabledProvider = auditEnabledProvider;
        Directory.CreateDirectory(_logDirectory);
    }

    /// <summary>
    /// 일반 운영 로그를 기록한다.
    /// </summary>
    public void Info(string message)
    {
        WriteLine("INFO", message);
    }

    /// <summary>
    /// 예외를 포함한 오류 로그를 기록한다.
    /// </summary>
    public void Error(string message, Exception exception)
    {
        WriteLine("ERROR", $"{message}{Environment.NewLine}{exception}");
    }

    /// <summary>
    /// 보안팀 추적용 감사 로그를 기록한다.
    /// </summary>
    public void Audit(string action, string details)
    {
        if (!(_auditEnabledProvider?.Invoke() ?? true))
        {
            return;
        }

        WriteLine("AUDIT", $"{action} | {details}");
    }

    /// <summary>
    /// 실제 파일 쓰기를 수행한다.
    /// </summary>
    private void WriteLine(string level, string message)
    {
        lock (_syncRoot)
        {
            var path = Path.Combine(_logDirectory, $"{DateTime.Now:yyyyMMdd}.log");
            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

            File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
