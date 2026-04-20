using System.Linq;
using System.Net.NetworkInformation;

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
    private Button _btnTestPg = new();
    private Label _lblPgStatus = new();

    // Keep cfg ref so RunPgTestAsync can read PgDumpPath without re-loading
    private WizardConfig? _cfg;

    public Control BuildUI(WizardConfig cfg)
    {
        _cfg = cfg;
        var root = WizardUi.MakeScrollPanel();

        WizardUi.SectionLabel(root, "Base de datos PostgreSQL");
        _txtDbHost = AddField(root, "Host", cfg.DbHost, "localhost");
        _txtDbPort = AddField(root, "Puerto", cfg.DbPort, "5432");
        _txtDbName = AddField(root, "Nombre BD", cfg.DbName, "conda_db");
        _txtDbUser = AddField(root, "Usuario", cfg.DbUser, "postgres");
        _txtDbPass = AddPasswordField(root, "Contraseña", cfg.DbPassword);

        // ── PG connection test row ────────────────────────────────────────────
        var testRow = new Panel { Height = 30, Margin = new Padding(0, 6, 0, 0) };

        _btnTestPg = WizardUi.SmallButton("🔌 Probar conexión PG", 160);
        _btnTestPg.Location = new Point(168, 1);
        _btnTestPg.Click += async (_, _) => await RunPgTestAsync();

        _lblPgStatus = new Label
        {
            Text = "Sin probar",
            Location = new Point(336, 6),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9f),
        };

        testRow.Controls.Add(_btnTestPg);
        testRow.Controls.Add(_lblPgStatus);
        WizardUi.AddRow(root, testRow);
        // ─────────────────────────────────────────────────────────────────────

        WizardUi.Separator(root);
        WizardUi.SectionLabel(root, "Aplicación");
        _txtPort = AddField(root, "Puerto app", cfg.AppPort, AppProfile.DefaultAppPort.ToString());
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

        // Reset PG status whenever any credential field changes
        void Hook(TextBox tb) => tb.TextChanged += (_, _) =>
        {
            UpdatePreview();
            _lblPgStatus.Text = "Sin probar";
            _lblPgStatus.ForeColor = Color.Gray;
        };
        Hook(_txtDbHost);
        Hook(_txtDbPort);
        Hook(_txtDbName);
        Hook(_txtDbUser);
        Hook(_txtDbPass);
        Hook(_txtPort);

        return root;
    }

    // ── PG test ───────────────────────────────────────────────────────────────

    private async Task RunPgTestAsync()
    {
        _btnTestPg.Enabled = false;
        _lblPgStatus.Text = "Probando...";
        _lblPgStatus.ForeColor = Color.Gray;

        // Use the already-located pg_dump path from cfg — no re-resolution needed
        var pgDumpPath = _cfg?.PgDumpPath ?? "pg_dump.exe";
        var host = V(_txtDbHost, "localhost");
        var port = int.TryParse(_txtDbPort.Text, out var p) ? p : 5432;
        var user = V(_txtDbUser, "postgres");
        var password = _txtDbPass.Text;

        // Probe the 'postgres' system DB — always exists on any PG server.
        // Proves server is reachable + credentials valid without requiring the
        // app DB to exist yet (Drizzle push handles DB/table creation at install).
        var (ok, message) = await Task.Run(() =>
            PgProbe.Test(pgDumpPath, host, port, user, password, "postgres"));

        _lblPgStatus.Text = ok ? "✓ Conexión exitosa" : $"✗ {message}";
        _lblPgStatus.ForeColor = ok ? Color.FromArgb(30, 140, 50) : Color.FromArgb(200, 40, 40);
        _btnTestPg.Enabled = true;
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

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

    // ── Validate + Save ───────────────────────────────────────────────────────

    public string? Validate(WizardConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(_txtDbName.Text))
            return "El nombre de la base de datos es requerido.";
        if (string.IsNullOrWhiteSpace(_txtDbPass.Text))
            return "La contraseña de PostgreSQL es requerida.";
        if (!int.TryParse(_txtDbPort.Text, out _))
            return "El puerto de la BD debe ser numérico.";
        if (!int.TryParse(_txtPort.Text, out int appPort))
            return "El puerto de la app debe ser numérico.";

        if (appPort.ToString() != cfg.AppPort && IsPortInUse(appPort))
            return $"El puerto {appPort} ya está en uso. Prueba el {appPort + 1}.";

        if (_lblPgStatus.ForeColor != Color.FromArgb(30, 140, 50))
            return "Verifica la conexión a PostgreSQL antes de continuar (botón 🔌 Probar).";

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

    private static bool IsPortInUse(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return listeners.Any(ep => ep.Port == port);
    }
}

internal static class SecretGenerator
{
    public static string Create() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
               .ToLowerInvariant();
}
