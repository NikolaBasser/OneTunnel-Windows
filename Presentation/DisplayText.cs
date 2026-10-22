using System.Globalization;
namespace OneTunnel.Core;
// Rendering only: no import truncation, profile validation or naming policy.
public static class DisplayText
{
    public static IEnumerable<string> Elements(string value){var e=StringInfo.GetTextElementEnumerator(value);while(e.MoveNext())yield return e.GetTextElement();}
    public static string EmojiKey(string element){bool joiner=element.Contains('\u200d');return string.Join('-',element.EnumerateRunes().Where(r=>joiner||r.Value!=0xfe0f).Select(r=>r.Value.ToString("x",CultureInfo.InvariantCulture)));}
}
