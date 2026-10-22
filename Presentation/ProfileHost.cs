using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

namespace OneTunnel.Core;

// Transport only: application decisions and service authentication belong to Go.
internal static class ProfileHost
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Process? worker;
    private const int MaxFrame = 32 * 1024 * 1024;
    private static bool exitRegistered;
    private static Process Spawn(string arguments = "")
    {
        string path = Path.Combine(AppContext.BaseDirectory, "OneTunnel.Profiles.exe");
        if (!File.Exists(path)) throw new InvalidOperationException("Не найдено пользовательское Go-ядро. Нужна полная сборка приложения.");
        var info = new ProcessStartInfo(path, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = AppContext.BaseDirectory };
        var process = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить Go-ядро.");
        process.BeginErrorReadLine();
        return process;
    }
    private static void Start()
    {
        if (worker is { HasExited: false }) return;
        worker?.Dispose(); worker = Spawn();
        if (exitRegistered) return;
        exitRegistered = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { worker?.StandardInput.Close(); } catch { } };
    }
    public static T Call<T>(string command, object? args = null) => CallAsync<T>(command, args).GetAwaiter().GetResult();
    public static async Task<T> CallAsync<T>(string command, object? args = null, int seconds = 40, CancellationToken cancellation = default, Action<T>? received = null)
    {
        await Gate.WaitAsync(cancellation).ConfigureAwait(false);
        byte[] request = [], response = [];
        try
        {
            Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            var node = args == null ? new System.Text.Json.Nodes.JsonObject() : JsonSerializer.SerializeToNode(args)!.AsObject();
            node["Version"] = 1; node["Command"] = command;
            request = JsonSerializer.SerializeToUtf8Bytes(node);
            if (request.Length > MaxFrame) throw new InvalidDataException();
            byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, request.Length);
            var input = worker!.StandardInput.BaseStream; var output = worker.StandardOutput.BaseStream;
            await input.WriteAsync(header, timeout.Token).ConfigureAwait(false);
            await input.WriteAsync(request, timeout.Token).ConfigureAwait(false);
            await input.FlushAsync(timeout.Token).ConfigureAwait(false);
            await output.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
            int count = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (count < 2 || count > MaxFrame) throw new InvalidDataException();
            response = new byte[count]; await output.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.GetProperty("Version").GetInt32() != 1) throw new InvalidDataException();
            if (root.TryGetProperty("Error", out var error) && error.ValueKind == JsonValueKind.String) throw new FormatException(error.GetString());
            var value = root.GetProperty("Value").ValueKind == JsonValueKind.Null ? default! : root.GetProperty("Value").Deserialize<T>()!;
            received?.Invoke(value); // Deliver before releasing Gate; Go projections keep their wire order.
            return value;
        }
        catch (FormatException) { throw; }
        catch
        {
            try { if (worker is { HasExited: false }) worker.Kill(); } catch { }
            worker?.Dispose(); worker = null;
            if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
            throw new InvalidOperationException("Нет ответа от пользовательского Go-ядра. Операция могла выполниться; перед повтором проверь состояние. Служба подключения продолжает работать.");
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(request);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(response);
            Gate.Release();
        }
    }
    public static async Task WatchAsync(Func<DesktopObservation, Task> receive, CancellationToken cancellation)
    {
        using var process = Spawn("--events");
        try
        {
            while (!cancellation.IsCancellationRequested)
                await receive(await CoreWire.ReadAsync<DesktopObservation>(process.StandardOutput.BaseStream, cancellation).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            try { process.StandardInput.Close(); } catch { }
            try { if (!process.HasExited) process.Kill(); } catch { }
        }
    }
}
