namespace SysCondaWizard;

public class Step3_Service : IWizardStep
{
    public string Title => "Servicio";

    private CheckBox _chkExposeToNetwork = new();

    public Control BuildUI(WizardConfig cfg)
    {
        var root = WizardUi.MakeScrollPanel();

        WizardUi.SectionLabel(root, "Servicio de Windows");
        WizardUi.Hint(root, "La aplicación se instalará como servicio de Windows con inicio automático.");

        _chkExposeToNetwork = new CheckBox
        {
            Text = $"Exponer la app a la red local en el puerto {cfg.AppPort}",
            Checked = cfg.ExposeAppToNetwork,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4),
        };
        WizardUi.AddRow(root, _chkExposeToNetwork);

        WizardUi.Hint(root,
            "Opcional. Si está activo, el servicio escucha en todas las interfaces y el instalador crea la regla de Firewall.\n" +
            "Si está desactivado, la app queda disponible solo desde este equipo.");

        return root;
    }

    public string? Validate(WizardConfig cfg) => null;

    public void Save(WizardConfig cfg)
    {
        cfg.InstallAsService = true;
        cfg.ServiceName = AppProfile.ServiceName;
        cfg.ServiceDisplayName = AppProfile.ServiceDisplay;
        cfg.ServiceRestartDelaySeconds = 5;
        cfg.ExposeAppToNetwork = _chkExposeToNetwork.Checked;
    }
}
