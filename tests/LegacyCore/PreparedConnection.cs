namespace OneTunnel.Core;

// Private Go-to-Windows bridge contract. Never accepted on the public API.
public sealed record PreparedConnection(string NativeSource, bool NativeShadowsocks,
    bool NeedsXray, bool AccurateTime, int LocalPort, int? BridgePort, string Xray, string Proxy, string Clock);
