using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace OneTunnel.Core;
public static class Storage
{
    public static string Root { get; } = Environment.GetEnvironmentVariable("ONETUNNEL_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneTunnel");
    public static string Runtime => Path.Combine(Root, "runtime");
    public static void Initialize()
    {
        PrivateDirectory(Root); PrivateDirectory(Runtime);
    }
    public static void PrivateDirectory(string path)
    {
        var info = Directory.CreateDirectory(path);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(acl);
    }
    public static HashSet<string> CollapsedGroups { get; } = [];
    public static Dictionary<string,string> SubscriptionSources { get; } = [];
    private sealed record Document(int Version, List<Profile> Profiles, HashSet<string> CollapsedGroups, Dictionary<string,string>? SubscriptionSources = null);
    public static List<Profile> Load()
    {
        var path = Path.Combine(Root, "profiles.bin");
        if (!File.Exists(path)) return [];
        var bytes = Protect(File.ReadAllBytes(path), false);
        using var json = JsonDocument.Parse(bytes);
        CollapsedGroups.Clear();
        SubscriptionSources.Clear();
        if (json.RootElement.ValueKind == JsonValueKind.Array) return JsonSerializer.Deserialize<List<Profile>>(bytes) ?? [];
        var document = JsonSerializer.Deserialize<Document>(bytes) ?? throw new FormatException("Хранилище повреждено.");
        if (document.Version != 1) throw new FormatException("Неподдерживаемая версия хранилища.");
        CollapsedGroups.UnionWith(document.CollapsedGroups);
        if(document.SubscriptionSources!=null)
            foreach(var pair in document.SubscriptionSources)
                if(Uri.TryCreate(pair.Value,UriKind.Absolute,out var source)&&source.Scheme=="https"&&source.UserInfo=="")SubscriptionSources[pair.Key]=pair.Value;
        return document.Profiles;
    }
    public static void Save(IEnumerable<Profile> profiles)
    {
        var path = Path.Combine(Root, "profiles.bin");
        var list=profiles.ToList();
        var groups=list.Select(p=>p.Group).ToHashSet();
        var sources=SubscriptionSources.Where(p=>groups.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
        var bytes = Protect(JsonSerializer.SerializeToUtf8Bytes(new Document(1, list, CollapsedGroups, sources)), true);
        File.WriteAllBytes(path + ".new", bytes); File.Move(path + ".new", path, true);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    public static byte[] Protect(byte[] data, bool encrypt)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            var ok = encrypt ? CryptProtectData(ref input, "OneTunnel profiles", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows не смогла расшифровать или сохранить профили.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
    }
}
