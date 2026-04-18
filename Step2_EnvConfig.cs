namespace SysCondaWizard;

/// <summary>Step 2 — fill .env variables.</summary>
public class Step2_EnvConfig : IWizardStep
{
    public string Title => "Configuración del entorno (.env)";

    private TextBox _txtDbHost = new(), _txtDbPort = new(), _txtDbName = new(),
                    _txtDbUser = new(), _txtDbPass = new();
    private TextBox _txtPort = new(), _txtSecret = new();
    private TextBox _txtRateLimitWindow = new(), _txtMaxAttempts = new();
    private Label _lblPreview = new();

    public Control BuildUI(WizardConfig cfg)
    {
        var root = WizardUi.MakeScrollPanel();

        WizardUi.SectionLabel(root, "Base de datos PostgreSQL");
        _txtDbHost = AddField(root, "Host", cfg.DbHost, "localhost");
        _txtDbPort = AddField(root, "Puerto", cfg.DbPort, "5432");
        _txtDbName = AddField(root, "Nombre BD", cfg.DbName, "conda_db");
        _txtDbUser = AddField(root, "Usuario", cfg.DbUser, "postgres");
        _txtDbPass = AddPasswordField(root, "Contraseña", cfg.DbPassword);

        WizardUi.Separator(root);
        WizardUi.SectionLabel(root, "Aplicación");
        _txtPort = AddField(root, "Puerto app", cfg.AppPort, "4321");
        _txtSecret = AddField(root, "BETTER_AUTH_SECRET", cfg.BetterAuthSecret, "se reutiliza o se genera si queda vacío");

        var secretRow = new Panel { Height = 28, Margin = new Padding(0, 3, 0, 0) };
        var btnGenerate = WizardUi.SmallButton("Generar", 90);
        btnGenerate.Location = new Point(168, 1);
        btnGenerate.Click += (_, _) => _txtSecret.Text = SecretGenerator.Create();
        var hint = new Label
        {
            Text = "Si queda vacío, el instalador intenta reutilizar el actual o crear uno nuevo.",
            Location = new Point(266, 6),
            AutoSize = true,
            ForeColor = Color.Gray,
        };
        secretRow.Controls.Add(btnGenerate);
        secretRow.Controls.Add(hint);
        WizardUi.AddRow(root, secretRow);

        _txtRateLimitWindow = AddField(root, "RATE_LIMIT_WINDOW", cfg.RateLimitWindow, "900");
        _txtMaxAttempts = AddField(root, "MAX_ATTEMPTS", cfg.MaxAttempts, "5");

        WizardUi.Separator(root);
        WizardUi.SectionLabel(root, "Vista previa DATABASE_URL");

        _lblPreview = new Label
        {
            AutoSize = false,
            Height = 22,
            Font = new Font("Consolas", 8.5f),
            ForeColor = Color.FromArgb(60, 130, 60),
        };

        UpdatePreview();
        WizardUi.AddRow(root, _lblPreview);

        void Hook(TextBox tb) => tb.TextChanged += (_, _) => UpdatePreview();
        Hook(_txtDbHost);
        Hook(_txtDbPort);
        Hook(_txtDbName);
        Hook(_txtDbUser);
        Hook(_txtDbPass);
        Hook(_txtPort);

        return root;
    }

    private void UpdatePreview()
    {
        _lblPreview.Text =
            $"postgresql://{V(_txtDbUser, "user")}:{V(_txtDbPass, "pass")}@" +
            $"{V(_txtDbHost, "localhost")}:{V(_txtDbPort, "5432")}/" +
            $"{V(_txtDbName, "db")}";
        _lblPreview.Width = 600;
    }

    private static string V(TextBox tb, string fallback) =>
        string.IsNullOrWhiteSpace(tb.Text) ? fallback : tb.Text.Trim();

    private static TextBox AddField(Panel root, string label, string value, string placeholder = "")
    {
        var row = new Panel { Height = 28, Margin = new Padding(0, 3, 0, 0) };
        var lbl = new Label
        {
            Text = label + ":",
            Width = 160,
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(0, 4)
        };
        var tb = WizardUi.TextBox(value, placeholder);
        tb.Width = 340;
        tb.Location = new Point(168, 2);
        row.Controls.Add(lbl);
        row.Controls.Add(tb);
        WizardUi.AddRow(root, row);
        return tb;
    }

    private static TextBox AddPasswordField(Panel root, string label, string value)
    {
        var row = new Panel { Height = 28, Margin = new Padding(0, 3, 0, 0) };
        var lbl = new Label
        {
            Text = label + ":",
            Width = 160,
            TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(0, 4)
        };
        var tb = WizardUi.TextBox(value);
        tb.Width = 340;
        tb.Location = new Point(168, 2);
        tb.UseSystemPasswordChar = true;
        var chkShow = new CheckBox
        {
            Text = "Ver",
            Location = new Point(516, 6),
            AutoSize = true
        };
        chkShow.CheckedChanged += (_, _) => tb.UseSystemPasswordChar = !chkShow.Checked;
        row.Controls.Add(lbl);
        row.Controls.Add(tb);
        row.Controls.Add(chkShow);
        WizardUi.AddRow(root, row);
        return tb;
    }

    public string? Validate(WizardConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(_txtDbName.Text))
            return "El nombre de la base de datos es requerido.";
        if (string.IsNullOrWhiteSpace(_txtDbPass.Text))
            return "La contraseña de PostgreSQL es requerida.";
        if (!int.TryParse(_txtDbPort.Text, out _))
            return "El puerto de la BD debe ser numérico.";
        if (!int.TryParse(_txtPort.Text, out _))
            return "El puerto de la app debe ser numérico.";
        return null;
    }

    public void Save(WizardConfig cfg)
    {
        cfg.DbHost = _txtDbHost.Text.Trim();
        cfg.DbPort = _txtDbPort.Text.Trim();
        cfg.DbName = _txtDbName.Text.Trim();
        cfg.DbUser = _txtDbUser.Text.Trim();
        cfg.DbPassword = _txtDbPass.Text;
        cfg.AppPort = _txtPort.Text.Trim();
        cfg.AppUrl = $"http://localhost:{_txtPort.Text.Trim()}/";
        cfg.BetterAuthSecret = _txtSecret.Text.Trim();
        cfg.RateLimitWindow = _txtRateLimitWindow.Text.Trim();
        cfg.MaxAttempts = _txtMaxAttempts.Text.Trim();
    }
}

internal static class SecretGenerator
{
    public static string Create()
    {
        return Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }
}
