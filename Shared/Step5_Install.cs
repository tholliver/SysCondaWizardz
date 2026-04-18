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
        Log($"  Backup auto : {(cfg.EnableBackups ? $"{cfg.BackupTime} [{cfg.BackupDays}]" : "no")}\n", LogLevel.Info);

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

            // 1 ── Install bun under RootDirectory\bun\ (SYSTEM-visible) ──────
            Log("\n[1/x] Instalando bun...", LogLevel.Step);
            await InstallBunAsync();
            Tick();

            // 2 ── Extract / copy project source into RootDirectory\app\ ──────
            Log("\n[2/x] Preparando código fuente...", LogLevel.Step);
            await PrepareSourceAsync();
            Tick();

            // 3 ── Write .env ──────────────────────────────────────────────────
            Log("\n[3/x] Escribiendo .env...", LogLevel.Step);
            var envPath = Path.Combine(_cfg.AppDirectory, ".env");
            _cfg.BetterAuthSecret = ResolveBetterAuthSecret(envPath);
            await File.WriteAllTextAsync(envPath, _cfg.EnvFileContent());
            Log($"  ✓ {envPath}", LogLevel.Ok);
            Tick();

            // 4 ── bun install ─────────────────────────────────────────────────
            Log("\n[4/x] Instalando dependencias (bun install)...", LogLevel.Step);
            await RunCmd(_cfg.BunExePath, "install", _cfg.AppDirectory);
            Tick();

            // 5 ── bun run build ───────────────────────────────────────────────
            Log("\n[5/x] Compilando app Astro/Bun (bun run build)...", LogLevel.Step);
            await RunCmd(_cfg.BunExePath, "run build", _cfg.AppDirectory);
            EnsureBuildOutput();
            Tick();

            // 6 ── (optional) Restore DB ───────────────────────────────────────
            if (_cfg.RestoreDatabaseOnInstall)
            {
                Log("\n[6/x] Restaurando base de datos...", LogLevel.Step);
                await RestoreDatabaseIfAvailable();
                Tick();
            }

            // 7 ── (optional) Windows service ─────────────────────────────────
            if (_cfg.InstallAsService)
            {
                Log("\n[7/x] Instalando servicio de Windows...", LogLevel.Step);
                InstallWindowsService();
                Tick();
            }

            // 8 ── (optional) Backup scheduled task ───────────────────────────
            if (_cfg.EnableBackups)
            {
                Log("\n[8/x] Creando script de backup y tarea programada...", LogLevel.Step);
                await SetupBackup();
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

        // Force bun installer to land in our root dir instead of %USERPROFILE%\.bun
        psi.Environment["BUN_INSTALL"] = bunDir;

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Log("  " + e.Data, LogLevel.Info); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Log("  " + e.Data, LogLevel.Warn); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();

        // Bun installer puts the binary at $BUN_INSTALL\bin\bun.exe — copy
        // it one level up so our hardcoded BunExePath (<root>\bun\bun.exe) works.
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
                // "ExistingDirectory" means embedded source in this wizard
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

    /// <summary>
    /// Copies sourceDir → destDir excluding folders that must not be deployed.
    /// node_modules and dist are regenerated at install time by bun.
    /// </summary>
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
        var entryMjs = Path.Combine(_cfg.AppDirectory, "dist", "server", "entry.mjs");
        if (!File.Exists(entryMjs))
            throw new Exception($"No se encontró el entrypoint compilado: {entryMjs}");

        Directory.CreateDirectory(_cfg.ServiceRuntimeDirectory);
        Directory.CreateDirectory(_cfg.ServiceLogDirectory);

        var serviceConfig = new ServiceHostConfig
        {
            ServiceName = _cfg.ServiceName,
            AppDirectory = _cfg.AppDirectory,
            BunExecutable = _cfg.BunExePath,       // always system-wide path
            EntryPoint = entryMjs,
            EnvFilePath = Path.Combine(_cfg.AppDirectory, ".env"),
            LogDirectory = _cfg.ServiceLogDirectory,
            Port = _cfg.AppPort,
            RestartDelaySeconds = _cfg.ServiceRestartDelaySeconds,
        };

        TryStopAndDeleteService(_cfg.ServiceName);
        Thread.Sleep(1500); // let SCM fully finish removing the old registration

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

        RunSc("description", _cfg.ServiceName, _cfg.ServiceDisplayName + " — Astro SSR host");

        RunSc("failure", _cfg.ServiceName,
              "reset=", "86400",
              "actions=", "restart/5000/restart/5000/restart/5000");

        // Required so SCM actually applies the failure actions above
        RunSc("failureflag", _cfg.ServiceName, "1");

        // Give SCM time after create before starting (slow disks / AV scans)
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
            {
                continue;
            }

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

    /// <summary>
    /// Calls sc.exe via ArgumentList — no cmd.exe in the middle,
    /// no shell quoting layer to mangle paths with spaces.
    /// </summary>
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
        var scriptPath = Path.Combine(_cfg.AppDirectory, "scripts", "backup_win.ps1");
        Directory.CreateDirectory(Path.Combine(_cfg.AppDirectory, "scripts"));
        Directory.CreateDirectory(_cfg.BackupDirectory);

        await File.WriteAllTextAsync(scriptPath, BackupScriptGenerator.Generate(_cfg));
        Log($"  ✓ Script generado: {scriptPath}", LogLevel.Ok);
        _cfg.Save();
        Log($"  ✓ Configuracion de backup guardada.", LogLevel.Ok);
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

    private void RunSync(string exe, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new Exception($"No se pudo iniciar: {exe}");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (!string.IsNullOrWhiteSpace(stdout)) Log("  " + stdout.TrimEnd(), LogLevel.Info);
        if (!string.IsNullOrWhiteSpace(stderr)) Log("  " + stderr.TrimEnd(), LogLevel.Warn);
        if (p.ExitCode != 0)
            throw new Exception($"'{exe} {args}' falló con código {p.ExitCode}");
    }

    private int CountSteps()
    {
        int n = 5; // bun + source + .env + bun install + bun build
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
