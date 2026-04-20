using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace SysCondaWizard;

static class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            AttachConsole(-1);
        }

        if (ServiceHostRuntime.TryRun(args))
            return;

        if (args.Length > 0 && args[0] == "--status")
        {
            PrintStatus();
            return;
        }

        ApplicationConfiguration.Initialize();

        if (!IsAdministrator())
        {
            MessageBox.Show(
                "Este wizard requiere permisos de administrador.\n\nEjecuta el programa como Administrador.",
                "sys.conda — Setup Wizard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        WarnIfInstalledReleaseLooksCorrupted();
        Application.Run(new WizardForm());
    }

    static void WarnIfInstalledReleaseLooksCorrupted()
    {
        try
        {
            var cfg = WizardConfig.Load();
            if (string.IsNullOrWhiteSpace(cfg.RootDirectory) || !File.Exists(cfg.ReleaseManifestPath))
                return;

            if (!AppReleaseManifest.TryVerify(cfg, out var manifest, out var result))
                return;

            if (result.IsValid)
                return;

            MessageBox.Show(
                $"Se detectaron archivos modificados o dañados en la instalación actual de {manifest.AppName}.\n\n" +
                $"{result.ToDisplayText()}\n\n" +
                "Puedes continuar para ejecutar una actualización/reinstalación del release.",
                $"{AppProfile.AppName} — Integridad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch
        {
        }
    }

    static void PrintStatus()
    {
        var cfg = WizardConfig.Load();

        // ANSI colors
        var G = "\x1b[32m"; var R = "\x1b[31m"; var Y = "\x1b[33m";
        var C = "\x1b[36m"; var Gr = "\x1b[90m"; var W = "\x1b[97m";
        var B = "\x1b[1m"; var Rs = "\x1b[0m";

        // ── Service + process info ────────────────────────────────────────────
        string svcStatus = "not found";
        TimeSpan uptime = TimeSpan.Zero;
        long totalMemMb = 0;
        int bunPid = 0;
        int restarts = 0;

        try
        {
            using var svc = new ServiceController(cfg.ServiceName);
            svcStatus = svc.Status == ServiceControllerStatus.Running ? "online"
                      : svc.Status == ServiceControllerStatus.Stopped ? "stopped"
                      : svc.Status.ToString().ToLower();

            if (svc.Status == ServiceControllerStatus.Running)
            {
                // Host process memory
                foreach (var p in Process.GetProcessesByName("SysCondaWizard"))
                {
                    try
                    {
                        uptime = DateTime.Now - p.StartTime;
                        totalMemMb += p.WorkingSet64 / 1024 / 1024;
                    }
                    catch { }
                }

                // Bun child process
                foreach (var p in Process.GetProcessesByName("bun"))
                {
                    try
                    {
                        bunPid = p.Id;
                        totalMemMb += p.WorkingSet64 / 1024 / 1024;
                    }
                    catch { }
                }

                // Count restarts from startup log
                var startLog = Path.Combine(cfg.ServiceLogDirectory, "service-startup.log");
                if (File.Exists(startLog))
                    restarts = Math.Max(0, File.ReadAllLines(startLog).Length - 1);
            }
        }
        catch { }

        // ── Backup info ───────────────────────────────────────────────────────
        string lastBackup = "never";
        string lastBackupAge = "-";
        bool lastWasOk = false;
        int dumpCount = 0;
        double totalDumpMb = 0;
        string nextBackup = "-";

        try
        {
            var backupDir = cfg.BackupDirectory;
            var lastFile = Path.Combine(backupDir, ".last_backup");

            if (File.Exists(lastFile))
            {
                var raw = File.ReadAllText(lastFile).Trim();
                if (DateTime.TryParse(raw, out var last))
                {
                    var age = DateTime.UtcNow - last.ToUniversalTime();
                    lastBackup = last.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                    lastBackupAge = age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes}m ago"
                                  : age.TotalHours < 24 ? $"{age.TotalHours:F1}h ago"
                                  : $"{age.TotalDays:F0}d ago";
                }
            }

            if (Directory.Exists(backupDir))
            {
                var dumps = Directory.GetFiles(backupDir, "backup_cron_*.dump");
                dumpCount = dumps.Length;
                totalDumpMb = dumps.Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0;
            }

            // Check last log line for success
            var logFile = Path.Combine(backupDir, "backup.log");
            if (File.Exists(logFile))
            {
                var last5 = File.ReadAllLines(logFile).TakeLast(5).ToArray();
                lastWasOk = last5.Any(l => l.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch { }

        // ── Next backup slot ──────────────────────────────────────────────────
        if (cfg.EnableBackups && !cfg.BackupTestMode)
        {
            try
            {
                var winStart = TimeSpan.Parse(cfg.BackupWindowStart);
                var winEnd = TimeSpan.Parse(cfg.BackupWindowEnd);
                var now = DateTime.Now;
                var candidate = DateTime.Today.Add(winStart);

                while (candidate.TimeOfDay <= winEnd)
                {
                    if (candidate > now)
                    {
                        var until = candidate - now;
                        nextBackup = $"{candidate:HH:mm}  " +
                            $"(in {(int)until.TotalHours}h {until.Minutes}m)";
                        break;
                    }
                    candidate = candidate.AddHours(3);
                }
                if (nextBackup == "-")
                    nextBackup = $"tomorrow {cfg.BackupWindowStart}";
            }
            catch { }
        }
        else if (cfg.BackupTestMode)
        {
            nextBackup = $"{Y}~1 min  [TEST MODE ON]{Rs}";
        }

        // ── Log tails ─────────────────────────────────────────────────────────
        string[] backupLog = Array.Empty<string>();
        string[] appLog = Array.Empty<string>();
        string[] errLog = Array.Empty<string>();

        try
        {
            var bl = Path.Combine(cfg.BackupDirectory, "backup.log");
            if (File.Exists(bl)) backupLog = File.ReadAllLines(bl).TakeLast(5).ToArray();

            var al = Path.Combine(cfg.ServiceLogDirectory, "service-out.log");
            if (File.Exists(al)) appLog = File.ReadAllLines(al).TakeLast(5).ToArray();

            var el = Path.Combine(cfg.ServiceLogDirectory, "service-error.log");
            if (File.Exists(el)) errLog = File.ReadAllLines(el).TakeLast(3).ToArray();
        }
        catch { }

        // ── Render ────────────────────────────────────────────────────────────
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var svcColor = svcStatus == "online" ? G : R;
        var backupColor = lastWasOk ? G : Y;
        var uptimeStr = uptime == TimeSpan.Zero ? "-"
                        : uptime.TotalMinutes < 60 ? $"{(int)uptime.TotalMinutes}m"
                        : uptime.TotalHours < 24 ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
                        : $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        var memStr = totalMemMb > 0 ? $"{totalMemMb} MB" : "-";
        var pidStr = bunPid > 0 ? bunPid.ToString() : "-";

        Console.WriteLine();
        Console.WriteLine(
            $"{B}{C}  sys.conda — Process Monitor{Rs}" +
            $"                    {Gr}{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Rs}");
        Console.WriteLine($"  {new string('─', 62)}");

        // Process table
        Console.WriteLine(
            $"  {B}{"name",-18} {"status",-10} {"uptime",-12} {"memory",-10} {"pid",-8} {"restarts"}{Rs}");
        Console.WriteLine($"  {new string('─', 62)}");
        Console.WriteLine(
            $"  {W}{cfg.ServiceName,-18}{Rs}" +
            $"{svcColor}{B}{svcStatus,-10}{Rs}" +
            $"{uptimeStr,-12}" +
            $"{memStr,-10}" +
            $"{pidStr,-8}" +
            $"{restarts}");
        Console.WriteLine($"  {new string('─', 62)}");

        // Backup table
        Console.WriteLine();
        Console.WriteLine(
            $"  {B}backups{Rs}  " +
            $"{Gr}window {cfg.BackupWindowStart}-{cfg.BackupWindowEnd} " +
            $"every 3h  [{cfg.BackupDays}]{Rs}");
        Console.WriteLine($"  {new string('─', 62)}");
        Console.WriteLine(
            $"  {B}{"last",-16}{Rs}" +
            $"{backupColor}{lastBackup}{Rs}  {Gr}{lastBackupAge}{Rs}");
        Console.WriteLine($"  {"next",-18}{nextBackup}");
        Console.WriteLine($"  {"stored",-18}{dumpCount} dumps  ({totalDumpMb:F0} MB)");
        Console.WriteLine($"  {"path",-18}{Gr}{cfg.BackupDirectory}{Rs}");

        // Backup log
        if (backupLog.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  {B}backup log{Rs}");
            Console.WriteLine($"  {new string('─', 62)}");
            foreach (var line in backupLog)
            {
                var lc = line.Contains("ERROR") ? R
                       : line.Contains("SUCCESS") ? G
                       : Gr;
                Console.WriteLine($"  {lc}{line}{Rs}");
            }
        }

        // App log
        if (appLog.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  {B}app log{Rs}  {Gr}(service-out.log){Rs}");
            Console.WriteLine($"  {new string('─', 62)}");
            foreach (var line in appLog)
                Console.WriteLine($"  {Gr}{line}{Rs}");
        }

        // Error log
        if (errLog.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  {B}{R}error log{Rs}  {Gr}(service-error.log){Rs}");
            Console.WriteLine($"  {new string('─', 62)}");
            foreach (var line in errLog)
                Console.WriteLine($"  {R}{line}{Rs}");
        }

        Console.WriteLine();
    }

    static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

