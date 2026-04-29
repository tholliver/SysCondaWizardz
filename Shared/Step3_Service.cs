namespace SysCondaWizard;

public class Step3_Service : IWizardStep
{
    public string Title => "Servicio";

    private CheckBox _chkFirewall = new();

    public Control BuildUI(WizardConfig cfg)
    {
        var root = WizardUi.MakeScrollPanel();

        WizardUi.SectionLabel(root, "Servicio de Windows");
        WizardUi.Hint(root, "La aplicación se instalará como servicio de Windows con inicio automático.");

        _chkFirewall = new CheckBox
        {
            Text = $"Abrir puerto {cfg.AppPort} en el Firewall de Windows (acceso desde red local)",
            Checked = cfg.OpenFirewallPort,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        };
        WizardUi.AddRow(root, _chkFirewall);

        WizardUi.Hint(root,
            "Opcional. Permite acceder a la app desde otros dispositivos en la misma red.\n" +
            "Solo abre el puerto de la app — no compromete la seguridad del equipo.");

        return root;
    }

    public string? Validate(WizardConfig cfg) => null;

    public void Save(WizardConfig cfg)
    {
        cfg.InstallAsService = true;
        cfg.ServiceName = AppProfile.ServiceName;
        cfg.ServiceDisplayName = AppProfile.ServiceDisplay;
        cfg.ServiceRestartDelaySeconds = 5;
        cfg.OpenFirewallPort = _chkFirewall.Checked; // 👈 save checkbox state
    }
}
