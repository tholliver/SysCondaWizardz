namespace SysCondaWizard;

/// <summary>
/// Per-app identity for AppTwo.
/// Edit these constants and the two PropertyGroup lines in the .csproj, then rebuild.
/// </summary>
public static class AppProfile
{
    // ── Branding ──────────────────────────────────────────────────────────────
    public const string AppName        = "AppTwo";
    public const string WizardTitle    = "AppTwo — Setup Wizard";

    // ── Windows Service ───────────────────────────────────────────────────────
    public const string ServiceName    = "apptwo";
    public const string ServiceDisplay = "AppTwo Service";

    // ── Database defaults ─────────────────────────────────────────────────────
    public const string DbName         = "apptwo_db";
    public const string DbUser         = "postgres";

    // ── Task Scheduler ────────────────────────────────────────────────────────
    public const string TaskName       = "apptwo_pg_backup";

    // ── Embedded source prefix (must match AppEmbedPrefix in .csproj) ─────────
    public const string EmbedPrefix    = "apptwo-source/";

    // ── Install defaults ──────────────────────────────────────────────────────
    public const string DefaultRootDir = @"C:\apptwo";
    public const int    DefaultAppPort = 4322;
}
