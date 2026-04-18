using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace SysCondaWizard;

/// <summary>
/// Runs pg_dump on a schedule directly from the Windows Service.
/// Supports test mode (every minute), single or dual daily triggers,
/// day-of-week filtering, and tiered rotation (7 daily / 4 weekly / 3 monthly).
/// </summary>
public sealed class BackupScheduler : IDisposable
{
    private Timer? _timerMorning;
    private Timer? _timerEvening;
    private Timer? _timerTest;
    private readonly WizardConfig _cfg;

    public BackupScheduler(WizardConfig cfg) => _cfg = cfg;

    public void Start()
    {
        if (!_cfg.EnableBackups) return;

        if (_cfg.BackupTestMode)
        {
            // Fire after 10s, then every minute
            _timerTest = new Timer(_ => RunBackup("test"), null,
                TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
            return;
        }

        // Primary trigger — user configured time (e.g. 18:30)
        _timerMorning = new Timer(_ => RunBackup("scheduled"),
            null, TimeUntilNext(_cfg.BackupTime), TimeSpan.FromHours(24));

        // Secondary trigger — morning snapshot at 06:00 for financial safety
        _timerEvening = new Timer(_ => RunBackup("morning"),
            null, TimeUntilNext(_cfg.BackupTimeMorning), TimeSpan.FromHours(24));
    }

    private void RunBackup(string tag)
    {
        // Day-of-week gate (skip on non-scheduled days in normal mode)
        if (!_cfg.BackupTestMode && !IsTodayScheduled()) return;

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
            // Check DB connectivity before dumping
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

            // Tiered rotation: 7 daily + 4 weekly + 3 monthly
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

    // ── DB connectivity pre-check ─────────────────────────────────────────────

    private bool CanConnectToDb(out string error)
    {
        error = string.Empty;
        try
        {
            var psqlDir = Path.GetDirectoryName(_cfg.PgDumpPath) ?? "";
            var psql = Path.Combine(psqlDir, "psql.exe");
            if (!File.Exists(psql)) return true; // skip check if psql not found

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
            p.WaitForExit(10_000); // 10s timeout
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

        // Delete everything not in keep set
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
        if (target <= DateTime.Now) target = target.AddDays(1);
        return target - DateTime.Now;
    }

    public void Dispose()
    {
        _timerTest?.Dispose();
        _timerMorning?.Dispose();
        _timerEvening?.Dispose();
    }
}
