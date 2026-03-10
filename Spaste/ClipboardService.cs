using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Spaste;

/// <summary>
/// WinForms Clipboard API를 안전하게 호출하는 정적 헬퍼다.
/// 다른 프로세스가 잠깐 클립보드를 점유하는 상황을 고려해 짧은 재시도를 포함한다.
/// </summary>
internal static class ClipboardService
{
    /// <summary>
    /// 일반 텍스트를 읽고, 원본 클립보드에 RTF/HTML이 섞여 있었는지도 함께 반환한다.
    /// </summary>
    public static bool TryGetText([NotNullWhen(true)] out string? text, out bool hasRichFormatting)
    {
        string? localText = null;
        var localHasRichFormatting = false;
        var success = ExecuteWithRetry(() =>
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
            {
                return false;
            }

            localText = Clipboard.GetText(TextDataFormat.UnicodeText);
            localHasRichFormatting =
                Clipboard.ContainsData(DataFormats.Html) || Clipboard.ContainsData(DataFormats.Rtf);

            return !string.IsNullOrWhiteSpace(localText);
        });

        text = localText;
        hasRichFormatting = localHasRichFormatting;
        return success;
    }

    /// <summary>
    /// 탐색기 등에서 복사된 파일/폴더 드롭 리스트를 읽는다.
    /// </summary>
    public static bool TryGetFileDropList(out List<string> filePaths)
    {
        var localFilePaths = new List<string>();
        var success = ExecuteWithRetry(() =>
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return false;
            }

            localFilePaths = Clipboard.GetFileDropList()
                .Cast<string>()
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .ToList();

            return localFilePaths.Count > 0;
        });

        filePaths = localFilePaths;
        return success;
    }

    /// <summary>
    /// 비트맵 이미지를 읽어 파일 저장 가능한 Bitmap 복사본으로 반환한다.
    /// </summary>
    public static bool TryGetImage([NotNullWhen(true)] out Bitmap? bitmap)
    {
        Bitmap? localBitmap = null;
        var success = ExecuteWithRetry(() =>
        {
            if (!Clipboard.ContainsImage())
            {
                return false;
            }

            using var sourceImage = Clipboard.GetImage();

            if (sourceImage is null)
            {
                return false;
            }

            localBitmap = new Bitmap(sourceImage);
            return true;
        });

        bitmap = localBitmap;
        return success;
    }

    /// <summary>
    /// 내부 처리 결과를 일반 텍스트로 다시 클립보드에 쓴다.
    /// </summary>
    public static void SetText(string text)
    {
        ExecuteWrite(() => Clipboard.SetText(text));
    }

    /// <summary>
    /// 저장된 PNG 파일을 다시 비트맵으로 읽어 클립보드에 쓴다.
    /// </summary>
    public static void SetImageFromFile(string imagePath)
    {
        ExecuteWrite(() =>
        {
            using var bitmap = new Bitmap(imagePath);
            using var clone = new Bitmap(bitmap);
            Clipboard.SetImage(clone);
        });
    }

    /// <summary>
    /// 파일 드롭 리스트를 다시 클립보드에 쓴다.
    /// </summary>
    public static void SetFileDropList(IEnumerable<string> filePaths)
    {
        ExecuteWrite(() =>
        {
            var collection = new StringCollection();

            foreach (var filePath in filePaths)
            {
                collection.Add(filePath);
            }

            Clipboard.SetFileDropList(collection);
        });
    }

    /// <summary>
    /// 읽기 작업을 재시도 정책과 함께 실행한다.
    /// </summary>
    private static bool ExecuteWithRetry(Func<bool> action)
    {
        for (var attempt = 0; attempt < 8; attempt += 1)
        {
            try
            {
                return action();
            }
            catch (ExternalException)
            {
                Thread.Sleep(50);
            }
            catch (InvalidOperationException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    /// <summary>
    /// 쓰기 작업을 재시도 정책과 함께 실행하고, 끝내 실패하면 예외를 올린다.
    /// </summary>
    private static void ExecuteWrite(Action action)
    {
        for (var attempt = 0; attempt < 8; attempt += 1)
        {
            try
            {
                action();
                return;
            }
            catch (ExternalException) when (attempt < 7)
            {
                Thread.Sleep(50);
            }
            catch (InvalidOperationException) when (attempt < 7)
            {
                Thread.Sleep(50);
            }
        }

        action();
    }
}
