namespace Pelicano.Models;

/// <summary>
/// LiteDB에 저장되는 암호화된 클립보드 레코드다.
/// </summary>
internal sealed class StoredClipboardItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ClipboardItemKind ItemKind { get; set; } = ClipboardItemKind.Text;

    public string ContentHash { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;

    public int Version { get; set; } = 1;

    public byte[] Nonce { get; set; } = [];

    public byte[] EncryptedPayload { get; set; } = [];
}
