namespace SysCondaWizard;

/// <summary>
/// Step 1 — install root selection.
/// Mode (Install vs Update) is auto-detected from disk when the path changes.
/// No radio buttons — the status badge shows the detected intent clearly.
/// </summary>
public class Step1_Location : IWizardStep
{
    public string Title => "Origen de la aplicación";

    private TextBox _txtRoot = new();
    private Label _lblBadge = new();   // live Install / Update badge
    private Label _lblBadgeHint = new(); // one-line explanation under badge

    // Detected mode — updated live as the user changes the path
    private InstallMode _detectedMode = InstallMode.Install;

    private static bool ExistingInstallAt(string rootDir) =>
        !string.IsNullOrWhiteSpace(rootDir) &&
        (File.Exists(WizardConfig.GetInstallConfigPath(rootDir)) ||
         Directory.Exists(Path.Combine(rootDir, "app")));

    public Control BuildUI(WizardConfig cfg)
    {
        var root = WizardUi.MakeScrollPanel();

        // ── Directory row (first so badge can react to it) ────────────────
        WizardUi.SectionLabel(root, "Directorio de instalación");
        WizardUi.Hint(root, $"Recomendado: {AppProfile.DefaultRootDir}");
        WizardUi.AddRow(root, BuildFolderRow(cfg.RootDirectory, tb => _txtRoot = tb));

        // ── Live status badge ─────────────────────────────────────────────
        _lblBadge = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5f),
            Margin = new Padding(0, 14, 0, 0),
        };
        _lblBadgeHint = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(110, 110, 130),
            Margin = new Padding(0, 2, 0, 0),
        };
        WizardUi.AddRow(root, _lblBadge);
        WizardUi.AddRow(root, _lblBadgeHint);

        // ── Folder layout info box ─────────────────────────────────────────
        WizardUi.InfoBox(root,
            "<raíz>\\bun\\bun.exe   — runtime Bun (accesible por SYSTEM)\n" +
            "<raíz>\\app\\           — proyecto Astro\n" +
            "<raíz>\\logs\\          — logs del servicio\n" +
            "<raíz>\\backups\\       — backups de PostgreSQL");

        // Wire live detection
        _txtRoot.TextChanged += (_, _) => RefreshBadge();
        RefreshBadge();   // set initial state

        return root;
    }

    /// <summary>Re-evaluates the directory and updates badge + internal mode.</summary>
    private void RefreshBadge()
    {
        var path = _txtRoot.Text.Trim();
        bool exists = ExistingInstallAt(path);

        _detectedMode = exists ? InstallMode.Update : InstallMode.Install;

        if (exists)
        {
            _lblBadge.Text = "↑  Actualización detectada";
            _lblBadge.ForeColor = Color.FromArgb(30, 100, 30);
            _lblBadgeHint.Text = "Se reemplazará el código Astro, se recompilarán assets y se reiniciará el servicio.";
        }
        else
        {
            _lblBadge.Text = "✦  Nueva instalación";
            _lblBadge.ForeColor = Color.FromArgb(37, 99, 235);
            _lblBadgeHint.Text = "Se creará la estructura de carpetas, se instalará Bun y se registrará el servicio de Windows.";
        }
    }

    public string? Validate(WizardConfig cfg)
    {
        var rootDir = _txtRoot.Text.Trim();

        if (string.IsNullOrWhiteSpace(rootDir))
            return "Debes especificar el directorio raíz de instalación.";

        if (!EmbeddedSourceExtractor.IsAvailable)
            return "Este ejecutable no contiene el código embebido.\n" +
                   "Utiliza un build oficial del wizard.";

        if (_detectedMode == InstallMode.Update && !ExistingInstallAt(rootDir))
            return $"No se encontró una instalación válida en:\n{rootDir}";

        return null;
    }

    public void Save(WizardConfig cfg)
    {
        cfg.RootDirectory = _txtRoot.Text.Trim();
        cfg.Mode = _detectedMode;
        cfg.AppSource = AppSourceKind.ExistingDirectory;

        if (cfg.IsUpdateMode)
        {
            var existing = WizardConfig.Load(cfg.RootDirectory);
            if (!string.IsNullOrWhiteSpace(existing.RootDirectory))
            {
                cfg.ApplyInstalledSettings(existing);
                cfg.RootDirectory = _txtRoot.Text.Trim();
                cfg.Mode = InstallMode.Update;
                cfg.AppSource = AppSourceKind.ExistingDirectory;
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Panel BuildFolderRow(string value, Action<TextBox> assign)
    {
        var row = new Panel { Height = 34, Margin = new Padding(0, 6, 0, 0) };
        var textBox = WizardUi.TextBox(value);
        textBox.SetBounds(0, 0, 520, 28);
        textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var btn = WizardUi.SmallButton("Examinar", 110);
        btn.Location = new Point(530, 0);
        btn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                SelectedPath = textBox.Text,
                Description = "Selecciona o crea la carpeta raíz de instalación",
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                textBox.Text = dlg.SelectedPath;
        };

        row.Resize += (_, _) =>
        {
            const int gap = 10;
            btn.Left = Math.Max(0, row.ClientSize.Width - btn.Width);
            textBox.Width = Math.Max(220, btn.Left - gap);
        };

        row.Controls.Add(textBox);
        row.Controls.Add(btn);
        assign(textBox);
        return row;
    }
}
