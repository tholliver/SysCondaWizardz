namespace SysCondaWizard;

/// <summary>Step 4 — configure pg_dump backup schedule run by the Windows Service.</summary>
public class Step4_Backup : IWizardStep
{
    public string Title => "Backup automático (pg_dump)";

    private CheckBox _chkTestMode = new();
    private Panel _rowWindowStart = new();
    private Panel _rowWindowEnd = new();
    private Panel _rowDays = new();
    private CheckBox _chkEnable = new();
    private TextBox _txtPgDump = new();
    private TextBox _txtWindowStart = new();
    private TextBox _txtWindowEnd = new();
    private CheckBox[] _dayChecks = Array.Empty<CheckBox>();
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
            "El servicio ejecuta pg_dump cada 3 horas dentro de la ventana configurada.\n" +
            "Retención: 7 diarios + 4 semanales + 3 mensuales (automatico).");

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
        _lblPgStatus = new Label
        {
            AutoSize = true,
            Location = new Point(168, 6),
            ForeColor = Color.FromArgb(70, 70, 90)
        };
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
        _txtWindowStart = WizardUi.TextBox(cfg.BackupWindowStart, "08:00");
        _txtWindowStart.Width = 80;
        _txtWindowStart.Location = new Point(168, 2);
        var hintStart = new Label
        {
            Text = "(HH:mm — primer backup del dia)",
            Location = new Point(256, 6),
            AutoSize = true,
            ForeColor = Color.Gray
        };
        rowStart.Controls.Add(_txtWindowStart);
        rowStart.Controls.Add(hintStart);
        _rowWindowStart = rowStart;
        WizardUi.AddRow(_detailPanel, rowStart);

        // Window end
        var rowEnd = MakeRow("Ventana fin");
        _txtWindowEnd = WizardUi.TextBox(cfg.BackupWindowEnd, "17:00");
        _txtWindowEnd.Width = 80;
        _txtWindowEnd.Location = new Point(168, 2);
        var hintEnd = new Label
        {
            Text = "(HH:mm — ultimo backup del dia)",
            Location = new Point(256, 6),
            AutoSize = true,
            ForeColor = Color.Gray
        };
        rowEnd.Controls.Add(_txtWindowEnd);
        rowEnd.Controls.Add(hintEnd);
        _rowWindowEnd = rowEnd;
        WizardUi.AddRow(_detailPanel, rowEnd);

        // Schedule info label
        var rowInfo = MakeRow(string.Empty);
        var lblInfo = new Label
        {
            Text = "Dispara cada 3 horas dentro de la ventana: 08:00 / 11:00 / 14:00 / 17:00",
            Location = new Point(168, 3),
            AutoSize = true,
            ForeColor = Color.FromArgb(34, 120, 60),
            Font = new Font("Segoe UI", 8f, FontStyle.Italic),
        };
        rowInfo.Controls.Add(lblInfo);
        WizardUi.AddRow(_detailPanel, rowInfo);

        // Update info label dynamically when times change
        void UpdateInfoLabel()
        {
            var s = _txtWindowStart.Text.Trim();
            var e = _txtWindowEnd.Text.Trim();
            lblInfo.Text = $"Dispara cada 3 horas dentro de la ventana: {s} / +3h / +6h / {e}";
        }
        _txtWindowStart.TextChanged += (_, _) => UpdateInfoLabel();
        _txtWindowEnd.TextChanged += (_, _) => UpdateInfoLabel();

        // Days
        var rowDays = MakeRow("Días");
        rowDays.Height = 32;
        var dayFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Width = 420,
            Height = 28,
            Location = new Point(168, 0),
        };
        var dayLabels = new[] { "LUN", "MAR", "MIÉ", "JUE", "VIE", "SÁB", "DOM" };
        var dayMap = new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        var defaultDays = cfg.BackupDays.Split(',');
        _dayChecks = new CheckBox[7];
        for (int i = 0; i < 7; i++)
        {
            var chk = new CheckBox
            {
                Text = dayLabels[i],
                Width = 52,
                Height = 24,
                Checked = defaultDays.Contains(dayMap[i]),
                Margin = new Padding(0, 0, 2, 0),
                Tag = i,
            };
            dayFlow.Controls.Add(chk);
            _dayChecks[i] = chk;
        }
        rowDays.Controls.Add(dayFlow);
        _rowDays = rowDays;
        WizardUi.AddRow(_detailPanel, rowDays);

        // Test mode
        var rowTest = MakeRow(string.Empty);
        _chkTestMode = new CheckBox
        {
            Text = "⚡ Modo prueba (ejecutar cada minuto)",
            Checked = cfg.BackupTestMode,
            AutoSize = true,
            Location = new Point(168, 3),
            ForeColor = Color.FromArgb(160, 80, 0),
        };
        rowTest.Controls.Add(_chkTestMode);
        WizardUi.AddRow(_detailPanel, rowTest);

        var rowTestWarn = MakeRow(string.Empty);
        var lblTestWarn = new Label
        {
            Text = "⚠ Desactiva antes de entregar al cliente",
            Location = new Point(168, 3),
            AutoSize = true,
            ForeColor = Color.FromArgb(180, 60, 60),
            Font = new Font("Segoe UI", 8f, FontStyle.Italic),
        };
        rowTestWarn.Controls.Add(lblTestWarn);
        WizardUi.AddRow(_detailPanel, rowTestWarn);
        _chkTestMode.CheckedChanged += (_, _) =>
        {
            lblTestWarn.Visible = _chkTestMode.Checked;
            ToggleTestMode();
        };

        // Retention info
        var rowRetention = MakeRow("Retención");
        var lblRetention = new Label
        {
            Text = "7 diarios  +  4 semanales  +  3 mensuales  (automatico)",
            Location = new Point(168, 6),
            AutoSize = true,
            ForeColor = Color.FromArgb(34, 120, 60),
        };
        rowRetention.Controls.Add(lblRetention);
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
        lblTestWarn.Visible = cfg.BackupTestMode;
        ToggleTestMode();

        WizardUi.InfoBox(root,
            "🗄  Backups en formato .dump (pg_restore compatible).\n" +
            "   Ejecutados por el servicio de Windows — sin dependencias externas.\n" +
            "   4 snapshots diarios cada 3 horas dentro de la ventana configurada.\n" +
            "   Catch-up automatico al reiniciar si se perdio alguna copia.\n" +
            "   Solo se admite PostgreSQL 18.x.");

        return root;
    }

    private void ToggleRestoreFields()
    {
        var enabled = _chkRestoreOnInstall.Checked;
        _restorePgRow.Visible = enabled;
        _restoreDumpRow.Visible = enabled;
        _restoreHint.Visible = enabled;
    }

    private void ToggleTestMode()
    {
        var isTest = _chkTestMode.Checked;
        _rowWindowStart.Visible = !isTest;
        _rowWindowEnd.Visible = !isTest;
        _rowDays.Visible = !isTest;
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

    private static Panel MakeRow(string label)
    {
        var row = new Panel { Height = 28, Margin = new Padding(0, 3, 0, 0) };
        if (!string.IsNullOrWhiteSpace(label))
        {
            var lbl = new Label
            {
                Text = label + ":",
                Width = 160,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(0, 4)
            };
            row.Controls.Add(lbl);
        }
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
                using var dlg = new OpenFileDialog
                {
                    Filter = string.IsNullOrEmpty(filter) ? "All|*.*" : filter
                };
                if (dlg.ShowDialog() == DialogResult.OK) tbRef.Text = dlg.FileName;
            }
        };
        row.Controls.Add(tb);
        row.Controls.Add(btn);
        WizardUi.AddRow(root, row);
    }

    public string? Validate(WizardConfig cfg)
    {
        if (!_chkEnable.Checked) return null;
        if (!PostgresBinaryLocator.IsSupportedBinaryPath(_txtPgDump.Text, "pg_dump.exe"))
            return $"No se encontró pg_dump.exe de PostgreSQL 18 en:\n{_txtPgDump.Text}";

        if (!_chkTestMode.Checked)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    _txtWindowStart.Text.Trim(), @"^\d{1,2}:\d{2}$"))
                return "Hora de inicio inválida. Usa formato HH:mm (ej. 08:00).";
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    _txtWindowEnd.Text.Trim(), @"^\d{1,2}:\d{2}$"))
                return "Hora de fin inválida. Usa formato HH:mm (ej. 17:00).";
            if (!_dayChecks.Any(c => c.Checked))
                return "Selecciona al menos un día para el backup.";
        }

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
        cfg.BackupTestMode = _chkTestMode.Checked;
        cfg.RestoreDatabaseOnInstall = _chkRestoreOnInstall.Checked;
        cfg.PgRestorePath = _txtPgRestore.Text.Trim();
        cfg.RestoreDumpPath = _txtRestoreDump.Text.Trim();

        var dayMap = new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        var checkedDays = new List<string>();
        for (int i = 0; i < _dayChecks.Length; i++)
            if (_dayChecks[i].Checked) checkedDays.Add(dayMap[i]);
        cfg.BackupDays = string.Join(",", checkedDays);
    }
}
