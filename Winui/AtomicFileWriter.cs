using System.Text;

namespace Pelicano;

/// <summary>
/// 중요한 설정/키 파일을 임시 파일을 거쳐 원자적으로 교체한다.
/// </summary>
internal static class AtomicFileWriter
{
    public static void WriteAllText(string path, string content, Encoding? encoding = null)
    {
        var writerEncoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        WriteAllBytes(path, writerEncoding.GetBytes(content));
    }

    public static void WriteAllBytes(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            directory ?? AppDomain.CurrentDomain.BaseDirectory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        File.WriteAllBytes(tempPath, content);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }
}
