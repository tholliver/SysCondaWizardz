namespace SysCondaWizard;

/// <summary>Step 3 — install app as a native Windows Service.</summary>
public class Step3_Service : IWizardStep
{
    public string Title => "Servicio de Windows";

    private CheckBox _chkInstall = new();
    private TextBox _txtName = new();
    private NumericUpDown _numRestartDelay = new();
    private Panel _detailPanel = new();

    public Control BuildUI(WizardConfig cfg)
    {
        var root = WizardUi.MakeScrollPanel();

        WizardUi.SectionLabel(root, "Servicio de Windows");
        WizardUi.Hint(root,
            "El wizard instalará un servicio nativo de Windows hecho en .NET para mantener\n" +
            "la app Astro + Bun viva, reiniciarla si cae y arrancarla automáticamente al encender.");

        _chkInstall = new CheckBox
        {
            Text = $"Instalar {AppProfile.AppName} como servicio de Windows",   // ← AppProfile
            Checked = cfg.InstallAsService,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        };
        WizardUi.AddRow(root, _chkInstall);

        _detailPanel = new Panel { Visible = cfg.InstallAsService };
        AddField(_detailPanel, "Nombre del servicio", ref _txtName, cfg.ServiceName);

        var row = new Panel { Height = 28, Margin = new Padding(0, 3, 0, 0) };
        var lbl = new Label
        {
            Text = "Reintento tras fallo (s):",
            Width = 160,
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(0, 4)
        };
        _numRestartDelay = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 120,
            Value = cfg.ServiceRestartDelaySeconds,
            Width = 80,
            Location = new Point(168, 2)
        };
        var hint = new Label
        {
            Text = "reinicio del proceso Bun tras salida inesperada",
            Location = new Point(256, 6),
            AutoSize = true,
            ForeColor = Color.Gray
        };
        row.Controls.Add(lbl);
        row.Controls.Add(_numRestartDelay);
        row.Controls.Add(hint);
        _detailPanel.Controls.Add(row);
        _detailPanel.Height = 80;

        WizardUi.AddRow(root, _detailPanel);
        _chkInstall.CheckedChanged += (_, _) => _detailPanel.Visible = _chkInstall.Checked;

        WizardUi.InfoBox(root,
            "El wizard copiará un host de servicio .NET dentro de la carpeta de la app,\n" +
            "lo registrará con sc.exe y configurará recuperación automática del servicio.\n\n" +
            "El proceso lanzado será:\n" +
            $"bun run dist/server/entry.mjs   (NODE_ENV=production, HOST=0.0.0.0, PORT={cfg.AppPort})");

        return root;
    }

    private static void AddField(Panel root, string label, ref TextBox tb, string value)
    {
        var row = new Panel { Height = 28, Margin = new Padding(0, 3, 0, 0) };
        var lbl = new Label { Text = label + ":", Width = 160, TextAlign = ContentAlignment.MiddleRight, Location = new Point(0, 4) };
        tb = WizardUi.TextBox(value);
        tb.Width = 340;
        tb.Location = new Point(168, 2);
        row.Controls.Add(lbl);
        row.Controls.Add(tb);
        WizardUi.AddRow(root, row);
    }

    public string? Validate(WizardConfig cfg)
    {
        if (!_chkInstall.Checked) return null;
        if (string.IsNullOrWhiteSpace(_txtName.Text)) return "El nombre del servicio es requerido.";
        return null;
    }

    public void Save(WizardConfig cfg)
    {
        cfg.InstallAsService = _chkInstall.Checked;
        cfg.ServiceName = _txtName.Text.Trim();
        cfg.ServiceDisplayName = $"{AppProfile.AppName} ({cfg.ServiceName})";   // ← AppProfile
        cfg.ServiceRestartDelaySeconds = (int)_numRestartDelay.Value;
    }
}
