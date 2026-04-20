using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace SysCondaWizard;

/// <summary>Step 5 — execute everything and stream output to the log box.</summary>
public class Step5_Install : IWizardStep
{
    public string Title => "Instalación";
    public bool IsRunning => _started;
    public bool HasCompleted { get; private set; }
    public bool HasAttempted { get; private set; }
    public event Action? StateChanged;

    private RichTextBox _log = new();
    private Button _btnRun = new();
    private ProgressBar _progress = new();
    private bool _started;
    private WizardConfig _cfg = new();

    public Control BuildUI(WizardConfig cfg)
    {
        _cfg = cfg;

        var root = new Panel
        {
            Padding = new Padding(0, 0, 0, 8),
        };

        _progress = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 8,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = HasCompleted ? 100 : 0,
        };
        root.Controls.Add(_progress);

        var logHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 12, 0, 0),
        };

        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(18, 18, 24),
            ForeColor = Color.FromArgb(200, 230, 200),
            Font = new Font("Consolas", 9f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
        };
        logHost.Controls.Add(_log);
        root.Controls.Add(logHost);

        var btnRow = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(0, 6, 0, 0) };
        _btnRun = new Button
        {
            Text = GetRunButtonText(),
            Width = 220,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(34, 160, 80),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Enabled = !_started && !HasCompleted,
        };
        _btnRun.FlatAppearance.BorderColor = Color.FromArgb(34, 160, 80);
        _btnRun.Click += async (_, _) => await RunInstallAsync();
        btnRow.Controls.Add(_btnRun);
        root.Controls.Add(btnRow);

        Log(AppProfile.WizardTitle, LogLevel.Header);
        Log(HasCompleted
            ? "Instalación finalizada. Puedes cerrar el wizard.\n"
            : HasAttempted
                ? "La instalación anterior no terminó. Revisa el log y vuelve a intentarlo.\n"
                : "Pulsa ▶ Iniciar instalación para comenzar.\n", LogLevel.Info);
        Log($"  Raíz        : {cfg.RootDirectory}", LogLevel.Info);
        Log($"  Bun         : {cfg.BunExePath}", LogLevel.Info);
        Log($"  App         : {cfg.AppDirectory}", LogLevel.Info);
        Log($"  Origen      : {DescribeSource(cfg)}", LogLevel.Info);
        Log($"  Base datos  : {cfg.DbName}@{cfg.DbHost}:{cfg.DbPort}", LogLevel.Info);
        Log($"  Servicio    : {(cfg.InstallAsService ? cfg.ServiceName : "no")}", LogLevel.Info);
        Log($"  Restore BD  : {(cfg.RestoreDatabaseOnInstall ? "sí" : "no")}", LogLevel.Info);
        Log($"  Backup auto : {(cfg.EnableBackups ? $"{cfg.BackupWindowStart}-{cfg.BackupWindowEnd} [{cfg.BackupDays}]" : "no")}\n", LogLevel.Info);

        return root;
    }

    public async Task RunInstallAsync()
    {
        if (_started || HasCompleted) return;

        HasAttempted = true;
        _started = true;
        UpdateActionState();

        try
        {
            int total = CountSteps();
            int done = 0;
            void Tick() { done++; SetProgress((int)(done * 100.0 / total)); }

            // 1 ── Verify PostgreSQL credentials & connectivity ─────────────
            Log("\n[1/x] Verificando conexion a PostgreSQL...", LogLevel.Step);
            await VerifyPostgresCredentials();
            Tick();

            // 2 ── Ensure database exists, create if not ───────────────────
            Log("\n[2/x] Verificando base de datos...", LogLevel.Step);
            await EnsureDatabaseExists();
            Tick();

            // 3 ── Install bun ─────────────────────────────────────────────
            Log("\n[3/x] Instalando bun...", LogLevel.Step);
            await InstallBunAsync();
            Tick();

            // 4 ── Extract / copy project source ──────────────────────────
            Log("\n[4/x] Preparando código fuente...", LogLevel.Step);
            await PrepareSourceAsync();
            Tick();

            // 5 ── Write .env ──────────────────────────────────────────────
            Log("\n[5/x] Escribiendo .env...", LogLevel.Step);
            var envPath = Path.Combine(_cfg.AppDirectory, ".env");
            _cfg.BetterAuthSecret = ResolveBetterAuthSecret(envPath);
            await File.WriteAllTextAsync(envPath, _cfg.EnvFileContent());
            Log($"  ✓ {envPath}", LogLevel.Ok);
            Tick();

            // 6 ── bun install ─────────────────────────────────────────────
            Log("\n[6/x] Instalando dependencias (bun install)...", LogLevel.Step);
            await RunCmd(_cfg.BunExePath, "install", _cfg.AppDirectory);
            Tick();

            // 7 ── Run Drizzle migrations (schema push) ────────────────────
            Log("\n[7/x] Aplicando esquema de base de datos (drizzle)...", LogLevel.Step);
            await RunDrizzleMigrations();
            Tick();

            // 8 ── bun run build ───────────────────────────────────────────
            Log("\n[8/x] Compilando app Astro/Bun (bun run build)...", LogLevel.Step);
            await RunCmd(_cfg.BunExePath, "run build", _cfg.AppDirectory);
            EnsureBuildOutput();
            Tick();

            // 9 ── (optional) Restore DB ───────────────────────────────────
            if (_cfg.RestoreDatabaseOnInstall)
            {
                Log("\n[9/x] Restaurando base de datos...", LogLevel.Step);
                await RestoreDatabaseIfAvailable();
                Tick();
            }

            if (_cfg.EnableBackups)
            {
                Log("\n[10/x] Configurando backup automatico...", LogLevel.Step);
                await SetupBackup();
                Tick();
            }

            // 11 ── (optional) Windows service ────────────────────────────
            if (_cfg.InstallAsService)
            {
                Log("\n[11/x] Instalando servicio de Windows...", LogLevel.Step);
                InstallWindowsService();
                Tick();
            }

            SetProgress(100);
            HasCompleted = true;
            Log("\n══════════════════════════════════════════", LogLevel.Ok);
            Log("  ✅  Instalación completada con éxito", LogLevel.Ok);
            Log("══════════════════════════════════════════", LogLevel.Ok);
            Log($"\n  Accede a la app en: http://localhost:{_cfg.AppPort}/", LogLevel.Info);
            Log($"  Raíz de instalación: {_cfg.RootDirectory}", LogLevel.Info);
            if (_cfg.InstallAsService)
                Log($"\n  Servicio '{_cfg.ServiceName}': sc start {_cfg.ServiceName}", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Log($"\n❌  ERROR: {ex.Message}", LogLevel.Error);
            Log("  Puedes corregir el problema y volver a intentar la instalación.", LogLevel.Warn);
        }
        finally
        {
            _started = false;
            UpdateActionState();
        }
    }

    // ── Step 1: Verify PostgreSQL credentials ─────────────────────────────────

    private async Task VerifyPostgresCredentials()
    {
        var psqlDir = Path.GetDirectoryName(_cfg.PgDumpPath) ?? "";
        var psql = Path.Combine(psqlDir, "psql.exe");

        if (!File.Exists(psql))
        {
            Log($"  ! psql.exe no encontrado en {psqlDir} — omitiendo verificación.", LogLevel.Warn);
            return;
        }

        if (!File.Exists(_cfg.PgDumpPath))
            throw new Exception(
                $"pg_dump.exe no encontrado en:\n{_cfg.PgDumpPath}\n" +
                "Verifica que PostgreSQL 18 esté instalado.");

        Log($"  Conectando a {_cfg.DbHost}:{_cfg.DbPort} como '{_cfg.DbUser}'...", LogLevel.Info);

        var psi = new ProcessStartInfo
        {
            FileName = psql,
            // Connect to postgres (system db) to test credentials without needing our DB yet
            Arguments = $"-h {_cfg.DbHost} -p {_cfg.DbPort} -U {_cfg.DbUser} -d postgres -c \"SELECT version()\" -t -q",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["PGPASSWORD"] = _cfg.DbPassword;
        psi.Environment["PGCONNECT_TIMEOUT"] = "10";

        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
            throw new Exception(
                $"No se pudo conectar a PostgreSQL.\n" +
                $"Host: {_cfg.DbHost}:{_cfg.DbPort} | Usuario: {_cfg.DbUser}\n" +
                $"Error: {stderr.Trim()}\n\n" +
                "Verifica host, puerto, usuario y contraseña en el paso anterior.");

        var version = stdout.Trim();
        Log($"  ✓ Conectado: {version}", LogLevel.Ok);
    }

    // ── Step 2: Ensure DB exists ──────────────────────────────────────────────

    private async Task EnsureDatabaseExists()
    {
        var psqlDir = Path.GetDirectoryName(_cfg.PgDumpPath) ?? "";
        var psql = Path.Combine(psqlDir, "psql.exe");
        var createdb = Path.Combine(psqlDir, "createdb.exe");

        if (!File.Exists(psql))
        {
            Log("  ! psql.exe no encontrado — omitiendo verificación de BD.", LogLevel.Warn);
            return;
        }

        // Check if DB already exists
        var psiCheck = new ProcessStartInfo
        {
            FileName = psql,
            Arguments = $"-h {_cfg.DbHost} -p {_cfg.DbPort} -U {_cfg.DbUser} -d postgres" +
                        $" -t -c \"SELECT 1 FROM pg_database WHERE datname='{_cfg.DbName}'\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psiCheck.Environment["PGPASSWORD"] = _cfg.DbPassword;

        using var checkProc = Process.Start(psiCheck)!;
        var checkOut = await checkProc.StandardOutput.ReadToEndAsync();
        await checkProc.WaitForExitAsync();

        if (checkOut.Trim() == "1")
        {
            Log($"  ✓ Base de datos '{_cfg.DbName}' ya existe.", LogLevel.Ok);
            return;
        }

        // Create it
        Log($"  Creando base de datos '{_cfg.DbName}'...", LogLevel.Info);

        if (!File.Exists(createdb))
            throw new Exception($"createdb.exe no encontrado en {psqlDir}");

        var psiCreate = new ProcessStartInfo
        {
            FileName = createdb,
            Arguments = $"-h {_cfg.DbHost} -p {_cfg.DbPort} -U {_cfg.DbUser}" +
                        $" --encoding=UTF8 --locale=en_US.UTF-8 {_cfg.DbName}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psiCreate.Environment["PGPASSWORD"] = _cfg.DbPassword;

        using var createProc = Process.Start(psiCreate)!;
        var createErr = await createProc.StandardError.ReadToEndAsync();
        await createProc.WaitForExitAsync();

        if (createProc.ExitCode != 0)
            throw new Exception(
                $"No se pudo crear la base de datos '{_cfg.DbName}'.\n" +
                $"Error: {createErr.Trim()}");

        Log($"  ✓ Base de datos '{_cfg.DbName}' creada con encoding UTF8.", LogLevel.Ok);
    }

    // ── Step 7: Drizzle migrations ────────────────────────────────────────────

    private async Task RunDrizzleMigrations()
    {
        // drizzle-kit push applies schema without needing migration files
        // Works for fresh installs AND updates (idempotent)
        var drizzleConfig = Path.Combine(_cfg.AppDirectory, "drizzle.config.ts");
        var drizzleConfigJs = Path.Combine(_cfg.AppDirectory, "drizzle.config.js");

        if (!File.Exists(drizzleConfig) && !File.Exists(drizzleConfigJs))
        {
            Log("  ! drizzle.config.ts no encontrado — omitiendo migraciones.", LogLevel.Warn);
            Log("    Si tu app usa Drizzle, agrega drizzle.config.ts a la raíz del proyecto.", LogLevel.Warn);
            return;
        }

        Log("  Ejecutando drizzle-kit push (aplicar esquema)...", LogLevel.Info);

        // Set DATABASE_URL env for drizzle to connect
        var env = new Dictionary<string, string>
        {
            ["DATABASE_URL"] = _cfg.DatabaseUrl,
            ["DB_HOST"] = _cfg.DbHost,
            ["DB_PORT"] = _cfg.DbPort,
            ["DB_NAME"] = _cfg.DbName,
            ["DB_USER"] = _cfg.DbUser,
            ["DB_PASSWORD"] = _cfg.DbPassword,
        };

        try
        {
            // Use bun to run drizzle-kit push with auto-accept (--force skips confirmation prompts)
            await RunCmd(_cfg.BunExePath, "run drizzle-kit push --force", _cfg.AppDirectory, env);
            Log("  ✓ Esquema aplicado correctamente.", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            // Migration failure is non-fatal on reinstall — schema may already exist
            Log($"  ! drizzle-kit push: {ex.Message}", LogLevel.Warn);
            Log("    Continuando — si es reinstalación el esquema ya existe.", LogLevel.Warn);
        }
    }

    // ── Bun install ───────────────────────────────────────────────────────────

    private async Task InstallBunAsync()
    {
        var bunDir = _cfg.BunDirectory;
        var bunExe = _cfg.BunExePath;

        Directory.CreateDirectory(bunDir);

        if (File.Exists(bunExe))
        {
            Log($"  ✓ Bun ya presente en {bunExe}", LogLevel.Ok);
            return;
        }

        Log($"  Descargando e instalando bun en {bunDir} ...", LogLevel.Info);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"irm bun.sh/install.ps1 | iex\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["BUN_INSTALL"] = bunDir;

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Log("  " + e.Data, LogLevel.Info); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Log("  " + e.Data, LogLevel.Warn); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();

        var inBin = Path.Combine(bunDir, "bin", "bun.exe");
        if (File.Exists(inBin) && !File.Exists(bunExe))
        {
            File.Copy(inBin, bunExe);
            Log($"  ✓ Bun copiado de bin\\ a {bunExe}", LogLevel.Ok);
        }

        if (!File.Exists(bunExe))
            throw new Exception($"La instalación de bun terminó pero no se encontró el ejecutable en {bunExe}");

        Log($"  ✓ Bun instalado: {bunExe}", LogLevel.Ok);
    }

    // ── Source preparation ────────────────────────────────────────────────────

    private async Task PrepareSourceAsync()
    {
        switch (_cfg.AppSource)
        {
            case AppSourceKind.ExistingDirectory:
                Log("  Extrayendo fuente embebida...", LogLevel.Info);
                EnsureDirectoryReady(_cfg.AppDirectory, allowExistingProject: true);
                await EmbeddedSourceExtractor.ExtractToAsync(_cfg.AppDirectory);
                Log($"  ✓ Fuente extraída en: {_cfg.AppDirectory}", LogLevel.Ok);
                break;
            case AppSourceKind.ZipArchive:
                await ExtractZipSourceAsync();
                break;
            case AppSourceKind.GitRepository:
                await CloneRepositoryAsync();
                break;
            default:
                throw new Exception("Origen de aplicación no soportado.");
        }
        ValidateProjectFiles();
    }

    private async Task ExtractZipSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(_cfg.SourceZipPath) || !File.Exists(_cfg.SourceZipPath))
            throw new Exception("No se encontró el archivo ZIP configurado.");

        EnsureDirectoryReady(_cfg.AppDirectory, allowExistingProject: false);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"{AppProfile.ServiceName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            ZipFile.ExtractToDirectory(_cfg.SourceZipPath, tempRoot);
            var extractedRoot = ResolveExtractedProjectRoot(tempRoot);
            CopyDirectory(extractedRoot, _cfg.AppDirectory);
            Log($"  ✓ ZIP extraído en: {_cfg.AppDirectory}", LogLevel.Ok);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
        await Task.CompletedTask;
    }

    private async Task CloneRepositoryAsync()
    {
        if (string.IsNullOrWhiteSpace(_cfg.GitRepoUrl))
            throw new Exception("La URL del repositorio Git está vacía.");

        EnsureDirectoryReady(_cfg.AppDirectory, allowExistingProject: false);
        var parentDir = Path.GetDirectoryName(_cfg.AppDirectory)
            ?? throw new Exception("No se pudo resolver la carpeta padre para el clone de Git.");

        Directory.CreateDirectory(parentDir);
        await RunCmd("git", $"clone {_cfg.GitRepoUrl} \"{_cfg.AppDirectory}\"", parentDir);
        Log($"  ✓ Repositorio clonado en: {_cfg.AppDirectory}", LogLevel.Ok);
    }

    private void EnsureDirectoryReady(string targetDir, bool allowExistingProject)
    {
        if (!Directory.Exists(targetDir)) { Directory.CreateDirectory(targetDir); return; }
        if (allowExistingProject) return;
        if (Directory.EnumerateFileSystemEntries(targetDir).Any())
            throw new Exception(
                $"La carpeta destino ya contiene archivos: {targetDir}.\n" +
                "Usa una carpeta vacía para ZIP o Git.");
    }

    private string ResolveExtractedProjectRoot(string extractedPath)
    {
        if (LooksLikeAppRoot(extractedPath)) return extractedPath;
        var dirs = Directory.GetDirectories(extractedPath);
        if (dirs.Length == 1 && LooksLikeAppRoot(dirs[0])) return dirs[0];
        throw new Exception(
            "El ZIP no parece contener un proyecto Astro/Bun válido.\n" +
            "Se esperaba package.json en la raíz o en la carpeta principal.");
    }

    private void ValidateProjectFiles()
    {
        if (!LooksLikeAppRoot(_cfg.AppDirectory))
            throw new Exception(
                $"No se encontró package.json en {_cfg.AppDirectory}.\n" +
                "Verifica la carpeta del proyecto Astro/Bun.");
    }

    private static bool LooksLikeAppRoot(string path) =>
        File.Exists(Path.Combine(path, "package.json"));

    private void EnsureBuildOutput()
    {
        var entryMjs = Path.Combine(_cfg.AppDirectory, "dist", "server", "entry.mjs");
        if (!File.Exists(entryMjs))
            throw new Exception(
                $"La build terminó pero no generó dist\\server\\entry.mjs.\n" +
                $"Revisa la configuración SSR de Astro en {_cfg.AppDirectory}.");
        Log($"  ✓ Build SSR lista: {entryMjs}", LogLevel.Ok);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "dist", ".env"
        };

        bool IsExcluded(string rel) =>
            rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               .Any(excluded.Contains);

        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            if (!IsExcluded(rel))
                Directory.CreateDirectory(Path.Combine(destDir, rel));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            if (IsExcluded(rel)) continue;
            var target = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string DescribeSource(WizardConfig cfg) => cfg.AppSource switch
    {
        AppSourceKind.ExistingDirectory => EmbeddedSourceExtractor.IsAvailable
                                            ? "fuente embebida ✓"
                                            : "fuente embebida ✗ (recompila el wizard)",
        AppSourceKind.ZipArchive => $"ZIP ({cfg.SourceZipPath})",
        AppSourceKind.GitRepository => $"Git ({cfg.GitRepoUrl})",
        _ => "desconocido"
    };

    // ── Windows service ───────────────────────────────────────────────────────

    private void InstallWindowsService()
    {
        _cfg.Save();

        var entryMjs = Path.Combine(_cfg.AppDirectory, "dist", "server", "entry.mjs");
        if (!File.Exists(entryMjs))
            throw new Exception($"No se encontró el entrypoint compilado: {entryMjs}");

        Directory.CreateDirectory(_cfg.ServiceRuntimeDirectory);
        Directory.CreateDirectory(_cfg.ServiceLogDirectory);

        var serviceConfig = new ServiceHostConfig
        {
            ServiceName = _cfg.ServiceName,
            AppDirectory = _cfg.AppDirectory,
            BunExecutable = _cfg.BunExePath,
            EntryPoint = entryMjs,
            EnvFilePath = Path.Combine(_cfg.AppDirectory, ".env"),
            LogDirectory = _cfg.ServiceLogDirectory,
            Port = _cfg.AppPort,
            RestartDelaySeconds = _cfg.ServiceRestartDelaySeconds,
        };

        TryStopAndDeleteService(_cfg.ServiceName);
        Thread.Sleep(1500);

        File.WriteAllText(
            _cfg.ServiceConfigPath,
            JsonSerializer.Serialize(serviceConfig, new JsonSerializerOptions { WriteIndented = true }));
        Log($"  ✓ Config escrita en {_cfg.ServiceConfigPath}", LogLevel.Ok);

        var serviceExe = DeployServiceRuntime();

        var binPath = $"{serviceExe} --service {_cfg.ServiceConfigPath}";
        RunSc("create", _cfg.ServiceName,
              "start=", "auto",
              "binPath=", binPath,
              "DisplayName=", _cfg.ServiceDisplayName);

        RunSc("description", _cfg.ServiceName, _cfg.ServiceDisplayName + " - Astro SSR host");

        RunSc("failure", _cfg.ServiceName,
              "reset=", "86400",
              "actions=", "restart/5000/restart/5000/restart/5000");

        RunSc("failureflag", _cfg.ServiceName, "1");

        Thread.Sleep(3000);
        RunSc("start", _cfg.ServiceName);

        Log($"  ✓ Servicio '{_cfg.ServiceName}' instalado e iniciado.", LogLevel.Ok);
    }

    private string DeployServiceRuntime()
    {
        Directory.CreateDirectory(_cfg.ServiceRuntimeDirectory);
        var sourceExe = Application.ExecutablePath;
        var sourceDir = Path.GetDirectoryName(sourceExe)!;
        var deployedExe = Path.Combine(_cfg.ServiceRuntimeDirectory, Path.GetFileName(sourceExe));

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetPath = Path.Combine(_cfg.ServiceRuntimeDirectory, Path.GetFileName(file));
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, targetPath, overwrite: true);
        }

        Log(string.Equals(Path.GetFullPath(sourceExe), Path.GetFullPath(deployedExe), StringComparison.OrdinalIgnoreCase)
                ? $"  ✓ Runtime ya en uso desde {deployedExe}"
                : $"  ✓ Runtime desplegado en {deployedExe}",
            LogLevel.Ok);
        return deployedExe;
    }

    private void TryStopAndDeleteService(string name)
    {
        try { RunSc("stop", name); } catch { }
        try { RunSc("delete", name); } catch { }
    }

    private void RunSc(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (!string.IsNullOrWhiteSpace(stdout)) Log("  " + stdout.TrimEnd(), LogLevel.Info);
        if (!string.IsNullOrWhiteSpace(stderr)) Log("  " + stderr.TrimEnd(), LogLevel.Warn);
        if (p.ExitCode != 0)
            throw new Exception($"sc.exe falló (código {p.ExitCode}): {string.Join(" ", args)}");
    }

    // ── Backup ────────────────────────────────────────────────────────────────

    private async Task SetupBackup()
    {
        Directory.CreateDirectory(_cfg.BackupDirectory);
        _cfg.Save();
        Log("  ✓ Configuracion de backup guardada.", LogLevel.Ok);
        await Task.CompletedTask;
    }

    // ── DB restore ────────────────────────────────────────────────────────────

    private async Task RestoreDatabaseIfAvailable()
    {
        var dumpPath = ResolveRestoreDumpPath();
        if (dumpPath == null)
        {
            Log("  ! No se encontró ningún dump. Se continúa sin restauración.", LogLevel.Warn);
            return;
        }

        Log($"  Restaurando dump: {dumpPath}", LogLevel.Info);
        await RunCmd(
            _cfg.PgRestorePath,
            $"--clean --if-exists --no-owner --no-privileges " +
            $"-h {_cfg.DbHost} -p {_cfg.DbPort} -U {_cfg.DbUser} -d {_cfg.DbName} \"{dumpPath}\"",
            _cfg.AppDirectory,
            new Dictionary<string, string> { ["PGPASSWORD"] = _cfg.DbPassword });
        Log("  ✓ Restauración completada.", LogLevel.Ok);
    }

    private string? ResolveRestoreDumpPath()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.RestoreDumpPath))
            return File.Exists(_cfg.RestoreDumpPath) ? _cfg.RestoreDumpPath : null;
        if (!Directory.Exists(_cfg.BackupDirectory)) return null;
        return Directory.EnumerateFiles(_cfg.BackupDirectory, "*.dump", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ResolveBetterAuthSecret(string envPath)
    {
        if (!string.IsNullOrWhiteSpace(_cfg.BetterAuthSecret)) return _cfg.BetterAuthSecret;

        var envValues = EnvFileLoader.Load(envPath);
        if (envValues.TryGetValue("BETTER_AUTH_SECRET", out var existing) && !string.IsNullOrWhiteSpace(existing))
        {
            Log("  ✓ Reutilizando BETTER_AUTH_SECRET existente.", LogLevel.Ok);
            return existing;
        }

        var generated = SecretGenerator.Create();
        Log("  ✓ BETTER_AUTH_SECRET generado automáticamente.", LogLevel.Ok);
        return generated;
    }

    private async Task RunCmd(string exe, string args, string? workDir = null, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir ?? _cfg.AppDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (env != null)
            foreach (var pair in env) psi.Environment[pair.Key] = pair.Value;

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Log("  " + e.Data, LogLevel.Info); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Log("  " + e.Data, LogLevel.Warn); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
            throw new Exception($"'{exe} {args}' falló con código {proc.ExitCode}");
    }

    private int CountSteps()
    {
        int n = 7; // pg check + db ensure + bun + source + .env + bun install + drizzle + build
        if (_cfg.RestoreDatabaseOnInstall) n++;
        if (_cfg.InstallAsService) n++;
        if (_cfg.EnableBackups) n++;
        return n;
    }

    private void SetProgress(int val)
    {
        if (_progress.IsDisposed) return;
        RunOnUi(_progress, () => _progress.Value = Math.Clamp(val, 0, 100));
    }

    private void UpdateActionState()
    {
        if (!_btnRun.IsDisposed)
            RunOnUi(_btnRun, () =>
            {
                _btnRun.Enabled = !_started && !HasCompleted;
                _btnRun.Text = GetRunButtonText();
            });
        StateChanged?.Invoke();
    }

    private string GetRunButtonText() =>
        HasCompleted ? "Instalación completada"
        : _started ? "Instalando..."
        : HasAttempted ? "↻  Reintentar instalación"
        : "▶  Iniciar instalación";

    private enum LogLevel { Header, Step, Ok, Info, Warn, Error }

    private void Log(string text, LogLevel level)
    {
        if (_log.IsDisposed) return;
        RunOnUi(_log, () =>
        {
            Color clr = level switch
            {
                LogLevel.Header => Color.FromArgb(140, 160, 255),
                LogLevel.Step => Color.FromArgb(250, 200, 80),
                LogLevel.Ok => Color.FromArgb(80, 220, 120),
                LogLevel.Warn => Color.FromArgb(255, 180, 60),
                LogLevel.Error => Color.FromArgb(255, 80, 80),
                _ => Color.FromArgb(200, 210, 220),
            };
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = clr;
            _log.AppendText(text + "\n");
            _log.ScrollToCaret();
        });
    }

    private static void RunOnUi(Control control, Action action)
    {
        if (control.IsDisposed) return;
        if (!control.IsHandleCreated || !control.InvokeRequired) { action(); return; }
        control.Invoke(action);
    }

    public string? Validate(WizardConfig cfg) => null;
    public void Save(WizardConfig cfg) => _cfg = cfg;
}

