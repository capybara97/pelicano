namespace Pelicano;

/// <summary>
/// WinRT 클립보드에서 읽은 이미지를 PNG 바이트와 크기 정보로 보관한다.
/// </summary>
internal sealed record ClipboardImageData(byte[] Bytes, int Width, int Height);
