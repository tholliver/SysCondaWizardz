using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace SysCondaWizard;

/// <summary>
/// Runs pg_dump on a schedule directly from the Windows Service.
/// Fires every 3 hours starting at BackupWindowStart, only within
/// the configured window (default 08:00-17:00 Bolivia time).
/// Tiered rotation: 7 daily + 4 weekly + 3 monthly.
/// </summary>
public sealed class BackupScheduler : IDisposable
{
    private Timer? _timerScheduled;
    private Timer? _timerTest;
    private readonly WizardConfig _cfg;

    public BackupScheduler(WizardConfig cfg) => _cfg = cfg;

    public void Start()
    {
        if (!_cfg.EnableBackups) return;

        if (_cfg.BackupTestMode)
        {
            // Fire after 10s then every minute — ignores window
            _timerTest = new Timer(_ => RunBackup("test"), null,
                TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
            return;
        }

        // Catch-up: if last backup was 20+ hours ago fire in 15s
        TriggerCatchUpIfNeeded();

        // Main timer: every 3 hours starting at window start (08:00)
        // Window gate inside RunBackup blocks shots outside 08:00-17:00
        _timerScheduled = new Timer(_ => RunBackup("scheduled"),
            null, TimeUntilNext(_cfg.BackupWindowStart), TimeSpan.FromHours(3));
    }

    private void TriggerCatchUpIfNeeded()
    {
        try
        {
            var lastBackupFile = Path.Combine(_cfg.BackupDirectory, ".last_backup");
            if (!File.Exists(lastBackupFile)) return;

            var raw = File.ReadAllText(lastBackupFile).Trim();
            if (!DateTime.TryParse(raw, out var lastBackup)) return;

            var hoursSinceLast = (DateTime.UtcNow - lastBackup.ToUniversalTime()).TotalHours;
            if (hoursSinceLast >= 20)
            {
                // Missed at least one cycle — fire catch-up in 15s (once only)
                new Timer(_ => RunBackup("catchup"), null,
                    TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
            }
        }
        catch { }
    }

    private void RunBackup(string tag)
    {
        if (!_cfg.BackupTestMode && !IsTodayScheduled()) return;
        if (!_cfg.BackupTestMode && !IsWithinBackupWindow()) return;

        var dir = _cfg.BackupDirectory;
        Directory.CreateDirectory(dir);

        var logFile = Path.Combine(dir, "backup.log");
        void Log(string msg)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{tag.ToUpper()}] {msg}";
            try { File.AppendAllText(logFile, line + Environment.NewLine); } catch { }
        }

        var fileName = Path.Combine(dir,
            $"backup_cron_{DateTime.Now:yyyy-MM-ddTHH-mm-ss}.dump");
        Log($"Starting backup: {Path.GetFileName(fileName)}");

        try
        {
            if (!CanConnectToDb(out var connErr))
                throw new Exception($"DB unreachable: {connErr}");

            var psi = new ProcessStartInfo
            {
                FileName = _cfg.PgDumpPath,
                Arguments = $"-h {_cfg.DbHost} -p {_cfg.DbPort} -U {_cfg.DbUser}" +
                            $" -F c --data-only {_cfg.DbName} --file=\"{fileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            psi.Environment["PGPASSWORD"] = _cfg.DbPassword;

            using var proc = Process.Start(psi)!;
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
                throw new Exception($"pg_dump exit {proc.ExitCode}: {stderr.Trim()}");

            var size = new FileInfo(fileName).Length;
            if (size < 512)
            {
                File.Delete(fileName);
                throw new Exception($"Dump too small ({size} bytes) - DB may be empty");
            }

            RotateBackups(dir);

            File.WriteAllText(Path.Combine(dir, ".last_backup"),
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));

            Log($"SUCCESS: {Path.GetFileName(fileName)} - {size / 1024.0:F1} KB");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            if (File.Exists(fileName))
                try { File.Delete(fileName); } catch { }
        }
    }

    // ── Window gate 08:00-17:00 ───────────────────────────────────────────────

    private bool IsWithinBackupWindow()
    {
        var now = DateTime.Now.TimeOfDay;
        var start = ParseTime(_cfg.BackupWindowStart);
        var end = ParseTime(_cfg.BackupWindowEnd);
        return now >= start && now <= end;
    }

    private static TimeSpan ParseTime(string timeStr)
    {
        var parts = timeStr.Split(':');
        return new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), 0);
    }

    // ── DB connectivity pre-check ─────────────────────────────────────────────

    private bool CanConnectToDb(out string error)
    {
        error = string.Empty;
        try
        {
            var psqlDir = Path.GetDirectoryName(_cfg.PgDumpPath) ?? "";
            var psql = Path.Combine(psqlDir, "psql.exe");
            if (!File.Exists(psql)) return true;

            var psi = new ProcessStartInfo
            {
                FileName = psql,
                Arguments = $"-h {_cfg.DbHost} -p {_cfg.DbPort} -U {_cfg.DbUser}" +
                            $" -d {_cfg.DbName} -c \"SELECT 1\" -t -q",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["PGPASSWORD"] = _cfg.DbPassword;

            using var p = Process.Start(psi)!;
            p.WaitForExit(10_000);
            if (p.ExitCode != 0)
            {
                error = p.StandardError.ReadToEnd().Trim();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ── Tiered rotation ───────────────────────────────────────────────────────

    private static void RotateBackups(string dir)
    {
        var files = Directory.GetFiles(dir, "backup_cron_*.dump")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;

        // Keep last 7 dailies
        files.Take(7).ToList().ForEach(f => keep.Add(f.FullName));

        // Keep 1 per week for last 4 weeks
        for (int w = 1; w <= 4; w++)
        {
            var weekStart = now.AddDays(-w * 7);
            var weekly = files.FirstOrDefault(f =>
                f.LastWriteTime >= weekStart &&
                f.LastWriteTime < weekStart.AddDays(7));
            if (weekly != null) keep.Add(weekly.FullName);
        }

        // Keep 1 per month for last 3 months
        for (int m = 1; m <= 3; m++)
        {
            var monthStart = now.AddMonths(-m);
            var monthly = files.FirstOrDefault(f =>
                f.LastWriteTime.Year == monthStart.Year &&
                f.LastWriteTime.Month == monthStart.Month);
            if (monthly != null) keep.Add(monthly.FullName);
        }

        files.Where(f => !keep.Contains(f.FullName))
             .ToList()
             .ForEach(f => { try { f.Delete(); } catch { } });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsTodayScheduled()
    {
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
        return scheduled.Contains(DateTime.Now.DayOfWeek);
    }

    private static TimeSpan TimeUntilNext(string timeStr)
    {
        var parts = timeStr.Split(':');
        var target = DateTime.Today
            .AddHours(int.Parse(parts[0]))
            .AddMinutes(int.Parse(parts[1]));

        // If missed by less than 60 minutes fire in 30s instead of waiting
        var diff = DateTime.Now - target;
        if (diff > TimeSpan.Zero && diff < TimeSpan.FromMinutes(60))
            return TimeSpan.FromSeconds(30);

        if (target <= DateTime.Now) target = target.AddDays(1);
        return target - DateTime.Now;
    }

    public void Dispose()
    {
        _timerTest?.Dispose();
        _timerScheduled?.Dispose();
    }
}
