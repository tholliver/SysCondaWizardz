using System.Diagnostics;
using System.ServiceProcess;

namespace SysCondaWizard;

/// <summary>
/// Standalone uninstall dialog. Shows health checks for the current installation,
/// then offers a clean uninstall: stop+delete service, remove firewall rule,
/// and optionally delete files/backups/config.
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

    private EventHandler? _btnClickHandler;

    private static readonly Color Accent = Color.FromArgb(79, 70, 229);

    public UninstallForm(WizardConfig cfg)
    {
        _cfg = cfg;
        InitializeForm();
        Load += (_, _) => RunHealthCheck();
    }

    private void InitializeForm()
    {
        Text = $"Desinstalar — {AppProfile.AppName}";
        Size = new Size(700, 560);
        MinimumSize = new Size(600, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.White;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;

        // Header
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Color.FromArgb(180, 30, 30),
            Padding = new Padding(20, 12, 20, 8)
        };
        header.Controls.Add(new Label
        {
            Text = "🗑  Desinstalación de " + AppProfile.AppName,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 13f),
            TextAlign = ContentAlignment.MiddleLeft
        });

        // Log area
        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
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
            Dock = DockStyle.Bottom,
            Height = 160,
            BackColor = Color.FromArgb(248, 248, 252),
            Padding = new Padding(20, 12, 20, 12),
            Visible = false,
        };

        var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(220, 220, 230) };
        _actionsPanel.Controls.Add(sep);

        var optLabel = new Label
        {
            Text = "Opciones de limpieza:",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Color.FromArgb(60, 60, 80),
            Padding = new Padding(0, 6, 0, 0)
        };

        _chkDeleteFiles = new CheckBox
        {
            Text = $"Eliminar archivos de instalación  ({_cfg.RootDirectory})",
            Dock = DockStyle.Top,
            Height = 24,
            Checked = false,
            ForeColor = Color.FromArgb(60, 60, 80)
        };
        _chkDeleteBackups = new CheckBox
        {
            Text = $"Eliminar backups  ({_cfg.BackupDirectory})",
            Dock = DockStyle.Top,
            Height = 24,
            Checked = false,
            ForeColor = Color.FromArgb(180, 60, 60)
        };
        _chkDeleteConfig = new CheckBox
        {
            Text = $"Eliminar configuración guardada  ({_cfg.InstallConfigPath})",
            Dock = DockStyle.Top,
            Height = 24,
            Checked = false,
            ForeColor = Color.FromArgb(140, 80, 0)
        };

        _btnUninstall = new Button
        {
            Text = "🗑  Desinstalar",
            Dock = DockStyle.Bottom,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(180, 30, 30),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Semibold", 10f),
        };
        _btnUninstall.FlatAppearance.BorderColor = Color.FromArgb(180, 30, 30);

        _btnClickHandler = async (_, _) => await RunUninstallAsync();
        _btnUninstall.Click += _btnClickHandler;

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

        var cfgExists = File.Exists(_cfg.InstallConfigPath) || File.Exists(WizardConfig.LegacyConfigFilePath);
        Log($"  Config wizard   : {_cfg.InstallConfigPath}", File.Exists(_cfg.InstallConfigPath) ? LogLevel.Ok : LogLevel.Warn);
        if (File.Exists(WizardConfig.LegacyConfigFilePath))
            Log($"    Legado        : {WizardConfig.LegacyConfigFilePath}", LogLevel.Warn);
        if (!cfgExists)
            Log("    ⚠ No se encontró config guardada — puede que nunca se instaló.", LogLevel.Warn);

        var rootExists = Directory.Exists(_cfg.RootDirectory);
        Log($"  Raíz            : {_cfg.RootDirectory}  [{(rootExists ? "existe" : "no existe")}]",
            rootExists ? LogLevel.Ok : LogLevel.Warn);

        var appExists = Directory.Exists(_cfg.AppDirectory);
        Log($"  App             : {_cfg.AppDirectory}  [{(appExists ? "existe" : "no existe")}]",
            appExists ? LogLevel.Ok : LogLevel.Warn);

        var runtimeExe = Path.Combine(_cfg.ServiceRuntimeDirectory,
            Path.GetFileName(Application.ExecutablePath));
        var runtimeExists = File.Exists(runtimeExe);
        Log($"  Runtime         : {runtimeExe}  [{(runtimeExists ? "existe" : "no existe")}]",
            runtimeExists ? LogLevel.Ok : LogLevel.Warn);

        var toolsExists = Directory.Exists(_cfg.ToolsDirectory);
        Log($"  Herramientas    : {_cfg.ToolsDirectory}  [{(toolsExists ? "existe" : "no existe")}]",
            toolsExists ? LogLevel.Ok : LogLevel.Warn);

        Log($"\n  Servicio Windows: '{_cfg.ServiceName}'", LogLevel.Info);
        var (svcState, svcOk) = GetServiceState(_cfg.ServiceName);
        Log($"    Estado        : {svcState}", svcOk ? LogLevel.Ok : LogLevel.Warn);

        // Firewall rule check
        var fwActive = FirewallRuleExists(_cfg.AppPort);
        Log($"\n  Regla Firewall  : puerto {_cfg.AppPort}  [{(fwActive ? "activa" : "no encontrada")}]",
            fwActive ? LogLevel.Ok : LogLevel.Warn);
        var urlAclActive = UrlAclExists(_cfg.AppPort);
        Log($"  URL reservation : http://+:{_cfg.AppPort}/  [{(urlAclActive ? "activa" : "no encontrada")}]",
            urlAclActive ? LogLevel.Ok : LogLevel.Warn);

        var backupExists = Directory.Exists(_cfg.BackupDirectory);
        var dumpCount = backupExists
            ? Directory.GetFiles(_cfg.BackupDirectory, "*.dump").Length : 0;
        Log($"\n  Backups         : {_cfg.BackupDirectory}", LogLevel.Info);
        Log($"    Dumps encontrados: {dumpCount}", dumpCount > 0 ? LogLevel.Ok : LogLevel.Warn);

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

    /// <summary>Checks whether the wizard's firewall rule exists (no window).</summary>
    private static bool FirewallRuleExists(string port)
    {
        var ruleName = $"{AppProfile.ServiceDisplay} Port {port}";
        var psi = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 && output.Contains("Rule Name", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UrlAclExists(string port)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = "http show urlacl",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 && output.Contains($"http://+:{port}/", StringComparison.OrdinalIgnoreCase);
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    private async Task RunUninstallAsync()
    {
        if (_running) return;
        _running = true;
        _btnUninstall.Enabled = false;
        _btnUninstall.Text = "Desinstalando...";

        var deleteFiles = _chkDeleteFiles.Checked;
        var deleteBackups = _chkDeleteBackups.Checked;
        var deleteConfig = _chkDeleteConfig.Checked;

        if (!Confirm(BuildConfirmMessage(deleteFiles, deleteBackups, deleteConfig)))
        {
            _running = false;
            _btnUninstall.Enabled = true;
            _btnUninstall.Text = "🗑  Desinstalar";
            return;
        }

        Log("\n═══ Iniciando desinstalación ═══\n", LogLevel.Header);

        await Task.Run(() =>
        {
            // 1. Stop and delete Windows service
            StopAndDeleteService(_cfg.ServiceName);

            // 2. Remove firewall rule (always — was set during install)
            RemoveFirewallRule(_cfg.AppPort);

            // 3. Wait for process to fully release files
            Thread.Sleep(2000);

            // 4. Delete files if requested
            if (deleteFiles)
                DeleteDirectory(_cfg.AppDirectory, "App");
            if (deleteFiles)
                DeleteDirectory(_cfg.BunDirectory, "Bun");
            if (deleteFiles)
                DeleteDirectory(_cfg.ServiceLogDirectory, "Logs");
            if (deleteFiles)
                DeleteDirectory(_cfg.ServiceRuntimeDirectory, "Runtime");
            if (deleteFiles)
                DeleteDirectory(_cfg.ToolsDirectory, "Herramientas");
            if (deleteFiles)
                DeleteToolShortcut($"{_cfg.ServiceName}-health.cmd");
            if (deleteFiles)
                DeleteToolShortcut($"{_cfg.ServiceName}-uninstall.cmd");

            if (deleteBackups)
                DeleteDirectory(_cfg.BackupDirectory, "Backups");

            // 5. Try root dir if now empty
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

            // 6. Delete config
            if (deleteConfig)
            {
                try
                {
                    if (File.Exists(_cfg.InstallConfigPath))
                        File.Delete(_cfg.InstallConfigPath);
                    if (File.Exists(WizardConfig.LegacyConfigFilePath))
                        File.Delete(WizardConfig.LegacyConfigFilePath);
                    Log($"  ✓ Config eliminada: {_cfg.InstallConfigPath}", LogLevel.Ok);
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

        RunOnUi(this, () =>
        {
            _actionsPanel.Visible = false;

            if (_btnClickHandler is not null)
            {
                _btnUninstall.Click -= _btnClickHandler;
                _btnClickHandler = null;
            }
            _btnUninstall.Text = "✔  Cerrar";
            _btnUninstall.BackColor = Color.FromArgb(40, 120, 60);
            _btnUninstall.FlatAppearance.BorderColor = Color.FromArgb(40, 120, 60);
            _btnUninstall.Enabled = true;
            _btnUninstall.Click += (_, _) => Close();
            _btnUninstall.Dock = DockStyle.Bottom;
            Controls.Add(_btnUninstall);
        });

        _running = false;
    }

    // ── Service ───────────────────────────────────────────────────────────────

    private void StopAndDeleteService(string serviceName)
    {
        Log($"  Deteniendo servicio '{serviceName}'...", LogLevel.Info);
        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
            Log("  ✓ Servicio detenido.", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            Log($"  ! Stop: {ex.Message}", LogLevel.Warn);
        }

        Log($"  Eliminando servicio '{serviceName}'...", LogLevel.Info);
        try
        {
            RunSc("delete", serviceName);
            Log("  ✓ Servicio eliminado.", LogLevel.Ok);
        }
        catch (Exception ex)
        {
            Log($"  ! Delete: {ex.Message}", LogLevel.Warn);
        }
    }

    // ── Firewall ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes the firewall rule created during install.
    /// Uses netsh with CreateNoWindow=true — no cmd or terminal window ever appears.
    /// Non-fatal: if the rule was already gone, logs a warning and continues.
    /// </summary>
    private void RemoveFirewallRule(string port)
    {
        var ruleName = $"{AppProfile.ServiceDisplay} Port {port}";
        Log($"  Eliminando regla de Firewall y URL reservation (puerto {port})...", LogLevel.Info);

        var firewallExit = RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");
        RunNetsh($"http delete urlacl url=http://+:{port}/");

        if (firewallExit == 0)
            Log($"  ✓ Regla de Firewall eliminada (puerto {port}).", LogLevel.Ok);
        else
            Log($"  ! Regla de Firewall no encontrada o ya eliminada (puerto {port}).", LogLevel.Warn);

        Log($"  ✓ URL reservation limpiada si existía: http://+:{port}/", LogLevel.Ok);
    }

    private static int RunNetsh(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    // ── File helpers ──────────────────────────────────────────────────────────

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
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new Exception($"sc.exe {string.Join(" ", args)} falló (código {p.ExitCode})");
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private static string BuildConfirmMessage(bool files, bool backups, bool config)
    {
        var lines = new List<string> { "¿Confirmar desinstalación?\n" };
        lines.Add("  • Detener y eliminar el servicio de Windows");
        lines.Add("  • Eliminar regla de Firewall y URL reservation del puerto de la app");
        if (files) lines.Add("  • Eliminar archivos de instalación");
        if (backups) lines.Add("  • ⚠  ELIMINAR TODOS LOS BACKUPS");
        if (config) lines.Add("  • Eliminar configuración guardada");
        lines.Add("\nEsta acción no se puede deshacer.");
        return string.Join("\n", lines);
    }

    private void ShowActions() =>
        RunOnUi(this, () => _actionsPanel.Visible = true);

    private void DeleteToolShortcut(string fileName)
    {
        var path = Path.Combine(_cfg.RootDirectory, fileName);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Log($"  ✓ Acceso directo eliminado: {path}", LogLevel.Ok);
            }
        }
        catch (Exception ex)
        {
            Log($"  ! No se pudo eliminar acceso directo {path}: {ex.Message}", LogLevel.Warn);
        }
    }

    private bool Confirm(string msg) =>
        MessageBox.Show(msg, "Confirmar desinstalación",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private enum LogLevel { Header, Ok, Info, Warn, Error }

    private void Log(string text, LogLevel level)
    {
        RunOnUi(_log, () =>
        {
            Color clr = level switch
            {
                LogLevel.Header => Color.FromArgb(140, 160, 255),
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

    private static void RunOnUi(Control c, Action a)
    {
        try
        {
            if (c.IsDisposed || !c.IsHandleCreated) return;
            if (c.InvokeRequired)
                c.Invoke(a);
            else
                a();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
