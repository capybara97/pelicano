using LiteDB;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Pelicano.Models;

/// <summary>
/// Pelicano가 저장하는 히스토리 항목 종류다.
/// 텍스트, 비트맵 이미지, 탐색기 파일 드롭 복사를 구분한다.
/// </summary>
public enum ClipboardItemKind
{
    Text,
    Image,
    FileDrop
}

/// <summary>
/// UI와 런타임에서 사용하는 복호화된 클립보드 히스토리 한 건이다.
/// </summary>
public sealed class ClipboardItem
{
    /// <summary>
    /// 항목 고유 식별자다.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 항목 종류다.
    /// </summary>
    public ClipboardItemKind ItemKind { get; set; } = ClipboardItemKind.Text;

    /// <summary>
    /// 리스트에서 보여줄 짧은 제목이다.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 원문 텍스트다.
    /// 텍스트가 아닌 항목은 빈 문자열을 유지한다.
    /// </summary>
    public string PlainText { get; set; } = string.Empty;

    /// <summary>
    /// 줄바꿈만 정규화한 일반 텍스트 표현이다.
    /// 텍스트가 아닌 항목은 빈 문자열을 유지한다.
    /// </summary>
    public string NormalizedText { get; set; } = string.Empty;

    /// <summary>
    /// 원본 포맷 또는 캡처 출처 설명이다.
    /// </summary>
    public string SourceFormat { get; set; } = string.Empty;

    /// <summary>
    /// 중복 판정을 위한 콘텐츠 해시다.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// 이전 버전 호환을 위한 이미지 파일 경로다.
    /// </summary>
    public string? ImagePath { get; set; }

    /// <summary>
    /// 이미지 항목의 PNG 바이트다.
    /// </summary>
    public byte[] ImageBytes { get; set; } = [];

    /// <summary>
    /// 탐색기 등에서 복사한 파일/폴더 목록이다.
    /// </summary>
    public List<string> FileDropPaths { get; set; } = [];

    /// <summary>
    /// 캡처 시각이다.
    /// </summary>
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 검색 필터링용 인덱스 문자열이다.
    /// </summary>
    public string SearchIndex { get; set; } = string.Empty;

    /// <summary>
    /// UI에서 보여줄 항목 종류 이름이다.
    /// </summary>
    public string KindLabel => ItemKind switch
    {
        ClipboardItemKind.Text => "텍스트",
        ClipboardItemKind.Image => "이미지",
        ClipboardItemKind.FileDrop => "파일",
        _ => "기타"
    };

    /// <summary>
    /// UI에서 사용하는 간단한 시각 문자열이다.
    /// </summary>
    public string CapturedTimeLabel => $"{CapturedAt:HH:mm}";

    [BsonIgnore]
    public BitmapImage? ThumbnailBitmap
    {
        get
        {
            var path = ThumbnailPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return new BitmapImage(new Uri(path));
            }
            catch
            {
                return null;
            }
        }
    }

    [BsonIgnore]
    public string? ThumbnailPath => ItemKind switch
    {
        ClipboardItemKind.Image when !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath) => ImagePath,
        ClipboardItemKind.FileDrop => FileDropPaths.FirstOrDefault(IsPreviewImagePath),
        _ => null
    };

    [BsonIgnore]
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);

    [BsonIgnore]
    public string PreviewBadgeText => HasThumbnail ? string.Empty : KindLabel;

    [BsonIgnore]
    public string AssetSecondaryText => ItemKind switch
    {
        ClipboardItemKind.Image => "이미지 미리보기",
        ClipboardItemKind.FileDrop when HasThumbnail => "이미지 파일",
        ClipboardItemKind.FileDrop when FileDropPaths.Count > 1 => $"파일 {FileDropPaths.Count}개",
        ClipboardItemKind.FileDrop => "파일",
        _ => KindLabel
    };

    private static bool IsPreviewImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }
}
