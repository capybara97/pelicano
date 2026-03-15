namespace Pelicano.Models;

/// <summary>
/// 암호화 저장 전 클립보드 항목의 민감 데이터를 담는 페이로드다.
/// </summary>
internal sealed class ClipboardPayload
{
    public string RawText { get; set; } = string.Empty;

    public string NormalizedText { get; set; } = string.Empty;

    public string SourceFormat { get; set; } = string.Empty;

    public List<string> FileDropPaths { get; set; } = [];

    public byte[] ImageBytes { get; set; } = [];
}
