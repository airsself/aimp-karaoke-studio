using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace KaraokeStudio.Services;
public sealed class PipeClient : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancel = new();
    private readonly SemaphoreSlim _writing = new(1, 1);
    private StreamWriter? _writer;
    private Task? _loop;
    public event Action<string>? Message;
    public event Action<bool>? ConnectionChanged;
    public PipeClient(string pipeName) => _pipeName = pipeName;
    public void Start() => _loop ??= Task.Run(LoopAsync);
    private async Task LoopAsync()
    {
        while (!_cancel.IsCancellationRequested)
        {
            StreamWriter? activeWriter = null;
            try
            {
                using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000, _cancel.Token);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false, true), false, 65536, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 65536, leaveOpen: true) { AutoFlush = true };
                activeWriter = writer;
                await _writing.WaitAsync(_cancel.Token);
                try { _writer = writer; } finally { _writing.Release(); }
                ConnectionChanged?.Invoke(true);
                while (!_cancel.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cancel.Token);
                    if (line == null) break;
                    if (line.Length > 1_000_000) throw new InvalidDataException("IPC line exceeds limit");
                    Message?.Invoke(line);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception error) { StudioLog.Error("IPC receive", error); }
            finally
            {
                // Never expose a disposed StreamWriter to SendAsync.
                try
                {
                    await _writing.WaitAsync();
                    try { if (ReferenceEquals(_writer, activeWriter)) _writer = null; }
                    finally { _writing.Release(); }
                }
                catch (ObjectDisposedException) { }
                ConnectionChanged?.Invoke(false);
            }
            try { await Task.Delay(1000, _cancel.Token); }
            catch (OperationCanceledException) { break; }
        }
    }
    public async Task SendAsync(string json)
    {
        try
        {
            await _writing.WaitAsync(_cancel.Token);
            try { if (_writer != null) await _writer.WriteLineAsync(json); }
            finally { _writing.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { StudioLog.Error("IPC send", error); }
    }
    public void Dispose()
    {
        _cancel.Cancel();
        // The running loop owns its streams and disposes them on cancellation.
    }
}
