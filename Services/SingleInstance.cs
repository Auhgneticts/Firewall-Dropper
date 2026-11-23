using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Windows;

namespace FirewallDropper.Services;

/// <summary>
/// Only one copy of Firewall Dropper runs per user. A second launch hands its request to the running copy
/// through a named pipe and exits. The request is either "bring yourself to the front" or "here are dropped
/// files". The short-lived elevated helper copies (--apply, --rules, --uninstall) are part of the running
/// app's own work and don't take part in this.
/// </summary>
public static class SingleInstance
{
    private const string ActivateCommand = "activate";
    private const string OpenCommand = "open";

    private static readonly SecurityIdentifier User = WindowsIdentity.GetCurrent().User!;
    private static readonly string MutexName = $@"Local\FirewallDropper-{User.Value}";
    private static readonly string PipeName = $"FirewallDropper-{User.Value}";

    private static Mutex? _mutex;
    private static CancellationTokenSource? _server;

    /// <summary>True if this process is now the one running copy.</summary>
    public static bool TryBecomePrimary()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        bool owned = created;
        if (!owned)
        {
            try { owned = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; } // previous copy crashed; take over
        }

        if (owned)
        {
            _mutex = mutex;
            return true;
        }
        mutex.Dispose();
        return false;
    }

    /// <summary>Gives up being the running copy (e.g. right before starting the freshly installed copy).</summary>
    public static void Release()
    {
        _server?.Cancel();
        _server = null;
        if (_mutex is null) return;
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
        _mutex = null;
    }

    /// <summary>Asks the running copy to come to the front (no files) or to handle dropped files. False if it couldn't be reached.</summary>
    public static bool SendToPrimary(IReadOnlyList<string> files)
    {
        try
        {
            // We were just launched by the user, so we may hand the foreground to the running copy.
            AllowSetForegroundWindow(AsfwAny);

            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            string payload = files.Count == 0 ? ActivateCommand : OpenCommand + "\n" + string.Join("\n", files);
            client.Write(Encoding.UTF8.GetBytes(payload));
            client.Flush();
            client.WaitForPipeDrain();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Listens for requests from later launches. <paramref name="onRequest"/> runs on the UI thread with the
    /// dropped files, or null for "come to the front".
    /// </summary>
    public static void StartServer(Action<string[]?> onRequest)
    {
        _server = new CancellationTokenSource();
        var token = _server.Token;
        var dispatcher = Application.Current.Dispatcher;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = CreatePipe();
                    await server.WaitForConnectionAsync(token);
                    using var buffer = new MemoryStream();
                    await server.CopyToAsync(buffer, token);

                    var lines = Encoding.UTF8.GetString(buffer.ToArray())
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (lines.Length == 0) continue;

                    string[]? files = lines[0] == OpenCommand ? lines[1..] : null;
                    _ = dispatcher.BeginInvoke(() => onRequest(files));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(250, CancellationToken.None);
                }
            }
        }, token);
    }

    private static NamedPipeServerStream CreatePipe()
    {
        // Only this user may connect. Set explicitly so it also works when the running copy is elevated.
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(User, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
