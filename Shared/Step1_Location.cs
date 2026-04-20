namespace SysCondaWizard;

/// <summary>Step 1 — select install root and app source.</summary>
public class Step1_Location : IWizardStep
{
    public string Title => "Origen de la aplicación";

    private TextBox _txtRoot = new();
    private TextBox _txtGitUrl = new();
    private TextBox _txtZipPath = new();
    private RadioButton _rbInstall = new();
    private RadioButton _rbUpdate = new();
    private RadioButton _rbEmbed = new();
    private RadioButton _rbZip = new();
    private RadioButton _rbGit = new();
    private Panel _zipPanel = new();
    private Panel _gitPanel = new();

    public Control BuildUI(WizardConfig cfg)
    {
        var root = WizardUi.MakeScrollPanel();

        WizardUi.SectionLabel(root, "Modo de despliegue");
        WizardUi.Hint(root,
            "Instalación: crea una nueva carpeta release.\n" +
            "Actualización: conserva la configuración actual, vuelve a desplegar los archivos Astro y recompila.");

        _rbInstall = CreateSourceRadio("Nueva instalación", cfg.Mode == InstallMode.Install);
        _rbUpdate = CreateSourceRadio("Actualizar instalación existente", cfg.Mode == InstallMode.Update);
        WizardUi.AddRow(root, _rbInstall);
        WizardUi.AddRow(root, _rbUpdate);

        WizardUi.InfoBox(root,
            "Update profesional recomendado:\n" +
            "1. Parar el servicio.\n" +
            "2. Verificar integridad del release actual.\n" +
            "3. Reemplazar código Astro con un release nuevo.\n" +
            "4. Ejecutar bun install, migraciones y build.\n" +
            "5. Escribir un manifiesto SHA-256 del release y volver a iniciar el servicio.");

        WizardUi.SectionLabel(root, "Origen del proyecto");
        WizardUi.Hint(root,
            $"El wizard incluye el código de {AppProfile.AppName} dentro del propio ejecutable.\n" +
            "Puedes instalarlo directamente desde ahí, o usar un ZIP/repositorio Git externo.");

        _rbEmbed = CreateSourceRadio("Usar fuente embebida (recomendado)", cfg.AppSource == AppSourceKind.ExistingDirectory);
        _rbZip = CreateSourceRadio("Extraer desde archivo ZIP externo", cfg.AppSource == AppSourceKind.ZipArchive);
        _rbGit = CreateSourceRadio("Clonar repositorio Git", cfg.AppSource == AppSourceKind.GitRepository);

        WizardUi.AddRow(root, _rbEmbed);
        WizardUi.AddRow(root, _rbZip);
        WizardUi.AddRow(root, _rbGit);

        _zipPanel = new Panel { Height = 88, Margin = new Padding(0, 8, 0, 0), Visible = cfg.AppSource == AppSourceKind.ZipArchive };
        WizardUi.SectionLabel(_zipPanel, "Archivo ZIP");
        WizardUi.Hint(_zipPanel, "El ZIP puede venir con la app en la raíz o dentro de una carpeta principal.");
        WizardUi.AddRow(_zipPanel, BuildFileRow(cfg.SourceZipPath, "*.zip", tb => _txtZipPath = tb));
        WizardUi.AddRow(root, _zipPanel);

        _gitPanel = new Panel { Height = 88, Margin = new Padding(0, 8, 0, 0), Visible = cfg.AppSource == AppSourceKind.GitRepository };
        WizardUi.SectionLabel(_gitPanel, "Repositorio Git");
        WizardUi.Hint(_gitPanel, "Clona el proyecto en la carpeta destino y construye con Bun.");
        _txtGitUrl = WizardUi.TextBox(cfg.GitRepoUrl, $"https://github.com/usuario/{AppProfile.ServiceName}.git");
        WizardUi.AddRow(_gitPanel, _txtGitUrl);
        WizardUi.AddRow(root, _gitPanel);

        WizardUi.SectionLabel(root, "Directorio raíz de instalación");
        WizardUi.Hint(root,
            "Todo se instala bajo esta carpeta:\n" +
            "  <raíz>\\bun\\bun.exe   — runtime (visible para el servicio SYSTEM)\n" +
            "  <raíz>\\app\\          — proyecto Astro\n" +
            "  <raíz>\\logs\\         — logs del servicio\n" +
            "  <raíz>\\backups\\      — backups de PostgreSQL\n\n" +
            $"Recomendado: {AppProfile.DefaultRootDir}");

        WizardUi.AddRow(root, BuildFolderRow(cfg.RootDirectory, path =>
        {
            using var dlg = new FolderBrowserDialog
            {
                SelectedPath = _txtRoot.Text,
                Description = "Selecciona o crea la carpeta raíz de instalación",
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                path.Text = dlg.SelectedPath;
        }, tb => _txtRoot = tb));

        _rbEmbed.CheckedChanged += (_, _) => RefreshSourcePanels();
        _rbZip.CheckedChanged += (_, _) => RefreshSourcePanels();
        _rbGit.CheckedChanged += (_, _) => RefreshSourcePanels();
        RefreshSourcePanels();

        WizardUi.InfoBox(root,
            "Flujo de despliegue:\n" +
            "1. Instalar bun en <raíz>\\bun\\  (accesible por SYSTEM).\n" +
            "2. Sincronizar proyecto en <raíz>\\app\\  (sin node_modules ni dist).\n" +
            "3. Escribir .env.\n" +
            "4. bun install + drizzle push + bun run build.\n" +
            "5. Registrar/verificar release y servicio de Windows.");

        return root;
    }

    public string? Validate(WizardConfig cfg)
    {
        var rootDir = _txtRoot.Text.Trim();
        if (string.IsNullOrWhiteSpace(rootDir))
            return "Debes especificar el directorio raíz de instalación.";

        if (_rbZip.Checked)
        {
            var zipPath = _txtZipPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(zipPath))
                return "Selecciona el archivo ZIP del proyecto.";
            if (!File.Exists(zipPath))
                return "No se encontró el archivo ZIP seleccionado.";
        }

        if (_rbGit.Checked && string.IsNullOrWhiteSpace(_txtGitUrl.Text.Trim()))
            return "Ingresa la URL del repositorio Git.";

        if (_rbUpdate.Checked)
        {
            var hasConfig = File.Exists(WizardConfig.GetInstallConfigPath(rootDir));
            var hasApp = Directory.Exists(Path.Combine(rootDir, "app"));
            if (!hasConfig && !hasApp)
                return "Para actualizar, la carpeta raíz debe contener una instalación existente o su wizard-config.json.";
        }

        if (_rbEmbed.Checked && !EmbeddedSourceExtractor.IsAvailable)
            return "Este ejecutable no contiene una fuente embebida.\n" +
                   "Recompila el wizard con SysCondaSourceDir configurado en el .csproj, o usa ZIP/Git.";

        return null;
    }

    public void Save(WizardConfig cfg)
    {
        var rootDir = _txtRoot.Text.Trim();
        cfg.RootDirectory = rootDir;
        cfg.Mode = _rbUpdate.Checked ? InstallMode.Update : InstallMode.Install;
        cfg.SourceZipPath = _txtZipPath.Text.Trim();
        cfg.GitRepoUrl = _txtGitUrl.Text.Trim();
        cfg.AppSource = _rbZip.Checked
            ? AppSourceKind.ZipArchive
            : _rbGit.Checked
                ? AppSourceKind.GitRepository
                : AppSourceKind.ExistingDirectory;

        if (cfg.IsUpdateMode)
        {
            var existing = WizardConfig.Load(rootDir);
            if (!string.IsNullOrWhiteSpace(existing.RootDirectory))
            {
                cfg.ApplyInstalledSettings(existing);
                cfg.RootDirectory = rootDir;
                cfg.Mode = InstallMode.Update;
                cfg.SourceZipPath = _txtZipPath.Text.Trim();
                cfg.GitRepoUrl = _txtGitUrl.Text.Trim();
                cfg.AppSource = _rbZip.Checked
                    ? AppSourceKind.ZipArchive
                    : _rbGit.Checked
                        ? AppSourceKind.GitRepository
                        : AppSourceKind.ExistingDirectory;
            }
        }
    }

    private static RadioButton CreateSourceRadio(string text, bool isChecked) => new()
    {
        Text = text,
        Checked = isChecked,
        AutoSize = true,
        Margin = new Padding(0, 10, 0, 0),
    };

    private Panel BuildFolderRow(string value, Action<TextBox> browse, Action<TextBox> assign)
    {
        var row = new Panel { Height = 34, Margin = new Padding(0, 4, 0, 0) };
        var textBox = WizardUi.TextBox(value);
        textBox.SetBounds(0, 0, 520, 28);
        textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var btn = WizardUi.SmallButton("Examinar", 110);
        btn.Location = new Point(530, 0);
        btn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btn.Click += (_, _) => browse(textBox);

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

    private Panel BuildFileRow(string value, string filter, Action<TextBox> assign)
    {
        var row = new Panel { Height = 34, Margin = new Padding(0, 4, 0, 0) };
        var textBox = WizardUi.TextBox(value);
        textBox.SetBounds(0, 0, 520, 28);
        textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var btn = WizardUi.SmallButton("Buscar ZIP", 110);
        btn.Location = new Point(530, 0);
        btn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Filter = $"Archivos ({filter})|{filter}|Todos|*.*",
                FileName = textBox.Text,
                CheckFileExists = true,
                Title = "Selecciona el ZIP del proyecto",
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                textBox.Text = dlg.FileName;
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

    private void RefreshSourcePanels()
    {
        _zipPanel.Visible = _rbZip.Checked;
        _gitPanel.Visible = _rbGit.Checked;
    }
}
