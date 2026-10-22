using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace OneTunnel.Core;

// Persistent, transactionally installed WFP filters. Deliberately not IDisposable:
// losing the app/engine must NOT remove the block. Only explicit disconnect does.
public static class KillSwitch
{
    private static string Sid => RuntimeIdentity.OwnerSid;
    private static Guid Key(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes("OneTunnel.WFP.v1/" + Sid + "/" + name)).AsSpan(0, 16));
    private static Guid ProviderKey => Key("provider");
    private static Guid SublayerKey => Key("sublayer");
    private static byte[] Owner => SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(Storage.Root).TrimEnd('\\').ToUpperInvariant()));
    private static string Marker => Path.Combine(Storage.Root, "kill-switch.json");
    public static bool NeedsRecovery => File.Exists(Marker);
    private static readonly Guid V4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82"), V6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    private static readonly Guid In4 = new("e1cd9fe7-f4b5-4273-96c0-592e487b8650"), In6 = new("a3b42c97-9f04-4672-b87e-cee9c483257f");
    private static readonly Guid App = new("d78e1e87-8644-4ea5-9437-d809ecefc971"), Interface = new("4cd62a49-59c3-4969-b7f3-bda5d32890a4"), Flags = new("632ce23b-5167-435c-86d7-e903684aa80c");
    private static readonly Guid Protocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7"), LocalPort = new("0c1ba1af-5765-453f-af22-a8f791ac775b"), RemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b"), RemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    private const int MaxFilters = 256;

    public static void Enable(string interfaceName, IEnumerable<string> enginePaths)
    {
        if (!Connection.IsAdministrator) throw new InvalidOperationException("Kill switch требует прав администратора.");
        if (IntPtr.Size != 8 || Marshal.SizeOf<Filter>() != 200) throw new InvalidOperationException("Неподдерживаемая архитектура WFP.");
        using var engine = Open();
        var providerKey = ProviderKey;
        uint existing = FwpmProviderGetByKey0(engine.Handle, ref providerKey, out var found);
        if (existing == 0) { FwpmFreeMemory0(ref found); throw new InvalidOperationException("Kill switch уже установлен. Сначала отключи прежнее подключение или выполни восстановление."); }
        Check(existing, 0x80320005);
        var adapter = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == interfaceName && n.OperationalStatus == OperationalStatus.Up) ?? throw new InvalidOperationException("Интерфейс туннеля не готов для kill switch.");
        var adapterGuid = Guid.Parse(adapter.Id);
        Check(ConvertInterfaceGuidToLuid(ref adapterGuid, out ulong luid));
        using var memory = new Arena();
        // Journal precedes the first possible persistent commit, including crashes.
        File.WriteAllText(Marker, "{\"version\":1,\"scope\":\"whole-device\"}");
        Check(FwpmTransactionBegin0(engine.Handle, 0));
        bool committed = false;
        try
        {
            var owner = Owner;
            var provider = new Provider { Key = providerKey, Display = memory.Display("OneTunnel kill switch"), Flags = 1, Data = new Blob { Size = (uint)owner.Length, Data = memory.Bytes(owner) } };
            Check(FwpmProviderAdd0(engine.Handle, ref provider, IntPtr.Zero));
            var providerPointer = memory.Struct(providerKey);
            var sublayer = new Sublayer { Key = SublayerKey, Display = memory.Display("OneTunnel persistent traffic lock"), Flags = 1, Provider = providerPointer, Weight = 0xfffe };
            Check(FwpmSubLayerAdd0(engine.Handle, ref sublayer, IntPtr.Zero));
            int slot = 0;
            void Rule(Guid layer, string label, byte weight, bool permit, params Condition[] conditions)
            {
                if (slot >= MaxFilters) throw new InvalidOperationException("Слишком много правил kill switch.");
                var rule = new Filter { Key = Key("filter/" + slot++), Display = memory.Display("OneTunnel · " + label), Flags = 1, Provider = providerPointer, Layer = layer, Sublayer = SublayerKey, Weight = new Value { Type = 1, Data = weight }, Count = (uint)conditions.Length, Conditions = memory.Conditions(conditions), Action = new Action { Type = permit ? 0x1002u : 0x1001u } };
                Check(FwpmFilterAdd0(engine.Handle, ref rule, IntPtr.Zero, out _));
            }
            foreach (var layer in new[] { V4, V6, In4, In6 })
            {
                Rule(layer, "block direct traffic", 0, false);
                Rule(layer, "tunnel interface", 12, true, C(Interface, 4, (ulong)memory.Struct(luid)));
                Rule(layer, "loopback", 13, true, C(Flags, 3, 1, 6));
                foreach (string path in enginePaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    Check(FwpmGetAppIdFromFileName0(Path.GetFullPath(path), out var appId));
                    try { Rule(layer, "transport engine", 14, true, C(App, 12, (ulong)appId)); }
                    finally { FwpmFreeMemory0(ref appId); }
                }
            }
            // Only DHCP and IPv6 neighbor/router discovery may bypass the tunnel.
            var dhcpServers = NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().DhcpServerAddresses).Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(ip => ip.GetAddressBytes()).Append(new byte[] { 255, 255, 255, 255 }).DistinctBy(Convert.ToHexString);
            foreach (byte[] address in dhcpServers)
            {
                uint number = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(address);
                foreach (var layer in new[] { V4, In4 }) Rule(layer, "DHCPv4", 12, true, C(Protocol, 1, 17), C(LocalPort, 2, 68), C(RemotePort, 2, 67), C(RemoteAddress, 3, number));
            }
            Rule(V6, "DHCPv6", 12, true, C(Protocol, 1, 17), C(LocalPort, 2, 546), C(RemotePort, 2, 547), C(RemoteAddress, 11, (ulong)memory.Bytes(System.Net.IPAddress.Parse("ff02::1:2").GetAddressBytes())));
            foreach (ushort type in new ushort[] { 133, 134, 135, 136 }) foreach (var layer in new[] { V6, In6 }) Rule(layer, "IPv6 neighbor discovery", 12, true, C(Protocol, 1, 58), C(LocalPort, 2, type));
            Check(FwpmTransactionCommit0(engine.Handle)); committed = true;
        }
        finally
        {
            if (!committed) { FwpmTransactionAbort0(engine.Handle); File.Delete(Marker); }
        }
    }
    public static void Remove(bool forceOwnedRecovery = false)
    {
        if (!NeedsRecovery && !forceOwnedRecovery) return;
        if (!Connection.IsAdministrator) throw new InvalidOperationException("Для снятия kill switch нужны права администратора. Открой «Движки и восстановление».");
        using var engine = Open();
        var key = ProviderKey;
        uint result = FwpmProviderGetByKey0(engine.Handle, ref key, out var pointer);
        if (result == 0)
        {
            try
            {
                var provider = Marshal.PtrToStructure<Provider>(pointer);
                if (provider.Data.Size != 32) throw new InvalidOperationException("Неизвестный владелец правил kill switch.");
                byte[] owner = new byte[32];
                Marshal.Copy(provider.Data.Data, owner, 0, owner.Length);
                if (!owner.SequenceEqual(Owner)) throw new InvalidOperationException("Kill switch принадлежит другой копии приложения. Отключи её подключение.");
            }
            finally { FwpmFreeMemory0(ref pointer); }
            Check(FwpmTransactionBegin0(engine.Handle, 0)); bool committed = false;
            try
            {
                for (int i = 0; i < MaxFilters; i++) { var filter = Key("filter/" + i); Check(FwpmFilterDeleteByKey0(engine.Handle, ref filter), 0x80320003); }
                var sublayer = SublayerKey; Check(FwpmSubLayerDeleteByKey0(engine.Handle, ref sublayer), 0x80320007);
                Check(FwpmProviderDeleteByKey0(engine.Handle, ref key));
                Check(FwpmTransactionCommit0(engine.Handle)); committed = true;
            }
            finally { if (!committed) FwpmTransactionAbort0(engine.Handle); }
        }
        else Check(result, 0x80320005);
        File.Delete(Marker);
    }
    internal static int InstalledFilterCount()
    {
        using var engine = Open(); int count = 0;
        for (int i = 0; i < MaxFilters; i++)
        {
            var key = Key("filter/" + i);
            uint result = FwpmFilterGetByKey0(engine.Handle, ref key, out var filter);
            if (result == 0) { count++; FwpmFreeMemory0(ref filter); } else Check(result, 0x80320003);
        }
        return count;
    }
    private static Condition C(Guid field, uint type, ulong data, uint match = 0) => new() { Field = field, Match = match, Value = new Value { Type = type, Data = data } };
    private static void Check(uint result, uint ignored = 0) { if (result != 0 && result != ignored) throw new Win32Exception(unchecked((int)result), $"Windows WFP: 0x{result:X8}. Защита не подтверждена."); }
    private static Engine Open() { Check(FwpmEngineOpen0(null, 10, IntPtr.Zero, IntPtr.Zero, out var handle)); return new Engine(handle); }
    private sealed class Engine(IntPtr handle) : IDisposable { public IntPtr Handle => handle; public void Dispose() => FwpmEngineClose0(handle); }
    private sealed class Arena : IDisposable
    {
        private readonly List<IntPtr> pointers = [];
        public IntPtr Bytes(byte[] value) { var p = Marshal.AllocHGlobal(value.Length); pointers.Add(p); Marshal.Copy(value, 0, p, value.Length); return p; }
        public IntPtr Struct<T>(T value) where T : struct { var p = Marshal.AllocHGlobal(Marshal.SizeOf<T>()); pointers.Add(p); Marshal.StructureToPtr(value, p, false); return p; }
        public Display Display(string name) { var p = Marshal.StringToHGlobalUni(name); pointers.Add(p); return new() { Name = p }; }
        public IntPtr Conditions(Condition[] conditions)
        {
            if (conditions.Length == 0) return IntPtr.Zero;
            int size = Marshal.SizeOf<Condition>(); var p = Marshal.AllocHGlobal(size * conditions.Length); pointers.Add(p);
            for (int i = 0; i < conditions.Length; i++) Marshal.StructureToPtr(conditions[i], p + i * size, false);
            return p;
        }
        public void Dispose() { foreach (var p in pointers) Marshal.FreeHGlobal(p); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Display { public IntPtr Name, Description; }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public uint Size; public IntPtr Data; }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct Value { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ulong Data; }
    [StructLayout(LayoutKind.Sequential)] private struct Condition { public Guid Field; public uint Match; public Value Value; }
    [StructLayout(LayoutKind.Sequential)] private struct Action { public uint Type; public Guid Callout; }
    [StructLayout(LayoutKind.Sequential)] private struct Provider { public Guid Key; public Display Display; public uint Flags; public Blob Data; public IntPtr Service; }
    [StructLayout(LayoutKind.Sequential)] private struct Sublayer { public Guid Key; public Display Display; public uint Flags; public IntPtr Provider; public Blob Data; public ushort Weight; }
    [StructLayout(LayoutKind.Explicit, Size = 200)] private struct Filter
    {
        [FieldOffset(0)] public Guid Key; [FieldOffset(16)] public Display Display; [FieldOffset(32)] public uint Flags;
        [FieldOffset(40)] public IntPtr Provider; [FieldOffset(48)] public Blob Data; [FieldOffset(64)] public Guid Layer; [FieldOffset(80)] public Guid Sublayer;
        [FieldOffset(96)] public Value Weight; [FieldOffset(112)] public uint Count; [FieldOffset(120)] public IntPtr Conditions; [FieldOffset(128)] public Action Action;
        [FieldOffset(152)] public Guid Context; [FieldOffset(168)] public IntPtr Reserved; [FieldOffset(176)] public ulong Id; [FieldOffset(184)] public Value EffectiveWeight;
    }
    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)] private static extern uint FwpmEngineOpen0(string? server, uint auth, IntPtr identity, IntPtr session, out IntPtr engine);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmEngineClose0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmTransactionBegin0(IntPtr engine, uint flags);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmTransactionCommit0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmTransactionAbort0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmProviderAdd0(IntPtr engine, ref Provider provider, IntPtr security);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmSubLayerAdd0(IntPtr engine, ref Sublayer sublayer, IntPtr security);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmFilterAdd0(IntPtr engine, ref Filter filter, IntPtr security, out ulong id);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmProviderGetByKey0(IntPtr engine, ref Guid key, out IntPtr provider);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmProviderDeleteByKey0(IntPtr engine, ref Guid key);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmSubLayerDeleteByKey0(IntPtr engine, ref Guid key);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmFilterDeleteByKey0(IntPtr engine, ref Guid key);
    [DllImport("fwpuclnt.dll")] private static extern uint FwpmFilterGetByKey0(IntPtr engine, ref Guid key, out IntPtr filter);
    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)] private static extern uint FwpmGetAppIdFromFileName0(string file, out IntPtr appId);
    [DllImport("fwpuclnt.dll")] private static extern void FwpmFreeMemory0(ref IntPtr memory);
    [DllImport("iphlpapi.dll")] private static extern uint ConvertInterfaceGuidToLuid(ref Guid guid, out ulong luid);
}
