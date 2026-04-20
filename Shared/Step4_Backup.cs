namespace SysCondaWizard;

/// <summary>Step 4 — configure pg_dump backup schedule run by the Windows Service.</summary>
public class Step4_Backup : IWizardStep
{
    public string Title => "Backup automático (pg_dump)";

    private CheckBox _chkEnable = new();
    private TextBox _txtPgDump = new();
    private TextBox _txtWindowStart = new();
    private TextBox _txtWindowEnd = new();
    private Label _lblShots = new();
    private CheckBox _chkRestoreOnInstall = new();
    private TextBox _txtPgRestore = new();
    private TextBox _txtRestoreDump = new();
    private Panel _detailPanel = new();
    private Panel _restorePgRow = new();
    private Panel _restoreDumpRow = new();
    private Label _restoreHint = new();
    private Label _lblBackupDir = new();
    private Label _lblPgStatus = new();

    public Control BuildUI(WizardConfig cfg)
    {
        var root = WizardUi.MakeScrollPanel();

        WizardUi.SectionLabel(root, "Backup automático de PostgreSQL");
        WizardUi.Hint(root,
            "El servicio ejecuta pg_dump 4 veces al día, distribuidas uniformemente dentro de la ventana.\n" +
            "Retención automática: 7 diarios + 4 semanales + 3 mensuales.");

        _chkEnable = new CheckBox
        {
            Text = "Habilitar backups automáticos",
            Checked = cfg.EnableBackups,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        };
        WizardUi.AddRow(root, _chkEnable);

        _detailPanel = new Panel { Visible = cfg.EnableBackups };

        // Backup directory (read-only info)
        var rowBackupDir = MakeRow("Carpeta de backups");
        _lblBackupDir = new Label
        {
            Text = cfg.BackupDirectory,
            Location = new Point(168, 6),
            AutoSize = true,
            ForeColor = Color.FromArgb(70, 70, 90)
        };
        rowBackupDir.Controls.Add(_lblBackupDir);
        WizardUi.AddRow(_detailPanel, rowBackupDir);

        // PG detection
        var rowDetect = MakeRow("PostgreSQL 18.x");
        _lblPgStatus = new Label { AutoSize = true, Location = new Point(168, 6), ForeColor = Color.FromArgb(70, 70, 90) };
        var btnDetectPg = WizardUi.SmallButton("Re-escanear", 100);
        btnDetectPg.Location = new Point(476, 1);
        btnDetectPg.Click += (_, _) => RefreshDetectedPostgresPaths();
        rowDetect.Controls.Add(_lblPgStatus);
        rowDetect.Controls.Add(btnDetectPg);
        WizardUi.AddRow(_detailPanel, rowDetect);

        AddBrowseField(_detailPanel, "Ruta de pg_dump.exe", ref _txtPgDump,
            cfg.PgDumpPath, isFolder: false, filter: "pg_dump.exe|pg_dump.exe");

        // Window start
        var rowStart = MakeRow("Ventana inicio");
        _txtWindowStart = WizardUi.TextBox(cfg.BackupWindowStart, WizardConfig.DefaultBackupWindowStart);
        _txtWindowStart.Width = 80;
        _txtWindowStart.Location = new Point(168, 2);
        rowStart.Controls.Add(_txtWindowStart);
        rowStart.Controls.Add(new Label
        {
            Text = $"(HH:mm — por defecto {WizardConfig.DefaultBackupWindowStart}, hora Bolivia)",
            Location = new Point(256, 6),
            AutoSize = true,
            ForeColor = Color.Gray
        });
        WizardUi.AddRow(_detailPanel, rowStart);

        // Window end
        var rowEnd = MakeRow("Ventana fin");
        _txtWindowEnd = WizardUi.TextBox(cfg.BackupWindowEnd, WizardConfig.DefaultBackupWindowEnd);
        _txtWindowEnd.Width = 80;
        _txtWindowEnd.Location = new Point(168, 2);
        rowEnd.Controls.Add(_txtWindowEnd);
        rowEnd.Controls.Add(new Label
        {
            Text = $"(HH:mm — por defecto {WizardConfig.DefaultBackupWindowEnd}, hora Bolivia)",
            Location = new Point(256, 6),
            AutoSize = true,
            ForeColor = Color.Gray
        });
        WizardUi.AddRow(_detailPanel, rowEnd);

        // Live shots label — updates on every keystroke
        var rowInfo = MakeRow(string.Empty);
        _lblShots = new Label
        {
            Location = new Point(168, 3),
            AutoSize = true,
            Font = new Font("Segoe UI", 8f, FontStyle.Italic),
        };
        rowInfo.Controls.Add(_lblShots);
        WizardUi.AddRow(_detailPanel, rowInfo);

        _txtWindowStart.TextChanged += (_, _) => RefreshShotsLabel();
        _txtWindowEnd.TextChanged += (_, _) => RefreshShotsLabel();
        RefreshShotsLabel();

        // Retention info
        var rowRetention = MakeRow("Retención");
        rowRetention.Controls.Add(new Label
        {
            Text = "7 diarios  +  4 semanales  +  3 mensuales  (automatico)",
            Location = new Point(168, 6),
            AutoSize = true,
            ForeColor = Color.FromArgb(34, 120, 60),
        });
        WizardUi.AddRow(_detailPanel, rowRetention);

        // Restore section
        var rowRestore = MakeRow(string.Empty);
        _chkRestoreOnInstall = new CheckBox
        {
            Text = "Restaurar la base de datos durante la instalación",
            Checked = cfg.RestoreDatabaseOnInstall,
            AutoSize = true,
            Location = new Point(168, 3)
        };
        rowRestore.Controls.Add(_chkRestoreOnInstall);
        WizardUi.AddRow(_detailPanel, rowRestore);

        AddBrowseField(_detailPanel, "Ruta de pg_restore.exe", ref _txtPgRestore,
            cfg.PgRestorePath, isFolder: false, filter: "pg_restore.exe|pg_restore.exe");
        _restorePgRow = (Panel)_txtPgRestore.Parent!;

        AddBrowseField(_detailPanel, "Dump a restaurar", ref _txtRestoreDump,
            cfg.RestoreDumpPath, isFolder: false, filter: "PostgreSQL dump|*.dump|Todos|*.*");
        _restoreDumpRow = (Panel)_txtRestoreDump.Parent!;

        _restoreHint = new Label
        {
            Text = "Si se deja vacío, el instalador usará el dump más reciente del directorio de backups.",
            AutoSize = true,
            ForeColor = Color.Gray,
            Location = new Point(168, 0)
        };
        var restoreHintRow = new Panel { Height = 22, Margin = new Padding(0, 3, 0, 0) };
        restoreHintRow.Controls.Add(_restoreHint);
        WizardUi.AddRow(_detailPanel, restoreHintRow);

        _detailPanel.Height = 400;
        WizardUi.AddRow(root, _detailPanel);

        _chkEnable.CheckedChanged += (_, _) => _detailPanel.Visible = _chkEnable.Checked;
        _chkRestoreOnInstall.CheckedChanged += (_, _) => ToggleRestoreFields();

        RefreshDetectedPostgresPaths();
        ToggleRestoreFields();

        WizardUi.InfoBox(root,
            "🗄  Backups en formato .dump (pg_restore compatible).\n" +
            "   Ejecutados por el servicio de Windows — sin dependencias externas.\n" +
            "   Catch-up automático al reiniciar si se perdió alguna copia.\n" +
            "   Solo se admite PostgreSQL 18.x.");

        return root;
    }

    // ── Shot label ────────────────────────────────────────────────────────────

    private void RefreshShotsLabel()
    {
        var shots = ComputeShots(_txtWindowStart.Text.Trim(), _txtWindowEnd.Text.Trim());
        if (shots.Length == 0)
        {
            _lblShots.Text = "Introduce horas válidas en formato HH:mm";
            _lblShots.ForeColor = Color.FromArgb(180, 60, 60);
        }
        else
        {
            _lblShots.Text = $"Disparos: {string.Join(" / ", shots)}  (hora Bolivia)";
            _lblShots.ForeColor = Color.FromArgb(34, 120, 60);
        }
    }

    /// <summary>Always returns exactly 4 shots evenly distributed across the window.</summary>
    private static string[] ComputeShots(string startStr, string endStr)
    {
        if (!TryParseTime(startStr, out var start) || !TryParseTime(endStr, out var end))
            return Array.Empty<string>();
        if (end <= start) return Array.Empty<string>();

        var interval = TimeSpan.FromTicks((end - start).Ticks / 3);
        var shots = new string[4];
        for (int i = 0; i < 4; i++)
        {
            var t = start.Add(TimeSpan.FromTicks(interval.Ticks * i));
            shots[i] = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}";
        }
        return shots;
    }

    private static bool TryParseTime(string s, out TimeSpan result)
    {
        result = default;
        var parts = s.Split(':');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59) return false;
        result = new TimeSpan(h, m, 0);
        return true;
    }

    // ── Visibility ────────────────────────────────────────────────────────────

    private void ToggleRestoreFields()
    {
        var on = _chkRestoreOnInstall.Checked;
        _restorePgRow.Visible = on;
        _restoreDumpRow.Visible = on;
        _restoreHint.Visible = on;
    }

    private void RefreshDetectedPostgresPaths()
    {
        var binDir = PostgresBinaryLocator.FindBinDirectory();
        if (!string.IsNullOrWhiteSpace(binDir))
        {
            _txtPgDump.Text = Path.Combine(binDir, "pg_dump.exe");
            _txtPgRestore.Text = Path.Combine(binDir, "pg_restore.exe");
            _lblPgStatus.Text = $"Detectado en {binDir}";
            _lblPgStatus.ForeColor = Color.FromArgb(34, 120, 60);
            return;
        }
        _lblPgStatus.Text = "No se encontraron binarios válidos de PostgreSQL 18.x";
        _lblPgStatus.ForeColor = Color.FromArgb(180, 90, 40);
    }

    // ── UI builders ───────────────────────────────────────────────────────────

    private static Panel MakeRow(string label)
    {
        var row = new Panel { Height = 28, Margin = new Padding(0, 3, 0, 0) };
        if (!string.IsNullOrWhiteSpace(label))
            row.Controls.Add(new Label
            {
                Text = label + ":",
                Width = 160,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(0, 4)
            });
        return row;
    }

    private static void AddBrowseField(Panel root, string label, ref TextBox tb,
        string value, bool isFolder, string filter = "")
    {
        var row = MakeRow(label);
        tb = WizardUi.TextBox(value);
        tb.Width = 300;
        tb.Location = new Point(168, 2);
        var btn = WizardUi.SmallButton("…", 42);
        btn.Location = new Point(476, 1);
        var tbRef = tb;
        btn.Click += (_, _) =>
        {
            if (isFolder)
            {
                using var dlg = new FolderBrowserDialog { SelectedPath = tbRef.Text };
                if (dlg.ShowDialog() == DialogResult.OK) tbRef.Text = dlg.SelectedPath;
            }
            else
            {
                using var dlg = new OpenFileDialog { Filter = string.IsNullOrEmpty(filter) ? "All|*.*" : filter };
                if (dlg.ShowDialog() == DialogResult.OK) tbRef.Text = dlg.FileName;
            }
        };
        row.Controls.Add(tb);
        row.Controls.Add(btn);
        WizardUi.AddRow(root, row);
    }

    // ── IWizardStep ───────────────────────────────────────────────────────────

    public string? Validate(WizardConfig cfg)
    {
        if (!_chkEnable.Checked) return null;

        if (!PostgresBinaryLocator.IsSupportedBinaryPath(_txtPgDump.Text, "pg_dump.exe"))
            return $"No se encontró pg_dump.exe de PostgreSQL 18 en:\n{_txtPgDump.Text}";

        if (!TryParseTime(_txtWindowStart.Text.Trim(), out _))
            return "Hora de inicio inválida. Usa formato HH:mm (ej. 08:00).";
        if (!TryParseTime(_txtWindowEnd.Text.Trim(), out _))
            return $"Hora de fin inválida. Usa formato HH:mm (ej. {WizardConfig.DefaultBackupWindowEnd}).";

        if (_chkRestoreOnInstall.Checked &&
            !PostgresBinaryLocator.IsSupportedBinaryPath(_txtPgRestore.Text, "pg_restore.exe"))
            return $"No se encontró pg_restore.exe de PostgreSQL 18 en:\n{_txtPgRestore.Text}";

        return null;
    }

    public void Save(WizardConfig cfg)
    {
        cfg.EnableBackups = _chkEnable.Checked;
        if (!cfg.EnableBackups) return;

        cfg.PgDumpPath = _txtPgDump.Text.Trim();
        cfg.BackupWindowStart = _txtWindowStart.Text.Trim();
        cfg.BackupWindowEnd = _txtWindowEnd.Text.Trim();
        cfg.BackupDays = "MON,TUE,WED,THU,FRI,SAT,SUN"; // always all 7
        cfg.BackupTestMode = false;
        cfg.RestoreDatabaseOnInstall = _chkRestoreOnInstall.Checked;
        cfg.PgRestorePath = _txtPgRestore.Text.Trim();
        cfg.RestoreDumpPath = _txtRestoreDump.Text.Trim();
    }
}
