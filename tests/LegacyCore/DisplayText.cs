using System.Buffers;
using System.Globalization;
using System.Text;

namespace OneTunnel.Core;
public static class DisplayText
{
    public static IEnumerable<string> Elements(string value)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext()) yield return enumerator.GetTextElement();
    }
    public static bool IsWellFormed(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        while (!span.IsEmpty) { if (Rune.DecodeFromUtf16(span, out _, out int length) != OperationStatus.Done) return false; span = span[length..]; }
        return true;
    }
    public static string ImportedName(string value)
    {
        value = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        // Cut at grapheme boundaries: flags, modifiers, keycaps and ZWJ sequences stay intact.
        return string.Concat(Elements(value).Take(100));
    }
    public static string EmojiKey(string element)
    {
        bool joiner = element.Contains('\u200d');
        return string.Join('-', element.EnumerateRunes().Where(r => joiner || r.Value != 0xfe0f).Select(r => r.Value.ToString("x", CultureInfo.InvariantCulture)));
    }
}
