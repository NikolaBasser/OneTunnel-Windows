using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

namespace OneTunnel.Core;

// Private inherited stdio, not a listening port or a privileged service RPC.
// This is transport glue: it does not interpret profiles, files or credentials.
internal static class ProfileHost
{
    private static readonly object Gate=new();
    private static Process? worker;
    private const int MaxFrame=32*1024*1024;
    private static void Start()
    {
        if(worker is {HasExited:false})return;
        string path=Path.Combine(AppContext.BaseDirectory,"OneTunnel.Profiles.exe");
        if(!File.Exists(path))throw new InvalidOperationException("Не найдено Go-ядро профилей. Запусти приложение из полной новой сборки.");
        var info=new ProcessStartInfo(path){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppContext.BaseDirectory};
        worker=Process.Start(info)??throw new InvalidOperationException("Не удалось запустить Go-ядро профилей.");
        // No raw engine/host diagnostics or credentials are retained in the UI.
        worker.BeginErrorReadLine();
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>{try{worker?.StandardInput.Close();}catch{}};
    }
    public static T Call<T>(string command,object? args=null)
    {
        lock(Gate)
        {
            Start();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(40));
            byte[] request=[];byte[] response=[];
            try
            {
                request=BuildRequest(command,args);
                if(request.Length>MaxFrame)throw new InvalidDataException();
                byte[] header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,request.Length);
                var input=worker!.StandardInput.BaseStream;var output=worker.StandardOutput.BaseStream;
                input.WriteAsync(header,timeout.Token).AsTask().GetAwaiter().GetResult();input.WriteAsync(request,timeout.Token).AsTask().GetAwaiter().GetResult();input.FlushAsync(timeout.Token).GetAwaiter().GetResult();
                output.ReadExactlyAsync(header,timeout.Token).AsTask().GetAwaiter().GetResult();int count=BinaryPrimitives.ReadInt32LittleEndian(header);if(count<2||count>MaxFrame)throw new InvalidDataException();
                response=new byte[count];output.ReadExactlyAsync(response,timeout.Token).AsTask().GetAwaiter().GetResult();
                using var document=JsonDocument.Parse(response,new JsonDocumentOptions{MaxDepth=64});var root=document.RootElement;
                if(root.GetProperty("Version").GetInt32()!=1)throw new InvalidDataException();
                if(root.TryGetProperty("Error",out var error)&&error.ValueKind==JsonValueKind.String)throw new FormatException(error.GetString());
                return root.GetProperty("Value").ValueKind==JsonValueKind.Null?default!:root.GetProperty("Value").Deserialize<T>()!;
            }
            catch(FormatException){throw;}
            catch
            {
                try{if(worker is {HasExited:false})worker.Kill();}catch{}worker?.Dispose();worker=null;
                throw new InvalidOperationException("Нет ответа от Go-ядра профилей. Операция могла сохраниться; перечитай список перед повтором. Служба подключения продолжает работать.");
            }
            finally{System.Security.Cryptography.CryptographicOperations.ZeroMemory(request);System.Security.Cryptography.CryptographicOperations.ZeroMemory(response);}
        }
    }
    private static byte[] BuildRequest(string command,object? args)
    {
        var node=args==null?new System.Text.Json.Nodes.JsonObject():System.Text.Json.JsonSerializer.SerializeToNode(args)!.AsObject();
        node["Version"]=1;node["Command"]=command;return JsonSerializer.SerializeToUtf8Bytes(node);
    }
}
