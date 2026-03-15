using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Pelicano;

/// <summary>
/// WinRT Clipboard API를 안전하게 호출하는 정적 헬퍼다.
/// 다른 프로세스가 잠깐 클립보드를 점유하는 상황을 고려해 짧은 재시도를 포함한다.
/// </summary>
internal static class ClipboardService
{
    private static readonly Regex HtmlLineBreakRegex = new(
        @"<(?:br\s*/?|/p|/div|/li|/tr|/h[1-6])>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HtmlListItemRegex = new(
        @"<li\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HtmlTagRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ExtraBlankLineRegex = new(
        @"\n{3,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 일반 텍스트를 읽고, 원본 클립보드에 RTF/HTML이 섞여 있었는지도 함께 반환한다.
    /// </summary>
    public static bool TryGetText([NotNullWhen(true)] out string? text, out bool hasRichFormatting)
    {
        string? localText = null;
        var localHasRichFormatting = false;
        var success = ExecuteWithRetry(() =>
        {
            var content = Clipboard.GetContent();
            localHasRichFormatting =
                content.Contains(StandardDataFormats.Html) || content.Contains(StandardDataFormats.Rtf);

            if (content.Contains(StandardDataFormats.Text))
            {
                var textValue = content.GetTextAsync().AsTask().GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(textValue))
                {
                    localText = textValue;
                    return true;
                }
            }

            // WebView/Electron 계열 앱은 텍스트 없이 HTML fragment만 올리는 경우가 있다.
            if (TryGetTextFromHtml(content, out localText))
            {
                localHasRichFormatting = true;
                return true;
            }

            if (TryGetTextFromWebLink(content, out localText))
            {
                return true;
            }

            return false;
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
            var content = Clipboard.GetContent();

            if (!content.Contains(StandardDataFormats.StorageItems))
            {
                return false;
            }

            localFilePaths = content.GetStorageItemsAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult()
                .Select(item => item.Path)
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .ToList();

            return localFilePaths.Count > 0;
        });

        filePaths = localFilePaths;
        return success;
    }

    /// <summary>
    /// 비트맵 이미지를 PNG 바이트와 크기 정보로 반환한다.
    /// </summary>
    public static bool TryGetImage([NotNullWhen(true)] out ClipboardImageData? image)
    {
        ClipboardImageData? localImage = null;
        var success = ExecuteWithRetry(() =>
        {
            var content = Clipboard.GetContent();

            if (!content.Contains(StandardDataFormats.Bitmap))
            {
                return false;
            }

            var bitmapReference = content.GetBitmapAsync().AsTask().GetAwaiter().GetResult();
            using var sourceStream = bitmapReference.OpenReadAsync().AsTask().GetAwaiter().GetResult();
            var pngBytes = ConvertToPngBytes(sourceStream, out var width, out var height);

            if (pngBytes.Length == 0)
            {
                return false;
            }

            localImage = new ClipboardImageData(pngBytes, width, height);
            return true;
        });

        image = localImage;
        return success;
    }

    /// <summary>
    /// 내부 처리 결과를 일반 텍스트로 다시 클립보드에 쓴다.
    /// </summary>
    public static void SetText(string text)
    {
        ExecuteWrite(() =>
        {
            var package = CreatePackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        });
    }

    /// <summary>
    /// 저장된 PNG 파일을 비트맵으로 다시 클립보드에 쓴다.
    /// </summary>
    public static void SetImageFromFile(string imagePath)
    {
        ExecuteWrite(() =>
        {
            var file = StorageFile.GetFileFromPathAsync(imagePath).AsTask().GetAwaiter().GetResult();
            var package = CreatePackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            Clipboard.SetContent(package);
            Clipboard.Flush();
        });
    }

    /// <summary>
    /// 파일 드롭 리스트를 다시 클립보드에 쓴다.
    /// </summary>
    public static void SetFileDropList(IEnumerable<string> filePaths)
    {
        ExecuteWrite(() =>
        {
            var storageItems = new List<IStorageItem>();

            foreach (var filePath in filePaths)
            {
                if (File.Exists(filePath))
                {
                    storageItems.Add(
                        StorageFile.GetFileFromPathAsync(filePath).AsTask().GetAwaiter().GetResult());
                    continue;
                }

                if (Directory.Exists(filePath))
                {
                    storageItems.Add(
                        StorageFolder.GetFolderFromPathAsync(filePath).AsTask().GetAwaiter().GetResult());
                }
            }

            var package = CreatePackage();
            package.SetStorageItems(storageItems);
            Clipboard.SetContent(package);
            Clipboard.Flush();
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
            catch (Exception) when (attempt < 7)
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
            catch (Exception) when (attempt < 7)
            {
                Thread.Sleep(50);
            }
        }

        action();
    }

    private static DataPackage CreatePackage()
    {
        return new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
    }

    private static bool TryGetTextFromHtml(
        DataPackageView content,
        [NotNullWhen(true)] out string? text)
    {
        text = null;

        if (!content.Contains(StandardDataFormats.Html))
        {
            return false;
        }

        var html = content.GetHtmlFormatAsync().AsTask().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(html))
        {
            return false;
        }

        var fragment = ExtractHtmlFragment(html);
        var plainText = ConvertHtmlToPlainText(fragment);
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return false;
        }

        text = plainText;
        return true;
    }

    private static bool TryGetTextFromWebLink(
        DataPackageView content,
        [NotNullWhen(true)] out string? text)
    {
        text = null;

        if (!content.Contains(StandardDataFormats.WebLink))
        {
            return false;
        }

        var uri = content.GetWebLinkAsync().AsTask().GetAwaiter().GetResult();
        if (uri is null)
        {
            return false;
        }

        text = uri.AbsoluteUri;
        return !string.IsNullOrWhiteSpace(text);
    }

    private static string ExtractHtmlFragment(string html)
    {
        try
        {
            return HtmlFormatHelper.GetStaticFragment(html);
        }
        catch
        {
            return html;
        }
    }

    private static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var normalized = HtmlLineBreakRegex.Replace(html, "\n");
        normalized = HtmlListItemRegex.Replace(normalized, "\n- ");
        normalized = HtmlTagRegex.Replace(normalized, string.Empty);
        normalized = WebUtility.HtmlDecode(normalized)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\u00A0', ' ');
        normalized = ExtraBlankLineRegex.Replace(normalized, "\n\n");

        return normalized.Trim();
    }

    private static byte[] ConvertToPngBytes(
        IRandomAccessStream sourceStream,
        out int width,
        out int height)
    {
        var decoder = BitmapDecoder.CreateAsync(sourceStream).AsTask().GetAwaiter().GetResult();

        using var softwareBitmap = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();
        using var outputStream = new InMemoryRandomAccessStream();
        var encoder = BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputStream)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        encoder.SetSoftwareBitmap(softwareBitmap);
        encoder.IsThumbnailGenerated = false;
        encoder.FlushAsync().AsTask().GetAwaiter().GetResult();

        outputStream.Seek(0);
        using var stream = outputStream.AsStreamForRead();
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);

        width = (int)softwareBitmap.PixelWidth;
        height = (int)softwareBitmap.PixelHeight;
        return memoryStream.ToArray();
    }
}
