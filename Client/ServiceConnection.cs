using System.IO.Pipes;
using System.Runtime.InteropServices;
using OneTunnel.Core;

namespace OneTunnel.Client;

public sealed class ServiceConnection : IConnectionController
{
    private CoreStatus state = new(false, false, false, false, false, false, 0, "Проверка службы…", null);
    public bool Available { get; private set; }
    public bool Active => state.Active;
    public bool NeedsRecovery => state.NeedsRecovery;
    public bool TrafficLocked => state.TrafficLocked;
    public bool WholeDevice => state.WholeDevice;
    public bool FullDeviceMode => state.FullDeviceMode;
    public bool ClockSynchronized => state.ClockSynchronized;
    public int LocalPort => state.LocalPort;
    public string Status => Available ? state.Status : "Служба недоступна · состояние сети не подтверждено";
    public event Action? Changed;
    public Guid? ProfileId => state.ProfileId;
    public async Task RefreshAsync() { try { await CallAsync("status", timeout: 5); } catch { } }
    public async Task StartAsync(Profile profile, bool fullDevice)
    {
        profile.Transport?.Validate(profile);
        await CallAsync("start", profile, fullDevice);
    }
    public async Task StopAsync() => await CallAsync("stop");
    public async Task RecoverAsync() => await CallAsync("recover");
    public async Task<string> ProbeAsync() => (await CallAsync("probe")).Result ?? "Проверка не завершена.";
    public async Task<CoreResponse> CallAsync(string command, Profile? profile = null, bool fullDevice = false, int timeout = 180)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var pipe = new NamedPipeClientStream(".", CoreProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(2000, cancellation.Token);
            ServicePeer.Verify(pipe);
            await CoreWire.WriteAsync(pipe, new CoreRequest(CoreProtocol.Version, command, profile, fullDevice), cancellation.Token);
            var reply = await CoreWire.ReadAsync<CoreResponse>(pipe, cancellation.Token);
            if (reply.Version != CoreProtocol.Version) throw new InvalidDataException("Несовместимая версия службы.");
            state = reply.State; Available = true; Changed?.Invoke();
            if (reply.Error != null) throw new CoreCommandException(reply.Error);
            return reply;
        }
        catch (CoreCommandException) { throw; }
        catch (Exception ex)
        {
            Available = false; Changed?.Invoke();
            throw new InvalidOperationException("Нет связи со службой OneTunnel. Открой «Движки и восстановление» и установи или обнови службу. Последнее состояние подключения сохранено; блокировка может оставаться активной.", ex);
        }
    }
}

public sealed class CoreCommandException(string code) : InvalidOperationException(code switch
{
    "incompatible_version" => "Обнови службу: её версия несовместима с интерфейсом.",
    "invalid_configuration" => "Служба отклонила конфигурацию. Проверь профиль.",
    "operation_timeout" => "Операция не завершилась вовремя. Проверь состояние службы.",
    "network_timeout" => "Сервер не ответил на проверку вовремя. Проверь доступность подключения и повтори проверку.",
    "network_error" => "Проверочный запрос не прошёл. Проверь доступность сервера и DNS.",
    "backend_unavailable" => "Сетевой адаптер ядра остановился. Для восстановления используй «Очистить оставшееся подключение»; защита не снимается автоматически.",
    "recovery_required" => "Сначала отключи активное подключение или выполни восстановление.",
    _ => "Служба не выполнила операцию. Проверь состояние подключения, доступность сервера и необходимость восстановления."
}) { public string Code { get; } = code; }

internal static class ServicePeer
{
    [StructLayout(LayoutKind.Sequential)] private struct ProcessStatus { public uint Type, State, Accepted, Error, Specific, Checkpoint, WaitHint, Pid, Flags; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenServiceW(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatusEx(IntPtr service, int level, out ProcessStatus status, int size, out int needed);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool StartServiceW(IntPtr service, int argc, IntPtr argv);
    internal static async Task StartAsync()
    {
        var manager = OpenSCManagerW(null, null, 1);
        if (manager == IntPtr.Zero) throw new InvalidOperationException("Не удалось открыть управление службой.");
        try
        {
            var service = OpenServiceW(manager, CoreProtocol.ServiceName, 0x14);
            if (service == IntPtr.Zero) throw new InvalidOperationException("Сначала установи службу OneTunnel.");
            try
            {
                if (!StartServiceW(service, 0, IntPtr.Zero) && Marshal.GetLastWin32Error() != 1056) throw new InvalidOperationException("Не удалось запустить службу OneTunnel.");
                for (int i = 0; i < 100; i++)
                {
                    if (QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ProcessStatus>(), out _) && status.State == 4) return;
                    await Task.Delay(200);
                }
                throw new TimeoutException("Служба не запустилась вовремя.");
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    internal static void Verify(NamedPipeClientStream pipe)
    {
        uint expected = RunningProcessId();
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid) || pid != expected) throw new IOException("Untrusted IPC server.");
    }
    internal static uint RunningProcessId()
    {
        var manager = OpenSCManagerW(null, null, 1);
        if (manager == IntPtr.Zero) throw new IOException("SCM unavailable.");
        try
        {
            var service = OpenServiceW(manager, CoreProtocol.ServiceName, 4);
            if (service == IntPtr.Zero) throw new IOException("Service not installed.");
            try
            {
                if (!QueryServiceStatusEx(service, 0, out var s, Marshal.SizeOf<ProcessStatus>(), out _) || s.State != 4 || s.Pid == 0) throw new IOException("Service not running.");
                return s.Pid;
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
}
