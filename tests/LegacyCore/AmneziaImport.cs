using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace OneTunnel.Core;

public static class AmneziaImport
{
    public static bool IsLink(string source) => source.StartsWith("vpn://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("amnezia://", StringComparison.OrdinalIgnoreCase);
    public static Profile Parse(string name, string source)
    {
        try
        {
            string encoded = source[(source.IndexOf("://", StringComparison.Ordinal) + 3)..].Trim().Replace('-', '+').Replace('_', '/');
            if (encoded.Length > Subscriptions.MaxBytes) throw new FormatException();
            byte[] bytes = Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
            if (bytes.Length >= 6 && bytes[4] == 0x78)
            {
                uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes);
                if (length > Subscriptions.MaxBytes) throw new FormatException();
                using var compressed = new MemoryStream(bytes, 4, bytes.Length - 4);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var output = new MemoryStream();
                byte[] buffer = new byte[8192]; int count;
                while ((count = zlib.Read(buffer)) > 0) { if (output.Length + count > Subscriptions.MaxBytes) throw new FormatException(); output.Write(buffer, 0, count); }
                if (output.Length != length) throw new FormatException();
                bytes = output.ToArray();
            }
            var root = JsonNode.Parse(bytes) as JsonObject ?? throw new FormatException();
            var candidates = new List<Profile>();
            foreach (var container in root["containers"]?.AsArray() ?? [])
            {
                string kind = container?["container"]?.GetValue<string>() ?? "";
                string key = kind switch { "amnezia-awg" => "awg", "amnezia-wireguard" => "wireguard", "amnezia-xray" => "xray", "amnezia-ssxray" => "ssxray", _ => "" };
                if (key.Length == 0) continue;
                var last = container?[key]?["last_config"];
                var config = last is JsonValue ? JsonNode.Parse(last.GetValue<string>()) : last;
                string? text = config?["config"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text) && key is "xray" or "ssxray") text = config?.ToJsonString();
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (key is "awg" or "wireguard")
                {
                    text = text.Replace("$PRIMARY_DNS", root["dns1"]?.GetValue<string>() ?? "")
                        .Replace("$SECONDARY_DNS", root["dns2"]?.GetValue<string>() ?? "");
                    text = System.Text.RegularExpressions.Regex.Replace(text, @"(?mi)^(\s*DNS\s*=)([^\r\n]*)", match => match.Groups[1].Value + " " + string.Join(", ", match.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
                }
                string title = string.IsNullOrWhiteSpace(name) ? DisplayText.ImportedName(root["description"]?.GetValue<string>() ?? "Amnezia") : name;
                if (key is "xray" or "ssxray") candidates.AddRange(Subscriptions.Parse(text).Profiles.Select(p => p with { Name = title }));
                else candidates.Add(ProfileParser.Parse(title, text, key == "awg" ? Protocol.AmneziaWG : Protocol.WireGuard));
            }
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count > 1) throw new FormatException("В ключе Amnezia несколько подключений. Экспортируй нужное подключение в .conf или ссылку VLESS/Shadowsocks.");
            throw new FormatException("В ключе Amnezia нет готовой поддерживаемой конфигурации. Экспортируй .conf или ссылку подключения из Amnezia. Ключи Amnezia Premium/Free, требующие API, пока не поддерживаются.");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or InvalidOperationException or OverflowException) { throw new FormatException("Не удалось разобрать ключ Amnezia. Экспортируй конфигурацию .conf или ссылку подключения."); }
    }
}
