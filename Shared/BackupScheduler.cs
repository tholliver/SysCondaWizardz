using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace SysCondaWizard;

public sealed class BackupScheduler : IDisposable
{
    private Timer? _timer;
    private readonly WizardConfig _cfg;
    private readonly string _scriptPath; // still used to locate pg_dump path

    public BackupScheduler(WizardConfig cfg)
    {
        _cfg = cfg;
        _scriptPath = cfg.PgDumpPath;
    }

    public void Start()
    {
        if (!_cfg.EnableBackups) return;

        var interval = _cfg.BackupTestMode
            ? TimeSpan.FromMinutes(1)
            : TimeSpan.FromHours(24);

        // first run: align to scheduled time if not test mode
        var dueTime = _cfg.BackupTestMode
            ? TimeSpan.FromSeconds(10)
            : TimeUntilNextRun(_cfg.BackupTime, _cfg.BackupDays);

        _timer = new Timer(_ => RunBackup(), null, dueTime, interval);
    }

    private void RunBackup()
    {
        // skip if today is not a scheduled day (only matters in non-test mode)
        if (!_cfg.BackupTestMode)
        {
            var today = DateTime.Now.DayOfWeek;
            var dayMap = new Dictionary<string, DayOfWeek>
            {
                ["MON"] = DayOfWeek.Monday,
                ["TUE"] = DayOfWeek.Tuesday,
                ["WED"] = DayOfWeek.Wednesday,
                ["THU"] = DayOfWeek.Thursday,
                ["FRI"] = DayOfWeek.Friday,
                ["SAT"] = DayOfWeek.Saturday,
                ["SUN"] = DayOfWeek.Sunday,
            };
            var scheduled = _cfg.BackupDays.Split(',')
                .Select(d => dayMap.GetValueOrDefault(d.Trim()))
                .ToHashSet();
            if (!scheduled.Contains(today)) return;
        }

        var dir = _cfg.BackupDirectory;
        Directory.CreateDirectory(dir);

        var logFile = Path.Combine(dir, "backup.log");
        void Log(string msg)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}";
            File.AppendAllText(logFile, line + Environment.NewLine);
        }

        var fileName = Path.Combine(dir, $"backup_cron_{DateTime.Now:yyyy-MM-ddTHH-mm-ss}.dump");
        Log($"Starting backup: {fileName}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _cfg.PgDumpPath,
                Arguments = $"-h {_cfg.DbHost} -p {_cfg.DbPort} -U {_cfg.DbUser} -F c --data-only {_cfg.DbName} --file=\"{fileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            psi.Environment["PGPASSWORD"] = _cfg.DbPassword;

            using var proc = Process.Start(psi)!;
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
                throw new Exception($"pg_dump failed (code {proc.ExitCode}): {stderr}");

            var size = new FileInfo(fileName).Length;
            if (size < 512)
            {
                File.Delete(fileName);
                throw new Exception($"Dump too small ({size} bytes)");
            }

            // rotate
            Directory.GetFiles(dir, "backup_cron_*.dump")
                .OrderByDescending(f => f)
                .Skip(_cfg.KeepFiles)
                .ToList()
                .ForEach(File.Delete);

            File.WriteAllText(Path.Combine(dir, ".last_backup"),
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));

            Log($"SUCCESS: {fileName} - {size / 1024.0:F1} KB");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            if (File.Exists(fileName)) File.Delete(fileName);
        }
    }

    private static TimeSpan TimeUntilNextRun(string timeStr, string days)
    {
        var parts = timeStr.Split(':');
        var target = DateTime.Today.AddHours(int.Parse(parts[0]))
                                   .AddMinutes(int.Parse(parts[1]));
        if (target < DateTime.Now) target = target.AddDays(1);
        return target - DateTime.Now;
    }

    public void Dispose() => _timer?.Dispose();
}
