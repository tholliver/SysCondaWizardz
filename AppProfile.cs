namespace SysCondaWizard;

/// <summary>
/// Per-app identity — the ONLY file you change when building a wizard for a different app.
/// Everything else (steps, service, backup, install) reads from here.
/// </summary>
public static class AppProfile
{
    // ── Branding ─────────────────────────────────────────────────────────────
    public const string AppName          = "sys.conda";
    public const string WizardTitle      = "sys.conda — Setup Wizard";

    // ── Windows Service ───────────────────────────────────────────────────────
    public const string ServiceName      = "sysconda";
    public const string ServiceDisplay   = "sys.conda App";

    // ── Database defaults ─────────────────────────────────────────────────────
    public const string DbName           = "conda_db";
    public const string DbUser           = "postgres";

    // ── Task Scheduler ────────────────────────────────────────────────────────
    public const string TaskName         = "sysconda_pg_backup";

    // ── Embedded source prefix (must match LogicalName in .csproj) ────────────
    public const string EmbedPrefix      = "sysconda-source/";

    // ── Install defaults ──────────────────────────────────────────────────────
    public const string DefaultRootDir   = @"C:\sys.conda";
    public const int    DefaultAppPort   = 4321;
}
