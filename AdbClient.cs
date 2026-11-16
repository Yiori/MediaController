using System.Diagnostics;
using System.IO.Compression;

namespace MediaController;

public enum DeviceState { NoAdb, Disconnected, Unauthorized, Connected }

/// <summary>
/// Gerencia o adb: download automático, detecção de dispositivo USB e
/// um shell persistente para enviar keyevents com baixa latência.
/// </summary>
public sealed class AdbClient : IDisposable
{
    private const string PlatformToolsUrl =
        "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

    private readonly object _lock = new();
    private Process? _shell;
    private string? _shellSerial;
    private System.Threading.Timer? _pollTimer;

    public string? AdbPath { get; private set; }
    public string? PreferredDevice { get; set; }
    public DeviceState State { get; private set; } = DeviceState.NoAdb;
    public string? CurrentSerial { get; private set; }
    public string? CurrentModel { get; private set; }

    public event Action? StateChanged;

    private static string LocalToolsDir => Path.Combine(AppSettings.DataDir, "platform-tools");

    // ------------------------------------------------------------------ setup

    public async Task EnsureAdbAsync(IProgress<string>? progress = null)
    {
        AdbPath = FindAdb();
        if (AdbPath != null) return;

        progress?.Report("Baixando Android platform-tools…");
        Directory.CreateDirectory(AppSettings.DataDir);
        string zip = Path.Combine(AppSettings.DataDir, "platform-tools.zip");
        using (var http = new HttpClient())
        await using (var fs = File.Create(zip))
        {
            await using var s = await http.GetStreamAsync(PlatformToolsUrl);
            await s.CopyToAsync(fs);
        }
        progress?.Report("Extraindo…");
        if (Directory.Exists(LocalToolsDir)) Directory.Delete(LocalToolsDir, true);
        ZipFile.ExtractToDirectory(zip, AppSettings.DataDir, overwriteFiles: true);
        File.Delete(zip);

        AdbPath = FindAdb() ?? throw new FileNotFoundException("adb.exe não encontrado após o download.");
    }

    private static string? FindAdb()
    {
        string local = Path.Combine(LocalToolsDir, "adb.exe");
        if (File.Exists(local)) return local;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try
            {
                var p = Path.Combine(dir.Trim(), "adb.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    public void StartMonitoring()
    {
        _pollTimer = new System.Threading.Timer(_ => Refresh(), null, 0, 2000);
    }

    // ------------------------------------------------------------ devices

    public List<(string Serial, string State, string Model)> ListDevices()
    {
        var list = new List<(string, string, string)>();
        if (AdbPath == null) return list;
        var output = Run("devices -l", 5000);
        foreach (var raw in output.Split('\n').Skip(1))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('*')) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            // Somente USB: ignora dispositivos de rede (ip:porta) e emuladores
            if (parts[0].Contains(':') || parts[0].StartsWith("emulator-")) continue;
            string model = parts.FirstOrDefault(p => p.StartsWith("model:"))?[6..].Replace('_', ' ') ?? parts[0];
            list.Add((parts[0], parts[1], model));
        }
        return list;
    }

    private void Refresh()
    {
        if (AdbPath == null) { SetState(DeviceState.NoAdb, null, null); return; }
        try
        {
            var devs = ListDevices();
            var chosen = devs.FirstOrDefault(d => d.Serial == PreferredDevice && d.State == "device");
            if (chosen.Serial == null) chosen = devs.FirstOrDefault(d => d.State == "device");

            if (chosen.Serial != null)
                SetState(DeviceState.Connected, chosen.Serial, chosen.Model);
            else if (devs.Any(d => d.State == "unauthorized"))
                SetState(DeviceState.Unauthorized, null, null);
            else
                SetState(DeviceState.Disconnected, null, null);
        }
        catch
        {
            SetState(DeviceState.Disconnected, null, null);
        }
    }

    private void SetState(DeviceState st, string? serial, string? model)
    {
        bool changed = st != State || serial != CurrentSerial;
        State = st; CurrentSerial = serial; CurrentModel = model;
        if (changed)
        {
            lock (_lock) CloseShell();
            StateChanged?.Invoke();
        }
    }

    public void ForceRefresh() => Task.Run(Refresh);

    // ------------------------------------------------------------ commands

    public bool Send(MediaAction action)
    {
        var serial = CurrentSerial;
        if (serial == null) return false;
        int code = MediaActionInfo.KeyCode(action);

        lock (_lock)
        {
            try
            {
                if (_shell == null || _shell.HasExited || _shellSerial != serial)
                    OpenShell(serial);
                _shell!.StandardInput.WriteLine($"input keyevent {code}");
                _shell.StandardInput.Flush();
                return true;
            }
            catch
            {
                CloseShell();
            }
        }

        // Fallback: processo avulso
        try { Run($"-s {serial} shell input keyevent {code}", 5000); return true; }
        catch { return false; }
    }

    private void OpenShell(string serial)
    {
        CloseShell();
        var psi = new ProcessStartInfo(AdbPath!, $"-s {serial} shell")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        _shell = Process.Start(psi)!;
        _shell.OutputDataReceived += (_, _) => { };
        _shell.ErrorDataReceived += (_, _) => { };
        _shell.BeginOutputReadLine();
        _shell.BeginErrorReadLine();
        _shellSerial = serial;
    }

    private void CloseShell()
    {
        try
        {
            if (_shell != null && !_shell.HasExited)
            {
                _shell.StandardInput.WriteLine("exit");
                if (!_shell.WaitForExit(300)) _shell.Kill();
            }
        }
        catch { }
        _shell?.Dispose();
        _shell = null;
        _shellSerial = null;
    }

    private string Run(string args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(AdbPath!, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } throw new TimeoutException(); }
        return outTask.Result;
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        lock (_lock) CloseShell();
    }
}
