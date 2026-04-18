namespace SysCondaWizard;

public enum AppSourceKind
{
    ExistingDirectory, // reused to mean "embedded" source
    ZipArchive,
    GitRepository
}

/// <summary>All user inputs collected across wizard steps.</summary>
public class WizardConfig
{
    // ── Step 1: Install root + source ────────────────────────────────────────
    public string RootDirectory { get; set; } = AppProfile.DefaultRootDir;
    public AppSourceKind AppSource { get; set; } = AppSourceKind.ExistingDirectory;
    public string SourceZipPath { get; set; } = "";
    public string GitRepoUrl { get; set; } = "";

    // ── Step 2: Environment (.env) ───────────────────────────────────────────
    public string DbHost { get; set; } = "localhost";
    public string DbPort { get; set; } = "5432";
    public string DbName { get; set; } = AppProfile.DbName;
    public string DbUser { get; set; } = AppProfile.DbUser;
    public string DbPassword { get; set; } = "";
    public string AppUrl { get; set; } = $"http://localhost:{AppProfile.DefaultAppPort}/";
    public string AppPort { get; set; } = AppProfile.DefaultAppPort.ToString();
    public string BetterAuthSecret { get; set; } = "";
    public string RateLimitWindow { get; set; } = "900";
    public string MaxAttempts { get; set; } = "5";

    // ── Step 3: Service ──────────────────────────────────────────────────────
    public string ServiceName { get; set; } = AppProfile.ServiceName;
    public string ServiceDisplayName { get; set; } = AppProfile.ServiceDisplay;
    public bool InstallAsService { get; set; } = true;
    public int ServiceRestartDelaySeconds { get; set; } = 5;

    // ── Step 4: Backup ───────────────────────────────────────────────────────
    public bool EnableBackups { get; set; } = true;
    public string PgDumpPath { get; set; } = PostgresBinaryLocator.FindPgDumpPath();
    public string PgRestorePath { get; set; } = PostgresBinaryLocator.FindPgRestorePath();
    public int KeepFiles { get; set; } = 10;
    public string BackupTime { get; set; } = "18:30";
    public string BackupDays { get; set; } = "MON,TUE,WED,THU,FRI";
    public string TaskSchedulerTaskName { get; set; } = AppProfile.TaskName;
    public bool RestoreDatabaseOnInstall { get; set; } = false;
    public string RestoreDumpPath { get; set; } = "";

    // ── Derived paths (all rooted under RootDirectory) ───────────────────────
    public string BunDirectory            => Path.Combine(RootDirectory, "bun");
    public string BunExePath              => Path.Combine(BunDirectory, "bun.exe");
    public string AppDirectory            => Path.Combine(RootDirectory, "app");
    public string ServiceRuntimeDirectory => Path.Combine(RootDirectory, "runtime");
    public string ServiceConfigPath       => Path.Combine(ServiceRuntimeDirectory, "service-config.json");
    public string ServiceLogDirectory     => Path.Combine(RootDirectory, "logs");
    public string BackupDirectory         => Path.Combine(RootDirectory, "backups");

    // ── Env file ─────────────────────────────────────────────────────────────
    public string DatabaseUrl =>
        $"postgresql://{DbUser}:{DbPassword}@{DbHost}:{DbPort}/{DbName}";

    public string EnvFileContent() => $"""
        DB_HOST={DbHost}
        DB_PORT={DbPort}
        DB_NAME={DbName}
        DB_USER={DbUser}
        DB_PASSWORD={DbPassword}

        DATABASE_URL={DatabaseUrl}

        BETTER_AUTH_URL={AppUrl}
        BETTER_AUTH_BASE_URL={AppUrl}
        BETTER_AUTH_SECRET={BetterAuthSecret}

        RATE_LIMIT_WINDOW={RateLimitWindow}
        MAX_ATTEMPTS={MaxAttempts}
        """;
}
