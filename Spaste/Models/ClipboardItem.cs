namespace Spaste.Models;

/// <summary>
/// Spaste가 저장하는 히스토리 항목 종류다.
/// 텍스트, 비트맵 이미지, 탐색기 파일 드롭 복사를 구분한다.
/// </summary>
public enum ClipboardItemKind
{
    Text,
    Image,
    FileDrop
}

/// <summary>
/// 클립보드 히스토리 한 건을 표현한다.
/// LiteDB 저장과 UI 표시를 동시에 고려해 메타데이터를 평면 구조로 둔다.
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
    /// 일반 텍스트 본문이다.
    /// 텍스트가 아닌 항목은 빈 문자열을 유지한다.
    /// </summary>
    public string PlainText { get; set; } = string.Empty;

    /// <summary>
    /// Markdown 변환 결과다.
    /// 텍스트가 아닌 항목은 빈 문자열을 유지한다.
    /// </summary>
    public string MarkdownText { get; set; } = string.Empty;

    /// <summary>
    /// 원본 포맷 또는 캡처 출처 설명이다.
    /// </summary>
    public string SourceFormat { get; set; } = string.Empty;

    /// <summary>
    /// 중복 판정을 위한 콘텐츠 해시다.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// 비트맵 캡처 시 저장된 PNG 경로다.
    /// </summary>
    public string? ImagePath { get; set; }

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
    /// 현재 항목이 Markdown 복사를 지원하는지 여부다.
    /// </summary>
    public bool SupportsMarkdown =>
        ItemKind == ClipboardItemKind.Text && !string.IsNullOrWhiteSpace(PlainText);
}
