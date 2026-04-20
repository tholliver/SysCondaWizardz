using System.Diagnostics;
using System.ServiceProcess;

namespace SysCondaWizard;

/// <summary>
/// Standalone uninstall dialog. Shows health checks for the current installation,
/// then offers a clean uninstall: stop+delete service, optionally delete files.
/// Launch via a dedicated "Desinstalar" button in WizardForm or as a separate entry point.
/// </summary>
public class UninstallForm : Form
{
    private readonly WizardConfig _cfg;
    private RichTextBox _log = new();
    private Button _btnUninstall = new();
    private CheckBox _chkDeleteFiles = new();
    private CheckBox _chkDeleteBackups = new();
    private CheckBox _chkDeleteConfig = new();
    private Panel _actionsPanel = new();
    private bool _running;

    private static readonly Color Accent = Color.FromArgb(79, 70, 229);

    public UninstallForm(WizardConfig cfg)
    {
        _cfg = cfg;
        InitializeForm();
        RunHealthCheck();
    }

    private void InitializeForm()
    {
        Text            = $"Desinstalar — {AppProfile.AppName}";
        Size            = new Size(700, 560);
        MinimumSize     = new Size(600, 480);
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9.5f);
        BackColor       = Color.White;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = false;

        // Header
        var header = new Panel
        {
            Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(180, 30, 30),
            Padding = new Padding(20, 12, 20, 8)
        };
        header.Controls.Add(new Label
        {
            Text = "🗑  Desinstalación de " + AppProfile.AppName,
            Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 13f),
            TextAlign = ContentAlignment.MiddleLeft
        });

        // Log area
        _log = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true,
            BackColor = Color.FromArgb(18, 18, 24),
            ForeColor = Color.FromArgb(200, 210, 220),
            Font = new Font("Consolas", 9f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
        };

        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8) };
        logPanel.Controls.Add(_log);

        // Options panel
        _actionsPanel = new Panel
        {
            Dock = DockStyle.Bottom, Height = 160,
            BackColor = Color.FromArgb(248, 248, 252),
            Padding = new Padding(20, 12, 20, 12),
            Visible = false,
        };

        var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(220, 220, 230) };
        _actionsPanel.Controls.Add(sep);

        var optLabel = new Label
        {
            Text = "Opciones de limpieza:",
            Dock = DockStyle.Top, Height = 26,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Color.FromArgb(60, 60, 80),
            Padding = new Padding(0, 6, 0, 0)
        };

        _chkDeleteFiles = new CheckBox
        {
            Text = $"Eliminar archivos de instalación  ({_cfg.RootDirectory})",
            Dock = DockStyle.Top, Height = 24, Checked = false,
            ForeColor = Color.FromArgb(60, 60, 80)
        };
        _chkDeleteBackups = new CheckBox
        {
            Text = $"Eliminar backups  ({_cfg.BackupDirectory})",
            Dock = DockStyle.Top, Height = 24, Checked = false,
            ForeColor = Color.FromArgb(180, 60, 60)
        };
        _chkDeleteConfig = new CheckBox
        {
            Text = $"Eliminar configuración guardada  ({_cfg.InstallConfigPath})",
            Dock = DockStyle.Top, Height = 24, Checked = false,
            ForeColor = Color.FromArgb(140, 80, 0)
        };

        _btnUninstall = new Button
        {
            Text = "🗑  Desinstalar",
            Dock = DockStyle.Bottom, Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(180, 30, 30),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 10f),
        };
        _btnUninstall.FlatAppearance.BorderColor = Color.FromArgb(180, 30, 30);
        _btnUninstall.Click += async (_, _) => await RunUninstallAsync();

        // Stack controls bottom-up (DockStyle.Top stacks top-down in reverse add order)
        _actionsPanel.Controls.Add(_btnUninstall);
        _actionsPanel.Controls.Add(_chkDeleteConfig);
        _actionsPanel.Controls.Add(_chkDeleteBackups);
        _actionsPanel.Controls.Add(_chkDeleteFiles);
        _actionsPanel.Controls.Add(optLabel);
        _actionsPanel.Controls.Add(sep);

        Controls.Add(logPanel);
        Controls.Add(_actionsPanel);
        Controls.Add(header);
    }

    // ── Health check ──────────────────────────────────────────────────────────

    private void RunHealthCheck()
    {
        Log("═══ Verificación del sistema ═══\n", LogLevel.Header);

        // Config file
        var cfgExists = File.Exists(_cfg.InstallConfigPath) || File.Exists(WizardConfig.LegacyConfigFilePath);
        Log($"  Config wizard   : {_cfg.InstallConfigPath}", File.Exists(_cfg.InstallConfigPath) ? LogLevel.Ok : LogLevel.Warn); if (File.Exists(WizardConfig.LegacyConfigFilePath)) Log($"    Legado        : {WizardConfig.LegacyConfigFilePath}", LogLevel.Warn);
        if (!cfgExists) Log("    ⚠ No se encontró config guardada — puede que nunca se instaló.", LogLevel.Warn);

        // Root dir
        var rootExists = Directory.Exists(_cfg.RootDirectory);
        Log($"  Raíz            : {_cfg.RootDirectory}  [{(rootExists ? "existe" : "no existe")}]",
            rootExists ? LogLevel.Ok : LogLevel.Warn);

        // App dir
        var appExists = Directory.Exists(_cfg.AppDirectory);
        Log($"  App             : {_cfg.AppDirectory}  [{(appExists ? "existe" : "no existe")}]",
            appExists ? LogLevel.Ok : LogLevel.Warn);

        // Runtime exe
        var runtimeExe = Path.Combine(_cfg.ServiceRuntimeDirectory,
            Path.GetFileName(Application.ExecutablePath));
        var runtimeExists = File.Exists(runtimeExe);
        Log($"  Runtime         : {runtimeExe}  [{(runtimeExists ? "existe" : "no existe")}]",
            runtimeExists ? LogLevel.Ok : LogLevel.Warn);

        // Service status
        Log($"\n  Servicio Windows: '{_cfg.ServiceName}'", LogLevel.Info);
        var (svcState, svcOk) = GetServiceState(_cfg.ServiceName);
        Log($"    Estado        : {svcState}", svcOk ? LogLevel.Ok : LogLevel.Warn);

        // Backup dir
        var backupExists = Directory.Exists(_cfg.BackupDirectory);
        var dumpCount = backupExists
            ? Directory.GetFiles(_cfg.BackupDirectory, "*.dump").Length : 0;
        Log($"\n  Backups         : {_cfg.BackupDirectory}", LogLevel.Info);
        Log($"    Dumps encontrados: {dumpCount}", dumpCount > 0 ? LogLevel.Ok : LogLevel.Warn);

        // PgDump
        var pgOk = File.Exists(_cfg.PgDumpPath);
        Log($"\n  pg_dump.exe     : {_cfg.PgDumpPath}  [{(pgOk ? "encontrado" : "no encontrado")}]",
            pgOk ? LogLevel.Ok : LogLevel.Warn);

        Log("\n═══ Verificación completada ═══", LogLevel.Header);
        Log("  Marca las opciones de limpieza y pulsa Desinstalar.\n", LogLevel.Info);

        ShowActions();
    }

    private static (string state, bool ok) GetServiceState(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            var state = sc.Status.ToString();
            return (state, sc.Status == ServiceControllerStatus.Running);
        }
        catch
        {
            return ("No instalado", false);
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    private async Task RunUninstallAsync()
    {
        if (_running) return;
        _running = true;
        _btnUninstall.Enabled = false;
        _btnUninstall.Text    = "Desinstalando...";

        var deleteFiles   = _chkDeleteFiles.Checked;
        var deleteBackups = _chkDeleteBackups.Checked;
        var deleteConfig  = _chkDeleteConfig.Checked;

        if (!Confirm(BuildConfirmMessage(deleteFiles, deleteBackups, deleteConfig)))
        {
            _running = false;
            _btnUninstall.Enabled = true;
            _btnUninstall.Text    = "🗑  Desinstalar";
            return;
        }

        Log("\n═══ Iniciando desinstalación ═══\n", LogLevel.Header);

        await Task.Run(() =>
        {
            // 1. Stop and delete service
            StopAndDeleteService();

            // 2. Wait for process to fully release files
            Thread.Sleep(2000);

            // 3. Delete files if requested
            if (deleteFiles)
                DeleteDirectory(_cfg.AppDirectory, "App");
            if (deleteFiles)
                DeleteDirectory(_cfg.BunDirectory, "Bun");
            if (deleteFiles)
                DeleteDirectory(_cfg.ServiceLogDirectory, "Logs");
            if (deleteFiles)
                DeleteDirectory(_cfg.ServiceRuntimeDirectory, "Runtime");

            if (deleteBackups)
                DeleteDirectory(_cfg.BackupDirectory, "Backups");

            // 4. Try root dir if now empty
            if (deleteFiles && Directory.Exists(_cfg.RootDirectory))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(_cfg.RootDirectory).Any())
                    {
                        Directory.Delete(_cfg.RootDirectory);
                        Log($"  ✓ Raíz eliminada: {_cfg.RootDirectory}", LogLevel.Ok);
                    }
                    else
                    {
                        Log($"  ! Raíz no vacía, dejada intacta: {_cfg.RootDirectory}", LogLevel.Warn);
                    }
                }
                catch (Exception ex)
                {
                    Log($"  ! No se pudo eliminar raíz: {ex.Message}", LogLevel.Warn);
                }
            }

            // 5. Delete config
            if (deleteConfig)
            {
                try
                {
                    if (File.Exists(_cfg.InstallConfigPath))
                    {
                        File.Delete(_cfg.InstallConfigPath); if (File.Exists(WizardConfig.LegacyConfigFilePath)) File.Delete(WizardConfig.LegacyConfigFilePath);
                        Log($"  ✓ Config eliminada: {_cfg.InstallConfigPath}", LogLevel.Ok);
                    }
                }
                catch (Exception ex)
                {
                    Log($"  ! No se pudo eliminar config: {ex.Message}", LogLevel.Warn);
                }
            }
        });

        Log("\n══════════════════════════════════════════", LogLevel.Ok);
        Log("  ✅  Desinstalación completada", LogLevel.Ok);
        Log("══════════════════════════════════════════", LogLevel.Ok);

        RunOnUi(_btnUninstall, () =>
        {
            _btnUninstall.Text      = "Cerrar";
            _btnUninstall.BackColor = Color.FromArgb(60, 60, 80);
            _btnUninstall.Enabled   = true;
            _btnUninstall.Click    -= null;
            _btnUninstall.Click    += (_, _) => Close();
        });

        _running = false;
    }

    private void StopAndDeleteService()
    {
        Log($"  Deteniendo servicio '{_cfg.ServiceName}'...", LogLevel.Info);
        try
        {
            using var sc = new ServiceController(_cfg.ServiceName);
            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
            Log($"  ✓ Servicio detenido.", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            Log($"  ! Stop: {ex.Message}", LogLevel.Warn);
        }

        Log($"  Eliminando servicio '{_cfg.ServiceName}'...", LogLevel.Info);
        try
        {
            RunSc("delete", _cfg.ServiceName);
            Log($"  ✓ Servicio eliminado.", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            Log($"  ! Delete: {ex.Message}", LogLevel.Warn);
        }
    }

    private void DeleteDirectory(string path, string label)
    {
        if (!Directory.Exists(path))
        {
            Log($"  ! {label}: no existe ({path})", LogLevel.Warn);
            return;
        }
        Log($"  Eliminando {label}: {path}", LogLevel.Info);
        try
        {
            Directory.Delete(path, recursive: true);
            Log($"  ✓ {label} eliminado.", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            Log($"  ! {label}: {ex.Message}", LogLevel.Warn);
        }
    }

    private void RunSc(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new Exception($"sc.exe {string.Join(" ", args)} falló (código {p.ExitCode})");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildConfirmMessage(bool files, bool backups, bool config)
    {
        var lines = new List<string> { "¿Confirmar desinstalación?\n" };
        lines.Add("  • Detener y eliminar el servicio de Windows");
        if (files)   lines.Add($"  • Eliminar archivos de instalación");
        if (backups) lines.Add($"  • ⚠  ELIMINAR TODOS LOS BACKUPS");
        if (config)  lines.Add($"  • Eliminar configuración guardada");
        lines.Add("\nEsta acción no se puede deshacer.");
        return string.Join("\n", lines);
    }

    private void ShowActions() =>
        RunOnUi(_actionsPanel, () => _actionsPanel.Visible = true);

    private bool Confirm(string msg) =>
        MessageBox.Show(msg, "Confirmar desinstalación",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private enum LogLevel { Header, Ok, Info, Warn, Error }

    private void Log(string text, LogLevel level)
    {
        if (_log.IsDisposed) return;
        RunOnUi(_log, () =>
        {
            Color clr = level switch
            {
                LogLevel.Header => Color.FromArgb(140, 160, 255),
                LogLevel.Ok     => Color.FromArgb(80, 220, 120),
                LogLevel.Warn   => Color.FromArgb(255, 180, 60),
                LogLevel.Error  => Color.FromArgb(255, 80, 80),
                _               => Color.FromArgb(200, 210, 220),
            };
            _log.SelectionStart  = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor  = clr;
            _log.AppendText(text + "\n");
            _log.ScrollToCaret();
        });
    }

    private static void RunOnUi(Control c, Action a)
    {
        if (c.IsDisposed) return;
        if (!c.IsHandleCreated || !c.InvokeRequired) { a(); return; }
        c.Invoke(a);
    }
}

