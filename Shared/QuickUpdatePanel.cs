namespace SysCondaWizard;

/// <summary>
/// Shown at the top of Step 1 when an existing install is detected.
/// Lets the user do a one-click update without walking through all 5 steps.
/// </summary>
public sealed class QuickUpdatePanel : Panel
{
    public event Func<Task>? QuickUpdateRequested;

    private readonly WizardConfig _existing;
    private readonly Button _btnUpdate = new();
    private readonly Label _lblStatus = new();
    private bool _running;

    private static readonly Color PanelBg     = Color.FromArgb(235, 245, 255);
    private static readonly Color PanelBorder  = Color.FromArgb(147, 197, 253);
    private static readonly Color AccentColor  = Color.FromArgb(37, 99, 235);
    private static readonly Color TextPrimary  = Color.FromArgb(30, 58, 138);
    private static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);

    public QuickUpdatePanel(WizardConfig existing)
    {
        _existing = existing;

        BackColor   = PanelBg;
        Padding     = new Padding(16, 12, 16, 12);
        Margin      = new Padding(0, 0, 0, 16);
        AutoSize    = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(0, 80);

        BuildContent();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(PanelBorder, 1.5f);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private void BuildContent()
    {
        var icon = new Label
        {
            Text      = "⚡",
            Font      = new Font("Segoe UI", 18f),
            AutoSize  = true,
            Location  = new Point(4, 8),
            ForeColor = AccentColor,
        };

        var title = new Label
        {
            Text      = "Instalación detectada — actualización rápida disponible",
            Font      = new Font("Segoe UI Semibold", 10f),
            AutoSize  = true,
            Location  = new Point(38, 8),
            ForeColor = TextPrimary,
        };

        var manifestPath = _existing.ReleaseManifestPath;
        var manifest     = AppReleaseManifest.TryLoad(manifestPath);
        var releaseInfo  = manifest != null
            ? $"Release actual: {manifest.ReleaseId}  •  {manifest.InstalledAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}"
            : "Sin manifiesto previo";

        var info = new Label
        {
            Text      = $"{releaseInfo}\n" +
                        $"Directorio: {_existing.RootDirectory}  •  Servicio: {_existing.ServiceName}",
            Font      = new Font("Segoe UI", 8.5f),
            AutoSize  = true,
            Location  = new Point(38, 30),
            ForeColor = TextSecondary,
        };

        _lblStatus.AutoSize  = true;
        _lblStatus.Font      = new Font("Segoe UI", 8.5f);
        _lblStatus.ForeColor = TextSecondary;
        _lblStatus.Location  = new Point(38, 54);
        _lblStatus.Text      = "";

        _btnUpdate.Text      = "⚡  Actualizar ahora";
        _btnUpdate.Width     = 160;
        _btnUpdate.Height    = 32;
        _btnUpdate.FlatStyle = FlatStyle.Flat;
        _btnUpdate.BackColor = AccentColor;
        _btnUpdate.ForeColor = Color.White;
        _btnUpdate.Cursor    = Cursors.Hand;
        _btnUpdate.Font      = new Font("Segoe UI Semibold", 9f);
        _btnUpdate.FlatAppearance.BorderColor = AccentColor;
        _btnUpdate.Anchor    = AnchorStyles.Top | AnchorStyles.Right;
        _btnUpdate.Click    += async (_, _) => await OnClickUpdateAsync();

        Controls.Add(icon);
        Controls.Add(title);
        Controls.Add(info);
        Controls.Add(_lblStatus);
        Controls.Add(_btnUpdate);

        // Position button on the right side when panel resizes
        Resize += (_, _) =>
        {
            _btnUpdate.Location = new Point(
                Math.Max(200, Width - _btnUpdate.Width - Padding.Right),
                (Height - _btnUpdate.Height) / 2);
        };
    }

    private async Task OnClickUpdateAsync()
    {
        if (_running) return;
        _running = true;
        _btnUpdate.Enabled = false;
        _btnUpdate.Text    = "Actualizando...";

        try
        {
            SetStatus("Iniciando actualización...");
            if (QuickUpdateRequested != null)
                await QuickUpdateRequested.Invoke();
        }
        finally
        {
            // Leave button disabled — the install step will show results
        }
    }

    public void SetStatus(string text, bool isError = false)
    {
        if (IsDisposed) return;
        Action act = () =>
        {
            _lblStatus.Text      = text;
            _lblStatus.ForeColor = isError
                ? Color.FromArgb(185, 28, 28)
                : TextSecondary;
        };
        if (InvokeRequired) Invoke(act); else act();
    }
}
