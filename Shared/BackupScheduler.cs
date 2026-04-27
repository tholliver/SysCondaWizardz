using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace SysCondaWizard;

/// <summary>
/// Runs pg_dump on a fixed 4-shot-per-day schedule spread evenly across the configured window.
/// Window default: 08:00–18:00 BOT  →  shots at 08:00 · 11:20 · 14:40 · 18:00
/// All times are in Bolivia Time (UTC-4, no DST).
/// Tiered rotation: 7 daily + 4 weekly + 3 monthly.
///
/// Scheduling contract
/// ───────────────────
/// • Every shot is computed from absolute wall-clock anchors, never by adding an interval
///   to "when the last timer fired" — so drift is impossible.
/// • If the service starts between two anchored shots the next upcoming slot is used.
/// • If ALL today's shots have already passed the first shot of tomorrow is scheduled.
/// • A catch-up fires only when a backup is genuinely overdue AND the current time is
///   already inside the window — otherwise it waits for the next window-open slot.
/// • pg_dump runs with an overlap lock. The app service process is never stopped —
///   pg_dump reads from a live Postgres instance safely (read-only dump).
/// </summary>
public sealed class BackupScheduler : IDisposable
{
    // Bolivia is UTC-4 (no DST)
    private static readonly TimeZoneInfo BoliviaZone =
        TimeZoneInfo.CreateCustomTimeZone("BOT", TimeSpan.FromHours(-4), "Bolivia Time", "Bolivia Time");

    private readonly WizardConfig _cfg;
    private Timer? _timer;
    private int _backupRunning; // 0 = idle, 1 = running  (Interlocked flag)

    public BackupScheduler(WizardConfig cfg) => _cfg = cfg;

    // ── Public API ────────────────────────────────────────────────────────────

    public void Start()
    {
        if (!_cfg.EnableBackups) return;

        Directory.CreateDirectory(_cfg.BackupDirectory);

        // Schedule the very first tick; all subsequent ticks are re-armed inside the callback.
        var delay = ComputeDelayToNextSlot(out var label, allowCatchup: true);
        LogScheduler($"Scheduler started. First trigger '{label}' in {delay:c}.");
        _timer = new Timer(_ => OnTick(), null, delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    // ── Core tick ─────────────────────────────────────────────────────────────

    private void OnTick()
    {
        try
        {
            RunBackup("scheduled");
        }
        finally
        {
            // Always re-arm, even if RunBackup threw or skipped.
            RearmTimer();
        }
    }

    private void RearmTimer()
    {
        if (_timer == null) return;
        var delay = ComputeDelayToNextSlot(out var label, allowCatchup: false);
        LogScheduler($"Next backup slot '{label}' in {delay:c}.");
        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    // ── Slot math ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the 4 absolute shot times for a given Bolivia calendar date.
    /// Shots are evenly spaced: windowStart, +1/3 span, +2/3 span, windowEnd.
    /// </summary>
    private DateTime[] ShotsForDay(DateTime boliviaDate)
    {
        var start = ParseTime(_cfg.BackupWindowStart);
        var end   = ParseTime(_cfg.BackupWindowEnd);
        var span  = end - start;
        if (span <= TimeSpan.Zero) span = TimeSpan.FromHours(10); // safety fallback

        var origin = boliviaDate.Date + start;
        var step   = TimeSpan.FromTicks(span.Ticks / 3);
        return new[]
        {
            origin,
            origin + step,
            origin + step * 2,
            origin + step * 3,   // == windowEnd
        };
    }

    /// <summary>
    /// Computes how long to wait until the next valid shot slot.
    /// When <paramref name="allowCatchup"/> is true, also checks whether a catch-up
    /// backup is needed (last backup >= 20 h ago) and if so fires in 15 s — but
    /// only if we are currently inside the window; otherwise the first in-window slot
    /// is used as the catch-up opportunity.
    /// </summary>
    private TimeSpan ComputeDelayToNextSlot(out string label, bool allowCatchup)
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone);

        // Catch-up path: overdue AND already inside the window right now.
        if (allowCatchup && IsBackupOverdue() && IsWithinBackupWindow(now.TimeOfDay))
        {
            label = "catchup";
            return TimeSpan.FromSeconds(15);
        }

        // Walk today's shots, then tomorrow's, to find the next slot strictly after now.
        foreach (var day in new[] { now.Date, now.Date.AddDays(1) })
        {
            foreach (var shot in ShotsForDay(day))
            {
                if (shot > now)
                {
                    label = shot.ToString("HH:mm");
                    var shotUtc = TimeZoneInfo.ConvertTimeToUtc(shot, BoliviaZone);
                    var delay   = shotUtc - DateTime.UtcNow;
                    return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
                }
            }
        }

        // Should never reach here (tomorrow always has future shots) but be safe.
        label = "fallback";
        return TimeSpan.FromHours(1);
    }

    // ── Window / schedule gates ───────────────────────────────────────────────

    private bool IsWithinBackupWindow(TimeSpan timeOfDay)
    {
        var start = ParseTime(_cfg.BackupWindowStart);
        var end   = ParseTime(_cfg.BackupWindowEnd);
        return timeOfDay >= start && timeOfDay <= end;
    }

    private bool IsTodayScheduled()
    {
        var dayMap = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
        {
            ["MON"] = DayOfWeek.Monday,
            ["TUE"] = DayOfWeek.Tuesday,
            ["WED"] = DayOfWeek.Wednesday,
            ["THU"] = DayOfWeek.Thursday,
            ["FRI"] = DayOfWeek.Friday,
            ["SAT"] = DayOfWeek.Saturday,
            ["SUN"] = DayOfWeek.Sunday,
        };
        var scheduled = _cfg.BackupDays
            .Split(',')
            .Select(d => dayMap.GetValueOrDefault(d.Trim()))
            .ToHashSet();

        var boliviaToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone).DayOfWeek;
        return scheduled.Contains(boliviaToday);
    }

    private bool IsBackupOverdue()
    {
        try
        {
            var marker = Path.Combine(_cfg.BackupDirectory, ".last_backup");
            if (!File.Exists(marker)) return true;

            var raw = File.ReadAllText(marker).Trim();
            if (!DateTime.TryParse(raw, out var last)) return true;

            return (DateTime.UtcNow - last.ToUniversalTime()).TotalHours >= 20;
        }
        catch { return false; }
    }

    // ── pg_dump execution ─────────────────────────────────────────────────────

    private void RunBackup(string tag)
    {
        // Overlap guard — only one dump at a time.
        if (Interlocked.CompareExchange(ref _backupRunning, 1, 0) != 0)
        {
            LogScheduler("Skipping — previous backup still in progress.");
            return;
        }

        try
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone);

            if (!IsTodayScheduled())
            {
                LogScheduler($"Skipping — today ({now.DayOfWeek}) is not in BackupDays.");
                return;
            }

            if (!IsWithinBackupWindow(now.TimeOfDay))
            {
                // This should not normally happen because the scheduler only fires at window slots,
                // but guard anyway (e.g. system clock jumped, heavy load delay).
                LogScheduler($"Skipping — current time {now:HH:mm} is outside window " +
                             $"{_cfg.BackupWindowStart}–{_cfg.BackupWindowEnd}.");
                return;
            }

            ExecuteDump(tag, now);
        }
        finally
        {
            Interlocked.Exchange(ref _backupRunning, 0);
        }
    }

    private void ExecuteDump(string tag, DateTime boliviaNow)
    {
        var dir = _cfg.BackupDirectory;
        Directory.CreateDirectory(dir);

        var logFile  = Path.Combine(dir, "backup.log");
        var fileName = Path.Combine(dir, $"backup_cron_{boliviaNow:yyyy-MM-ddTHH-mm-ss}.dump");

        void Log(string msg)
        {
            var line = $"[{boliviaNow:yyyy-MM-dd HH:mm:ss} BOT] [{tag.ToUpper()}] {msg}";
            try { File.AppendAllText(logFile, line + Environment.NewLine); } catch { }
        }

        Log($"Starting: {Path.GetFileName(fileName)}");

        try
        {
            if (!CanConnectToDb(out var connErr))
                throw new Exception($"DB unreachable: {connErr}");

            var psi = new ProcessStartInfo
            {
                FileName  = _cfg.PgDumpPath,
                Arguments = $"-h {_cfg.DbHost} -p {_cfg.DbPort} -U {_cfg.DbUser}" +
                            $" -F c --data-only {_cfg.DbName} --file=\"{fileName}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardError  = true,
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
                throw new Exception($"Dump suspiciously small ({size} bytes) — DB may be empty");
            }

            RotateBackups(dir);

            File.WriteAllText(
                Path.Combine(dir, ".last_backup"),
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));

            Log($"SUCCESS: {Path.GetFileName(fileName)} — {size / 1024.0:F1} KB");
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
            var psql    = Path.Combine(psqlDir, "psql.exe");
            if (!File.Exists(psql)) return true; // assume reachable if psql not found

            var psi = new ProcessStartInfo
            {
                FileName               = psql,
                Arguments              = $"-h {_cfg.DbHost} -p {_cfg.DbPort} -U {_cfg.DbUser}" +
                                         $" -d {_cfg.DbName} -c \"SELECT 1\" -t -q",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
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
        var now  = DateTime.Now;

        // 7 most-recent dailies
        files.Take(7).ToList().ForEach(f => keep.Add(f.FullName));

        // 4 weekly representatives
        for (int w = 1; w <= 4; w++)
        {
            var weekStart = now.AddDays(-w * 7);
            var weekly = files.FirstOrDefault(f =>
                f.LastWriteTime >= weekStart && f.LastWriteTime < weekStart.AddDays(7));
            if (weekly != null) keep.Add(weekly.FullName);
        }

        // 3 monthly representatives
        for (int m = 1; m <= 3; m++)
        {
            var monthStart = now.AddMonths(-m);
            var monthly = files.FirstOrDefault(f =>
                f.LastWriteTime.Year  == monthStart.Year &&
                f.LastWriteTime.Month == monthStart.Month);
            if (monthly != null) keep.Add(monthly.FullName);
        }

        files.Where(f => !keep.Contains(f.FullName))
             .ToList()
             .ForEach(f => { try { f.Delete(); } catch { } });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TimeSpan ParseTime(string timeStr)
    {
        var parts = timeStr.Split(':');
        return new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), 0);
    }

    private void LogScheduler(string msg)
    {
        try
        {
            Directory.CreateDirectory(_cfg.BackupDirectory);
            var boliviaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BoliviaZone);
            var line = $"[{boliviaNow:yyyy-MM-dd HH:mm:ss} BOT] [SCHEDULER] {msg}";
            File.AppendAllText(Path.Combine(_cfg.BackupDirectory, "backup.log"),
                line + Environment.NewLine);
        }
        catch { }
    }
}
