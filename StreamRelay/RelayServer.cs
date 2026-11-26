using System.Diagnostics;
using System.IO;

namespace StreamRelay;

internal sealed class RelayServer : IDisposable
{
    private Process? _process;

    public event EventHandler? Exited;

    public async Task StartAsync(RelaySettings settings)
    {
        string executable = Path.Combine(AppContext.BaseDirectory, "MediaMTX", "mediamtx.exe");
        if (!File.Exists(executable))
            throw new InvalidOperationException("找不到 MediaMTX。請依 README 安裝，或使用包含 MediaMTX 的發行包。");

        ProcessStartInfo startInfo = new(executable)
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "relay.yml"));
        foreach (string key in startInfo.Environment.Keys.Where(key => key.StartsWith("MTX_", StringComparison.OrdinalIgnoreCase)).ToArray())
            startInfo.Environment.Remove(key);

        // 轉發帳密以雜湊交給 MediaMTX，來源連結只放在子程序的環境變數。
        startInfo.Environment["MTX_RTSPADDRESS"] = settings.Address;
        startInfo.Environment["MTX_AUTHINTERNALUSERS_0_USER"] = RelaySettings.HashCredential(settings.Username);
        startInfo.Environment["MTX_AUTHINTERNALUSERS_0_PASS"] = RelaySettings.HashCredential(settings.Password);
        foreach (RelayStream stream in settings.Streams)
            startInfo.Environment[$"MTX_PATHS_{stream.Path.ToUpperInvariant()}_SOURCE"] = stream.Source;

        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data?.Contains("[RTSP] started with listeners on ", StringComparison.Ordinal) == true)
                ready.TrySetResult();
            else if (args.Data?.Contains("bind:", StringComparison.OrdinalIgnoreCase) == true)
                ready.TrySetException(new InvalidOperationException("無法監聽指定 IP 與連接埠，請確認連接埠未被占用。"));
        };
        process.Exited += (_, _) =>
        {
            ready.TrySetException(new InvalidOperationException("MediaMTX 啟動失敗，請確認版本與本機端點設定。"));
            if (ReferenceEquals(Volatile.Read(ref _process), process))
                Exited?.Invoke(this, EventArgs.Empty);
        };

        _process = process;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(12));
            if (process.HasExited)
                throw new InvalidOperationException("MediaMTX 已結束，請確認連接埠及轉發設定。");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Process? process = Interlocked.Exchange(ref _process, null);
        if (process is null)
            return;

        using (process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch (InvalidOperationException)
            {
                // 程序可能尚未啟動，或已經結束。
            }
        }
    }
}
