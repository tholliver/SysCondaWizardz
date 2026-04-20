using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SysCondaWizard;

public class WizardForm : Form
{
    private int _currentStep;
    private WizardConfig _config = new();
    private readonly Step5_Install _installStep;
    private readonly IWizardStep[] _steps;

    private readonly TableLayoutPanel _layout = new();
    private readonly Panel _headerPanel = new();
    private readonly Label _titleLabel = new();
    private readonly Panel _stepPanel = new();
    private readonly Panel _footerPanel = new();
    private readonly Button _btnBack = new();
    private readonly Button _btnNext = new();
    private readonly Button _btnCancel = new();
    private readonly StepsBar _stepsBar = new();

    private static readonly Color Accent      = Color.FromArgb(79, 70, 229);
    private static readonly Color AccentHover = Color.FromArgb(99, 88, 255);

    public WizardForm()
    {
        _installStep = new Step5_Install();
        _installStep.StateChanged += RefreshNavigationState;
        _steps =
        [
            new Step1_Location(),
            new Step2_EnvConfig(),
            new Step3_Service(),
            new Step4_Backup(),
            _installStep,
        ];

        InitializeForm();
        LoadStep(_currentStep);
    }

    private void InitializeForm()
    {
        Text            = AppProfile.WizardTitle;
        Size            = new Size(1000, 720);
        MinimumSize     = new Size(800, 600);
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9.5f);
        BackColor       = Color.White;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = true;

        _layout.Dock = DockStyle.Fill;
        _layout.Margin = new Padding(0);
        _layout.Padding = new Padding(0);
        _layout.ColumnCount = 1;
        _layout.RowCount = 4;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

        _headerPanel.Dock      = DockStyle.Fill;
        _headerPanel.BackColor = Accent;
        _headerPanel.Padding   = new Padding(24, 14, 24, 12);
        _headerPanel.Margin    = new Padding(0);

        _titleLabel.AutoSize  = false;
        _titleLabel.Dock      = DockStyle.Fill;
        _titleLabel.ForeColor = Color.White;
        _titleLabel.Font      = new Font("Segoe UI Semibold", 17f);
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _headerPanel.Controls.Add(_titleLabel);

        _stepsBar.Dock   = DockStyle.Fill;
        _stepsBar.Names  = ["Origen", "Entorno", "Servicio", "Backup", "Instalar"];
        _stepsBar.Margin = new Padding(0);

        _stepPanel.Dock    = DockStyle.Fill;
        _stepPanel.Padding = new Padding(24, 18, 24, 12);
        _stepPanel.Margin  = new Padding(0);

        _footerPanel.Dock      = DockStyle.Fill;
        _footerPanel.BackColor = Color.FromArgb(248, 248, 252);
        _footerPanel.Padding   = new Padding(12, 12, 20, 0);
        _footerPanel.Margin    = new Padding(0);

        var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(220, 220, 230) };
        _footerPanel.Controls.Add(sep);

        StyleButton(_btnCancel, "Cancelar",    false);
        StyleButton(_btnBack,   "← Atrás",     false);
        StyleButton(_btnNext,   "Siguiente →", true);
        _btnNext.Width = 132;

        _btnCancel.Click += (_, _) =>
        {
            if (Confirm("¿Cancelar la instalación?"))
                Close();
        };
        _btnBack.Click += (_, _) => Navigate(-1);
        _btnNext.Click += async (_, _) => await HandleNextAsync();

        var btnRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Right,
            AutoSize      = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            Padding       = new Padding(0),
        };
        btnRow.Controls.Add(_btnCancel);
        btnRow.Controls.Add(_btnBack);
        btnRow.Controls.Add(_btnNext);
        _footerPanel.Controls.Add(btnRow);

        var btnUninstall = new Button
        {
            Text      = "🗑 Desinstalar",
            Width     = 120,
            Height    = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(220, 53, 69),
            ForeColor = Color.White,
            Cursor    = Cursors.Hand,
            Margin    = new Padding(0, 0, 0, 0),
            Font      = new Font("Segoe UI", 9f),
            Dock      = DockStyle.Left,
        };
        btnUninstall.FlatAppearance.BorderColor = Color.FromArgb(220, 53, 69);
        btnUninstall.Click += (_, _) =>
        {
            var cfg = WizardConfig.Load();
            using var dlg = new UninstallForm(cfg);
            dlg.ShowDialog(this);
        };
        _footerPanel.Controls.Add(btnUninstall);

        _layout.Controls.Add(_stepsBar, 0, 0);
        _layout.Controls.Add(_headerPanel, 0, 1);
        _layout.Controls.Add(_stepPanel, 0, 2);
        _layout.Controls.Add(_footerPanel, 0, 3);
        Controls.Add(_layout);
    }

    private static void StyleButton(Button btn, string text, bool primary)
    {
        btn.Text      = text;
        btn.Width     = 110;
        btn.Height    = 34;
        btn.FlatStyle = FlatStyle.Flat;
        btn.Cursor    = Cursors.Hand;
        btn.Margin    = new Padding(6, 0, 0, 0);
        btn.Font      = new Font("Segoe UI", 9.5f);

        if (primary)
        {
            btn.BackColor                  = Accent;
            btn.ForeColor                  = Color.White;
            btn.FlatAppearance.BorderColor = Accent;
            btn.MouseEnter += (_, _) => btn.BackColor = AccentHover;
            btn.MouseLeave += (_, _) => btn.BackColor = Accent;
        }
        else
        {
            btn.BackColor                  = Color.White;
            btn.ForeColor                  = Color.FromArgb(60, 60, 80);
            btn.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 215);
        }
    }

    private async Task HandleNextAsync()
    {
        var err = _steps[_currentStep].Validate(_config);
        if (err != null) { ShowError(err); return; }

        _steps[_currentStep].Save(_config);

        if (_currentStep < _steps.Length - 1)
        {
            _currentStep++;
            LoadStep(_currentStep);
            return;
        }

        if (_installStep.HasCompleted) { Close(); return; }

        await _installStep.RunInstallAsync();
    }

    private void Navigate(int delta)
    {
        if (_installStep.IsRunning) return;
        _currentStep = Math.Clamp(_currentStep + delta, 0, _steps.Length - 1);
        LoadStep(_currentStep);
    }

    private void LoadStep(int index)
    {
        _stepPanel.Controls.Clear();
        var step = _steps[index];
        var ctl  = step.BuildUI(_config);
        ctl.Dock = DockStyle.Fill;
        _stepPanel.Controls.Add(ctl);
        _titleLabel.Text = step.Title;
        _stepsBar.Active = index;
        _stepsBar.Invalidate();
        RefreshNavigationState();
    }

    private void RefreshNavigationState()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(RefreshNavigationState); return; }

        var isLast = _currentStep == _steps.Length - 1;
        _btnBack.Enabled   = _currentStep > 0 && !_installStep.IsRunning;
        _btnCancel.Enabled = !_installStep.IsRunning;

        if (!isLast) { _btnNext.Enabled = true; _btnNext.Text = "Siguiente →"; return; }

        _btnNext.Enabled = !_installStep.IsRunning || _installStep.HasCompleted;
        _btnNext.Text    = _installStep.HasCompleted ? "Cerrar"
                         : _installStep.IsRunning    ? "Instalando..."
                         : _installStep.HasAttempted ? "Reintentar instalación"
                                                     : "Iniciar instalación";
    }

    private static void ShowError(string msg) =>
        MessageBox.Show(msg, "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static bool Confirm(string msg) =>
        MessageBox.Show(msg, $"{AppProfile.AppName} Wizard",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}

internal class StepsBar : Control
{
    private string[] _names = Array.Empty<string>();
    private int _active;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public string[] Names
    {
        get => _names;
        set { _names = value ?? Array.Empty<string>(); Invalidate(); }
    }

    [DefaultValue(0)]
    public int Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    private static readonly Color Done    = Color.FromArgb(79, 70, 229);
    private static readonly Color Current = Color.FromArgb(48, 42, 180);
    private static readonly Color Idle    = Color.FromArgb(205, 208, 224);
    private static readonly Color BgClr   = Color.FromArgb(247, 247, 252);

    public StepsBar() { DoubleBuffered = true; BackColor = BgClr; ResizeRedraw = true; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Names.Length == 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BgClr);

        var rect         = ClientRectangle;
        var lineY        = 16f;
        var startX       = 28f;
        var endX         = Math.Max(startX, rect.Width - 28f);
        var segmentWidth = Names.Length == 1 ? 0 : (endX - startX) / (Names.Length - 1);

        using (var p = new Pen(Idle, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(p, startX, lineY, endX, lineY);

        var progressX = startX + Math.Max(0, Math.Min(Active, Names.Length - 1)) * segmentWidth;
        using (var p = new Pen(Done, 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(p, startX, lineY, progressX, lineY);

        using var activeFont = new Font("Segoe UI Semibold", 8.5f);
        using var idleFont   = new Font("Segoe UI", 8.5f);
        using var sf         = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

        for (var i = 0; i < Names.Length; i++)
        {
            var x = startX + i * segmentWidth;
            using (var b = new SolidBrush(i <= Active ? Done : Idle))
                g.FillEllipse(b, x - 5, lineY - 5, 10, 10);

            var lr = new RectangleF(x - 70, lineY + 10, 140, rect.Height - 20);
            using var lb = new SolidBrush(i == Active ? Current : i < Active ? Done : Color.FromArgb(130, 135, 155));
            g.DrawString(Names[i], i == Active ? activeFont : idleFont, lb, lr, sf);
        }
    }
}





