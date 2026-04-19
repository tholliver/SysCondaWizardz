using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;

namespace SysCondaWizard;

internal static class ServiceHostRuntime
{
    public static bool TryRun(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "--service", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var configPath = args[1];
        if (!File.Exists(configPath))
        {
            return true;
        }

        try
        {
            var config = JsonSerializer.Deserialize<ServiceHostConfig>(File.ReadAllText(configPath))
                ?? throw new InvalidOperationException("No se pudo leer la configuración del servicio.");

            if (Environment.UserInteractive)
            {
                using var service = new BunWindowsService(config);
                service.RunInteractive();
            }
            else
            {
                ServiceBase.Run(new BunWindowsService(config));
            }
        }
        catch (Exception ex)
        {
            ServiceStartupLogger.TryWrite(
                Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "service-startup-error.log"), ex);
            throw;
        }

        return true;
    }
}

internal sealed class BunWindowsService : ServiceBase
{
    private readonly ServiceHostConfig _config;
    private readonly object _sync = new();
    private Process? _process;
    private StreamWriter? _stdoutWriter;
    private StreamWriter? _stderrWriter;
    private bool _stopping;
    private BackupScheduler? _backup;

    public BunWindowsService(ServiceHostConfig config)
    {
        _config = config;
        ServiceName = config.ServiceName;
        CanStop = true;
        AutoLog = true;
    }

    public void RunInteractive()
    {
        OnStart(Array.Empty<string>());
        Console.WriteLine($"Service '{ServiceName}' running. Press Enter to stop.");
        Console.ReadLine();
        OnStop();
    }

    // Replace OnStart with this:
    protected override void OnStart(string[] args)
    {
        RequestAdditionalTime(30_000);
        _stopping = false;

        var t = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(_config.LogDirectory);
                var startLog = Path.Combine(_config.LogDirectory, "service-startup.log");
                File.AppendAllText(startLog,
                    $"[{DateTimeOffset.Now:u}] Starting — bun={_config.BunExecutable} entry={_config.EntryPoint} dir={_config.AppDirectory}\n");
                StartChildProcess();

                // START BACKUP AFTER PROCESS IS UP
                var cfg = WizardConfig.Load();
                var diagLog = Path.Combine(_config.LogDirectory, "backup-scheduler.log");
                File.AppendAllText(diagLog,
                    $"[{DateTimeOffset.Now:u}] BackupScheduler starting — " +
                    $"EnableBackups={cfg.EnableBackups} TestMode={cfg.BackupTestMode} " +
                    $"BackupWindowStart={cfg.BackupWindowStart} BackupWindowEnd={cfg.BackupWindowEnd} " +
                    $"PgDumpPath={cfg.PgDumpPath} ConfigFile={WizardConfig.ConfigFilePath}\n");
                _backup = new BackupScheduler(cfg);
                _backup.Start();
                File.AppendAllText(diagLog, $"[{DateTimeOffset.Now:u}] BackupScheduler.Start() completed\n");
            }
            catch (Exception ex)
            {
                ServiceStartupLogger.TryWrite(_config.LogDirectory + "\\service-startup-error.log", ex);
                Stop();
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    protected override void OnStop()
    {
        _backup?.Dispose();
        _stopping = true;
        StopChildProcess();
        DisposeWriters();
    }

    private void StartChildProcess()
    {
        lock (_sync)
        {
            DisposeProcess();
            Directory.CreateDirectory(_config.LogDirectory);
            _stdoutWriter = CreateWriter(Path.Combine(_config.LogDirectory, "service-out.log"));
            _stderrWriter = CreateWriter(Path.Combine(_config.LogDirectory, "service-error.log"));

            var psi = new ProcessStartInfo
            {
                FileName = _config.BunExecutable,
                Arguments = $"run \"{_config.EntryPoint}\"",
                WorkingDirectory = _config.AppDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var pair in EnvFileLoader.Load(_config.EnvFilePath))
            {
                psi.Environment[pair.Key] = pair.Value;
            }

            psi.Environment["NODE_ENV"] = "production";
            psi.Environment["HOST"] = "0.0.0.0";
            psi.Environment["PORT"] = _config.Port;

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, e) => WriteLine(_stdoutWriter, e.Data);
            _process.ErrorDataReceived += (_, e) => WriteLine(_stderrWriter, e.Data);
            _process.Exited += (_, _) => HandleUnexpectedExit();
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            WriteLine(_stdoutWriter, $"[{DateTimeOffset.Now:u}] Service host started process PID {_process.Id}.");
        }
    }

    private void HandleUnexpectedExit()
    {
        lock (_sync)
        {
            if (_process == null)
            {
                return;
            }

            var exitCode = _process.ExitCode;
            WriteLine(_stderrWriter, $"[{DateTimeOffset.Now:u}] Bun exited with code {exitCode}.");
            DisposeProcess();
            DisposeWriters();

            if (_stopping)
            {
                return;
            }
        }

        Thread.Sleep(TimeSpan.FromSeconds(Math.Max(1, _config.RestartDelaySeconds)));

        if (!_stopping)
        {
            StartChildProcess();
        }
    }

    private void StopChildProcess()
    {
        lock (_sync)
        {
            if (_process == null)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(15000);
                }
            }
            catch
            {
            }

            DisposeProcess();
        }
    }

    private static StreamWriter CreateWriter(string path)
    {
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        return new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    private static void WriteLine(StreamWriter? writer, string? line)
    {
        if (writer == null || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        writer.WriteLine(line);
    }

    private void DisposeProcess()
    {
        _process?.Dispose();
        _process = null;
    }

    private void DisposeWriters()
    {
        _stdoutWriter?.Dispose();
        _stderrWriter?.Dispose();
        _stdoutWriter = null;
        _stderrWriter = null;
    }
}

internal static class ServiceStartupLogger
{
    public static void TryWrite(string logPath, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:u}] {ex}\r\n{ex.StackTrace}\r\n\r\n",
                new System.Text.UTF8Encoding(false));
        }
        catch { }
    }
}

internal sealed class ServiceHostConfig
{
    public string ServiceName { get; set; } = "sysconda";
    public string AppDirectory { get; set; } = "";
    public string BunExecutable { get; set; } = "bun";
    public string EntryPoint { get; set; } = "";
    public string EnvFilePath { get; set; } = "";
    public string LogDirectory { get; set; } = "";
    public string Port { get; set; } = "4321";
    public int RestartDelaySeconds { get; set; } = 5;
}

internal static class EnvFileLoader
{
    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            values[key] = value;
        }

        return values;
    }
}
