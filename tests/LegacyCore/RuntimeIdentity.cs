using System.Security.Principal;

namespace OneTunnel.Core;

// Set only by the trusted service entry point, never by an RPC request.
public static class RuntimeIdentity
{
    public static string OwnerSid { get; private set; } = WindowsIdentity.GetCurrent().User!.Value;
    public static void ConfigureServiceOwner(string sid) => OwnerSid = new SecurityIdentifier(sid).Value;
}
