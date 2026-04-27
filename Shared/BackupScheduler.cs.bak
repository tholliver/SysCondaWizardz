using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace SysCondaWizard;

/// <summary>
/// Runs pg_dump on a schedule directly from the Windows Service.
/// Always fires exactly 4 times per day, evenly spread across the configured window.
/// Interval = (windowEnd - windowStart) / 3, so shots land at: start, +1/3, +2/3, end.
/// All times are in Bolivia time (UTC-4, no DST).
/// Tiered rotation: 7 daily + 4 weekly + 3 monthly.
/// </summary>
public sealed class BackupScheduler : IDisposable
{
    private Timer? _timerScheduled;
    private Timer? _timerCatchup;
    private readonly WizardConfig _cfg;

    // Bolivia is UTC-4 (no DST)
    private static readonly TimeZoneInfo BoliviaZone =
        TimeZoneInfo.CreateCustomTimeZone("BOT", TimeSpan.FromHours(-4), "Bolivia Time", "Bolivia Time");

    public BackupScheduler(WizardConfig cfg) => _cfg = cfg;

    // Interval is always (window duration / 3) so 4 shots fit exactly
    private TimeSpan ComputeInterval()
    {
        var start = ParseTime(_cfg.BackupWindowStart);
        var end = ParseTime(_cfg.BackupWindowEnd);
        var span = end - start;
        if (span <= TimeSpan.Zero) span = TimeSpan.FromHours(10); // fallback
        return TimeSpan.FromTicks(span.Ticks / 3);
    }

    public void Start()
    {
        if (!_cfg.EnableBackups) return;

        Directory.CreateDirectory(_cfg.BackupDirectory);

        // Catch-up: if last backup was 20+ hours ago (or never), fire in 15s
        TriggerCatchUpIfNeeded();

        _timerScheduled = new Timer(_ => RunScheduledBackup(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        ScheduleNextScheduledRun("start");
    }

    private void TriggerCatchUpIfNeeded()
    {
        try
        {
            var lastBackupFile = Path.Combine(_cfg.BackupDirectory, ".last_backup");

            if (!File.Exists(lastBackupFile))
            {
                ScheduleCatchup();
                return;
            }

            var raw = File.ReadAllText(lastBackupFile).Trim();
            if (!DateTime.TryParse(raw, out var lastBackup)) return;

            var hoursSinceLast = (DateTime.UtcNow - lastBackup.ToUniversalTime()).TotalHours;
            if (hoursSinceLast >= 20)
                ScheduleCatchup();
        }
        catch { }
    }

    private void ScheduleCatchup()
    {
        // Must be stored — a local var would be GC'd before it fires
        LogScheduler("Catch-up backup scheduled for 15 seconds from now.");
        _timerCatchup = new Timer(_ =>
        {
            RunBackup("catchup");
            _timerCatchup?.Dispose();
            _timerCatchup = null;
        }, null, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);
    }

    private void RunScheduledBackup()
    {
        try
        {
            RunBackup("scheduled");
        }
        finally
        {
            ScheduleNextScheduledRun("reschedule");
        }
    }

    private void ScheduleNextScheduledRun(string reason)
    {
        if (_timerScheduled == null) return;

        var interval = ComputeInterval();
        var delay = TimeUntilNext(_cfg.BackupWindowStart, interval);
        _timerScheduled.Change(delay, Timeout.InfiniteTimeSpan);
        LogScheduler($"Scheduled next backup ({reason}) in {delay:c} for window {_cfg.BackupWindowStart}-{_cfg.BackupWindowEnd}.");
    }
    private void RunBackup(string tag)
    {
        if (!IsTodayScheduled()) return;
        if (!IsWithinBackupWindow()) return;

        var dir = _cfg.BackupDirectory;
        Directory.CreateDirectory(dir);

        var logFile = Path.Combine(dir, "backup.log");
        void Log(string msg)
        {
            var boliviaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone);
            var line = $"[{boliviaNow:yyyy-MM-dd HH:mm:ss} BOT] [{tag.ToUpper()}] {msg}";
            try { File.AppendAllText(logFile, line + Environment.NewLine); } catch { }
        }

        var boliviaTs = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone);
        var fileName = Path.Combine(dir, $"backup_cron_{boliviaTs:yyyy-MM-ddTHH-mm-ss}.dump");
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

    // ── Window gate ───────────────────────────────────────────────────────────

    private bool IsWithinBackupWindow()
    {
        var boliviaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone).TimeOfDay;
        var start = ParseTime(_cfg.BackupWindowStart);
        var end = ParseTime(_cfg.BackupWindowEnd);
        return boliviaNow >= start && boliviaNow <= end;
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

        files.Take(7).ToList().ForEach(f => keep.Add(f.FullName));

        for (int w = 1; w <= 4; w++)
        {
            var weekStart = now.AddDays(-w * 7);
            var weekly = files.FirstOrDefault(f =>
                f.LastWriteTime >= weekStart && f.LastWriteTime < weekStart.AddDays(7));
            if (weekly != null) keep.Add(weekly.FullName);
        }

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
        var boliviaToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone).DayOfWeek;
        return scheduled.Contains(boliviaToday);
    }

    /// <summary>
    /// Finds the next shot time from the 4-shot schedule and returns the delay to it.
    /// If the service starts mid-window it snaps to the next upcoming slot.
    /// </summary>
    private static TimeSpan TimeUntilNext(string windowStartStr, TimeSpan interval)
    {
        var boliviaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone);
        var windowStart = ParseTime(windowStartStr);
        var firstShot = boliviaNow.Date + windowStart;

        // Walk the 4-shot schedule: start, +interval, +2*interval, +3*interval
        DateTime nextShot = firstShot.AddDays(1); // fallback: tomorrow
        for (int i = 0; i < 4; i++)
        {
            var candidate = firstShot.Add(TimeSpan.FromTicks(interval.Ticks * i));
            if (candidate > boliviaNow)
            {
                nextShot = candidate;
                break;
            }
        }

        var nextShotUtc = TimeZoneInfo.ConvertTimeToUtc(nextShot, BoliviaZone);
        var delay = nextShotUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.FromSeconds(1) : delay;
    }

    private void LogScheduler(string msg)
    {
        try
        {
            Directory.CreateDirectory(_cfg.BackupDirectory);
            var boliviaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone);
            var line = $"[{boliviaNow:yyyy-MM-dd HH:mm:ss} BOT] [SCHEDULER] {msg}";
            File.AppendAllText(Path.Combine(_cfg.BackupDirectory, "backup.log"), line + Environment.NewLine);
        }
        catch { }
    }

    public void Dispose()
    {
        _timerScheduled?.Dispose();
        _timerCatchup?.Dispose();
    }
}


