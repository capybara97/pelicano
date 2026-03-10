using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Spaste;

/// <summary>
/// 일반 텍스트 정규화, Markdown 변환, 미리보기 생성, 해시 계산을 담당한다.
/// 복사된 텍스트를 회사 표준에 맞는 "텍스트 전용" 형태로 다듬는 핵심 서비스다.
/// </summary>
internal sealed class TextStripper
{
    private const string EncryptedClipboardPrefix = "ENCRYPTED_CLIPDATA";
    private const string EncryptedClipboardPlaceholder = "<암호화된 문서입니다>";
    private static readonly Regex BulletRegex =
        new(@"^\s*[•·◦\-\*]\s+(?<value>.+)$", RegexOptions.Compiled);

    private static readonly Regex NumberedListRegex =
        new(@"^\s*(?<index>\d+)[\.\)]\s+(?<value>.+)$", RegexOptions.Compiled);

    /// <summary>
    /// 줄바꿈과 후행 공백을 정리해 비교/저장에 적합한 텍스트로 만든다.
    /// </summary>
    public string NormalizePlainText(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var normalized = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').Select(line => line.TrimEnd()).ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 일반 텍스트를 Markdown 친화적인 형태로 변환한다.
    /// 리스트, 제목, 코드 블록 패턴을 우선 처리한다.
    /// </summary>
    public string ConvertToMarkdown(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        var lines = plainText
            .Replace("\r\n", "\n")
            .Split('\n')
            .ToList();

        var builder = new List<string>();
        var index = 0;

        while (index < lines.Count)
        {
            var currentLine = lines[index];

            if (string.IsNullOrWhiteSpace(currentLine))
            {
                builder.Add(string.Empty);
                index += 1;
                continue;
            }

            if (currentLine.StartsWith('\t') || currentLine.StartsWith("    ", StringComparison.Ordinal))
            {
                var codeBlock = new List<string>();

                while (index < lines.Count &&
                       (lines[index].StartsWith('\t') ||
                        lines[index].StartsWith("    ", StringComparison.Ordinal)))
                {
                    codeBlock.Add(lines[index].TrimStart('\t').TrimStart());
                    index += 1;
                }

                builder.Add("```");
                builder.AddRange(codeBlock);
                builder.Add("```");
                continue;
            }

            if (BulletRegex.Match(currentLine) is { Success: true } bulletMatch)
            {
                builder.Add($"- {bulletMatch.Groups["value"].Value.Trim()}");
                index += 1;
                continue;
            }

            if (NumberedListRegex.Match(currentLine) is { Success: true } numberedMatch)
            {
                builder.Add($"{numberedMatch.Groups["index"].Value}. {numberedMatch.Groups["value"].Value.Trim()}");
                index += 1;
                continue;
            }

            if (IsLikelyHeading(lines, index))
            {
                builder.Add($"## {currentLine.Trim()}");
                index += 1;
                continue;
            }

            builder.Add(currentLine.TrimEnd());
            index += 1;
        }

        return string.Join(Environment.NewLine, builder).Trim();
    }

    /// <summary>
    /// 리스트 제목으로 쓸 짧은 요약 문자열을 만든다.
    /// </summary>
    public string BuildTitle(string text, int maxLength = 56)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "빈 텍스트";
        }

        if (IsEncryptedClipboardText(text))
        {
            return EncryptedClipboardPlaceholder;
        }

        var firstLine = text
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? "빈 텍스트";

        return firstLine.Length <= maxLength
            ? firstLine
            : $"{firstLine[..maxLength]}...";
    }

    /// <summary>
    /// 검색용 인덱스를 일관된 소문자 문자열로 생성한다.
    /// </summary>
    public string BuildSearchIndex(IEnumerable<string> tokens)
    {
        return string.Join(' ', tokens.Where(token => !string.IsNullOrWhiteSpace(token)))
            .ToLowerInvariant();
    }

    /// <summary>
    /// 특정 텍스트가 암호화된 클립보드 데이터 포맷인지 판정한다.
    /// </summary>
    public bool IsEncryptedClipboardText(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               text.StartsWith(EncryptedClipboardPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GUI 표시용 텍스트를 반환한다.
    /// 암호화된 클립보드 데이터는 실제 내용 대신 플레이스홀더를 보여준다.
    /// </summary>
    public string BuildDisplayText(string text)
    {
        return IsEncryptedClipboardText(text) ? EncryptedClipboardPlaceholder : text;
    }

    /// <summary>
    /// 텍스트나 바이너리 값을 SHA-256 해시 문자열로 변환한다.
    /// </summary>
    public string ComputeHash(string value)
    {
        return ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>
    /// 바이너리 값을 SHA-256 해시 문자열로 변환한다.
    /// </summary>
    public string ComputeHash(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 제목 후보 줄이 문서 제목처럼 보이는지 대략 판정한다.
    /// </summary>
    private static bool IsLikelyHeading(IReadOnlyList<string> lines, int index)
    {
        var current = lines[index].Trim();

        if (current.Length is 0 or > 50)
        {
            return false;
        }

        if (current.EndsWith('.') || current.EndsWith(','))
        {
            return false;
        }

        if (BulletRegex.IsMatch(current) || NumberedListRegex.IsMatch(current))
        {
            return false;
        }

        var previousBlank = index == 0 || string.IsNullOrWhiteSpace(lines[index - 1]);
        var nextBlank = index == lines.Count - 1 || string.IsNullOrWhiteSpace(lines[index + 1]);

        return previousBlank && nextBlank;
    }
}
