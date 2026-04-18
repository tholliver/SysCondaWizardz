namespace SysCondaWizard;

/// <summary>Step 4 — schedule pg_dump via Windows Task Scheduler (replaces cron).</summary>
public class Step4_Backup : IWizardStep
{
    public string Title => "Backup automático (pg_dump)";

    // Testing mode
    private CheckBox _chkTestMode = new();
    private Panel _rowTime = new();
    private Panel _rowDays = new();
    // All native
    private CheckBox _chkEnable = new();
    private TextBox _txtPgDump = new();
    private TextBox _txtTime = new();
    private NumericUpDown _numKeep = new();
    private CheckBox[] _dayChecks = Array.Empty<CheckBox>();
    private TextBox _txtTaskName = new();
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
            "Crea una tarea en el Programador de tareas de Windows que ejecuta pg_dump\n" +
            "de forma nativa, sin necesidad de PM2 ni cron.");

        _chkEnable = new CheckBox
        {
            Text = "Habilitar backups automáticos",
            Checked = cfg.EnableBackups,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        };
        WizardUi.AddRow(root, _chkEnable);

        _detailPanel = new Panel { Visible = cfg.EnableBackups };

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

        var rowTime = MakeRow("Hora del backup");
        _txtTime = WizardUi.TextBox(cfg.BackupTime, "18:30");
        _txtTime.Width = 80;
        _txtTime.Location = new Point(168, 2);
        var hintTime = new Label { Text = "(formato HH:mm, hora local)", Location = new Point(256, 6), AutoSize = true, ForeColor = Color.Gray };
        rowTime.Controls.Add(_txtTime);
        rowTime.Controls.Add(hintTime);
        _rowTime = rowTime;
        WizardUi.AddRow(_detailPanel, rowTime);

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
        _chkTestMode.CheckedChanged += (_, _) => ToggleTestMode();

        var rowKeep = MakeRow("Conservar últimos N");
        _numKeep = new NumericUpDown { Minimum = 1, Maximum = 99, Value = cfg.KeepFiles, Width = 60, Location = new Point(168, 2) };
        var hintKeep = new Label { Text = "archivos de backup", Location = new Point(234, 6), AutoSize = true, ForeColor = Color.Gray };
        rowKeep.Controls.Add(_numKeep);
        rowKeep.Controls.Add(hintKeep);
        WizardUi.AddRow(_detailPanel, rowKeep);

        var rowTask = MakeRow("Nombre tarea");
        _txtTaskName = WizardUi.TextBox(cfg.TaskSchedulerTaskName);
        _txtTaskName.Width = 260;
        _txtTaskName.Location = new Point(168, 2);
        rowTask.Controls.Add(_txtTaskName);
        WizardUi.AddRow(_detailPanel, rowTask);

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
            Text = "Si se deja vacío, el instalador intentará usar el dump más reciente del directorio de backups.",
            AutoSize = true,
            ForeColor = Color.Gray,
            Location = new Point(168, 0)
        };
        var restoreHintRow = new Panel { Height = 22, Margin = new Padding(0, 3, 0, 0) };
        restoreHintRow.Controls.Add(_restoreHint);
        WizardUi.AddRow(_detailPanel, restoreHintRow);

        _detailPanel.Height = 330;
        WizardUi.AddRow(root, _detailPanel);
        _chkEnable.CheckedChanged += (_, _) => _detailPanel.Visible = _chkEnable.Checked;
        _chkRestoreOnInstall.CheckedChanged += (_, _) => ToggleRestoreFields();
        RefreshDetectedPostgresPaths();
        ToggleRestoreFields();
        ToggleTestMode();

        WizardUi.InfoBox(root,
            "🗄  Los backups se guardan en formato .dump (pg_restore compatible).\n" +
            "   El Programador de tareas de Windows los lanza con tu sesión de usuario.\n" +
            "   La carpeta de backups siempre será <instalación>\\backups.\n" +
            "   Solo se admite PostgreSQL 18.x. Si no existe dump para restaurar, la instalación sigue sin fallar.");

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
        _rowTime.Visible = !isTest;
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
            var lbl = new Label { Text = label + ":", Width = 160, TextAlign = ContentAlignment.MiddleRight, Location = new Point(0, 4) };
            row.Controls.Add(lbl);
        }
        return row;
    }

    private static void AddBrowseField(Panel root, string label, ref TextBox tb, string value, bool isFolder, string filter = "")
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

    public string? Validate(WizardConfig cfg)
    {
        if (!_chkEnable.Checked) return null;
        if (!PostgresBinaryLocator.IsSupportedBinaryPath(_txtPgDump.Text, "pg_dump.exe"))
            return $"No se encontró pg_dump.exe de PostgreSQL 18 en:\n{_txtPgDump.Text}";
        if (!System.Text.RegularExpressions.Regex.IsMatch(_txtTime.Text.Trim(), @"^\d{1,2}:\d{2}$"))
            return "Hora inválida. Usa formato HH:mm (ej. 18:30).";
        if (!_dayChecks.Any(c => c.Checked)) return "Selecciona al menos un día para el backup.";
        if (string.IsNullOrWhiteSpace(_txtTaskName.Text)) return "El nombre de la tarea es requerido.";
        if (_chkRestoreOnInstall.Checked && !PostgresBinaryLocator.IsSupportedBinaryPath(_txtPgRestore.Text, "pg_restore.exe"))
            return $"No se encontró pg_restore.exe de PostgreSQL 18 en:\n{_txtPgRestore.Text}";
        return null;
    }

    public void Save(WizardConfig cfg)
    {
        cfg.EnableBackups = _chkEnable.Checked;
        if (!cfg.EnableBackups) return;

        cfg.PgDumpPath = _txtPgDump.Text.Trim();
        cfg.BackupTime = _txtTime.Text.Trim();
        cfg.BackupTestMode = _chkTestMode.Checked;
        cfg.KeepFiles = (int)_numKeep.Value;
        cfg.TaskSchedulerTaskName = _txtTaskName.Text.Trim();
        cfg.RestoreDatabaseOnInstall = _chkRestoreOnInstall.Checked;
        cfg.PgRestorePath = _txtPgRestore.Text.Trim();
        cfg.RestoreDumpPath = _txtRestoreDump.Text.Trim();

        var dayMap = new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        var checkedDays = new List<string>();
        for (int i = 0; i < _dayChecks.Length; i++)
            if (_dayChecks[i].Checked) checkedDays.Add(dayMap[i]);
    }
}
