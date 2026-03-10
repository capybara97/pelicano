using System.Text.Json;
using Spaste.Models;
using Spaste.Services;

namespace Spaste;

/// <summary>
/// 설정 JSON 로드/저장을 담당한다.
/// 외부 설정 라이브러리 없이도 가볍게 동작하도록 System.Text.Json만 사용한다.
/// </summary>
internal static class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 설정 파일을 읽어 AppSettings 인스턴스로 복원한다.
    /// 파일이 없거나 파싱에 실패하면 기본 설정을 반환한다.
    /// </summary>
    public static AppSettings Load(string path, Logger logger)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception exception)
        {
            logger.Error("설정 파일을 읽는 중 오류가 발생했다. 기본 설정으로 계속 진행한다.", exception);
            return new AppSettings();
        }
    }

    /// <summary>
    /// 현재 설정을 JSON 파일로 저장한다.
    /// </summary>
    public static void Save(string path, AppSettings settings, Logger logger)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception exception)
        {
            logger.Error("설정 파일 저장에 실패했다.", exception);
        }
    }
}
