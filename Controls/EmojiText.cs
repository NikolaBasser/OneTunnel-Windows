using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OneTunnel.Core;

namespace OneTunnel.Controls;
public sealed class EmojiText : TextBlock
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(EmojiText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((EmojiText)d).Refresh()));
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new(StringComparer.Ordinal);
    public static string AssetDirectory => Path.Combine(AppContext.BaseDirectory, "assets", "emoji");
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int RenderedEmojiCount { get; private set; }
    static EmojiText() { FontSizeProperty.OverrideMetadata(typeof(EmojiText), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits, (d, _) => ((EmojiText)d).Refresh())); }
    public EmojiText() { SetResourceReference(StyleProperty, typeof(TextBlock)); }
    private void Refresh()
    {
        if (Inlines == null) return;
        Inlines.Clear(); RenderedEmojiCount = 0;
        string value = Value ?? "";
        SetCurrentValue(AutomationProperties.NameProperty, value);
        var text = new System.Text.StringBuilder();
        void Flush() { if (text.Length > 0) { Inlines.Add(new Run(text.ToString())); text.Clear(); } }
        foreach (var element in DisplayText.Elements(value))
        {
            string key = DisplayText.EmojiKey(element);
            string path = Path.Combine(AssetDirectory, key + ".png");
            if (!File.Exists(path)) { text.Append(element); continue; }
            try
            {
                var bitmap = Cache.GetOrAdd(key, _ => { var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(path); b.EndInit(); b.Freeze(); return b; });
                Flush();
                var image = new Image { Source = bitmap, Width = FontSize * 1.22, Height = FontSize * 1.22, Stretch = Stretch.Uniform, Margin = new Thickness(0.5, 0, 0.5, 0), IsHitTestVisible = false };
                AutomationProperties.SetName(image, element);
                Inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center }); RenderedEmojiCount++;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or System.IO.FileFormatException) { text.Append(element); }
        }
        Flush();
    }
    public static IReadOnlyList<(string Flag, string Name)> Countries()
    {
        if (!Directory.Exists(AssetDirectory)) return [];
        var result = new List<(string, string)>();
        foreach (var path in Directory.EnumerateFiles(AssetDirectory, "1f1*-1f1*.png"))
        {
            var parts = Path.GetFileNameWithoutExtension(path).Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int a) || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b) || a is < 0x1f1e6 or > 0x1f1ff || b is < 0x1f1e6 or > 0x1f1ff) continue;
            string code = new([(char)('A' + a - 0x1f1e6), (char)('A' + b - 0x1f1e6)]);
            string name;
            try { name = new RegionInfo(code).DisplayName; } catch (ArgumentException) { name = code switch { "EU" => "Европейский союз", "UN" => "ООН", _ => code }; }
            result.Add((char.ConvertFromUtf32(a) + char.ConvertFromUtf32(b), name));
        }
        return result.OrderBy(x => x.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
