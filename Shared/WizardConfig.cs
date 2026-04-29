namespace SysCondaWizard;

public enum AppSourceKind
{
    ExistingDirectory,
    ZipArchive,
    GitRepository
}

public enum InstallMode
{
    Install,
    Update
}

/// <summary>All user inputs collected across wizard steps.</summary>
public class WizardConfig
{
    // ── Backup window defaults (change here to adjust the UI placeholder) ─────
    public const string DefaultBackupWindowStart = "08:00";
    public const string DefaultBackupWindowEnd = "18:00";

    // ── Step 1: Install root + source ────────────────────────────────────────
    public string RootDirectory { get; set; } = AppProfile.DefaultRootDir;
    public InstallMode Mode { get; set; } = InstallMode.Install;
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
    public bool OpenFirewallPort { get; set; } = false;

    // ── Step 4: Backup ───────────────────────────────────────────────────────
    public bool EnableBackups { get; set; } = true;
    public string PgDumpPath { get; set; } = PostgresBinaryLocator.FindPgDumpPath();
    public string PgRestorePath { get; set; } = PostgresBinaryLocator.FindPgRestorePath();
    public string BackupWindowStart { get; set; } = DefaultBackupWindowStart;
    public string BackupWindowEnd { get; set; } = DefaultBackupWindowEnd;
    // Always all 7 days — window period is the control, not specific days
    public string BackupDays { get; set; } = "MON,TUE,WED,THU,FRI,SAT,SUN";
    public bool RestoreDatabaseOnInstall { get; set; } = false;
    public string RestoreDumpPath { get; set; } = "";
    public bool BackupTestMode { get; set; } = false;

    // ── Derived paths (all rooted under RootDirectory) ───────────────────────
    public string BunDirectory => Path.Combine(RootDirectory, "bun");
    public string BunExePath => Path.Combine(BunDirectory, "bun.exe");
    public string AppDirectory => Path.Combine(RootDirectory, "app");
    public string ServiceRuntimeDirectory => Path.Combine(RootDirectory, "runtime");
    public string ServiceConfigPath => Path.Combine(ServiceRuntimeDirectory, "service-config.json");
    public string ServiceLogDirectory => Path.Combine(RootDirectory, "logs");
    public string BackupDirectory => Path.Combine(RootDirectory, "backups");
    public string InstallConfigPath => GetInstallConfigPath(RootDirectory);
    public string ReleaseManifestPath => Path.Combine(RootDirectory, "app-release.json");
    public bool IsUpdateMode => Mode == InstallMode.Update;

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

    // ── Persistence ──────────────────────────────────────────────────────────
    public static string LegacyConfigFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SysCondaWizard",
            "wizard-config.json");

    public static string ConfigFilePath => ResolveConfigFilePath() ?? LegacyConfigFilePath;

    public static string GetInstallConfigPath(string rootDirectory) =>
        Path.Combine(rootDirectory, "wizard-config.json");

    public static IEnumerable<string> EnumerateKnownConfigPaths(string? rootDirectory = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var yieldReturnList = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var fullPath = Path.GetFullPath(path);
            if (seen.Add(fullPath))
                yieldReturnList.Add(fullPath);
        }

        if (!string.IsNullOrWhiteSpace(rootDirectory))
            Add(GetInstallConfigPath(rootDirectory));

        Add(ResolveConfigFilePath());
        Add(LegacyConfigFilePath);

        return yieldReturnList;
    }

    public static WizardConfig Load(string? rootDirectory = null)
    {
        foreach (var path in EnumerateKnownConfigPaths(rootDirectory))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<WizardConfig>(File.ReadAllText(path));
                if (cfg != null)
                    return cfg;
            }
            catch
            {
            }
        }

        return new WizardConfig();
    }

    public void Save()
    {
        var path = !string.IsNullOrWhiteSpace(RootDirectory)
            ? InstallConfigPath
            : LegacyConfigFilePath;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            System.Text.Json.JsonSerializer.Serialize(this,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        if (!string.Equals(path, LegacyConfigFilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(LegacyConfigFilePath))
        {
            try { File.Delete(LegacyConfigFilePath); } catch { }
        }
    }

    private static string? ResolveConfigFilePath()
    {
        var appBase = AppContext.BaseDirectory;
        var directPath = Path.Combine(appBase, "wizard-config.json");
        if (File.Exists(directPath)) return directPath;

        var serviceConfig = Path.Combine(appBase, "service-config.json");
        if (File.Exists(serviceConfig))
        {
            var root = Directory.GetParent(appBase)?.FullName;
            if (!string.IsNullOrWhiteSpace(root))
                return GetInstallConfigPath(root);
        }

        var baseDirName = Path.GetFileName(appBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(baseDirName, "runtime", StringComparison.OrdinalIgnoreCase))
        {
            var root = Directory.GetParent(appBase)?.FullName;
            if (!string.IsNullOrWhiteSpace(root))
                return GetInstallConfigPath(root);
        }

        return null;
    }

    public void ApplyInstalledSettings(WizardConfig installed)
    {
        DbHost = installed.DbHost;
        DbPort = installed.DbPort;
        DbName = installed.DbName;
        DbUser = installed.DbUser;
        DbPassword = installed.DbPassword;
        AppUrl = installed.AppUrl;
        AppPort = installed.AppPort;
        BetterAuthSecret = installed.BetterAuthSecret;
        RateLimitWindow = installed.RateLimitWindow;
        MaxAttempts = installed.MaxAttempts;
        ServiceName = installed.ServiceName;
        ServiceDisplayName = installed.ServiceDisplayName;
        InstallAsService = installed.InstallAsService;
        ServiceRestartDelaySeconds = installed.ServiceRestartDelaySeconds;
        EnableBackups = installed.EnableBackups;
        PgDumpPath = installed.PgDumpPath;
        PgRestorePath = installed.PgRestorePath;
        BackupWindowStart = installed.BackupWindowStart;
        BackupWindowEnd = installed.BackupWindowEnd;
        BackupDays = installed.BackupDays;
        RestoreDatabaseOnInstall = installed.RestoreDatabaseOnInstall;
        RestoreDumpPath = installed.RestoreDumpPath;
        BackupTestMode = installed.BackupTestMode;
        OpenFirewallPort = installed.OpenFirewallPort;
    }
}
