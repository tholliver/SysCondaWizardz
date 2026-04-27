namespace SysCondaWizard;

/// <summary>
/// Step 3 — Windows Service configuration.
/// Not shown as a wizard page; constants are applied silently during Save()
/// which is called by WizardForm before advancing to Step 4.
/// </summary>
public class Step3_Service : IWizardStep
{
    // Required by IWizardStep but never rendered.
    public string Title => "Servicio";

    // No UI — returns an invisible placeholder so WizardForm.LoadStep never crashes.
    public Control BuildUI(WizardConfig cfg) => new Panel { Visible = false };

    public string? Validate(WizardConfig cfg) => null;

    public void Save(WizardConfig cfg)
    {
        cfg.InstallAsService = true;
        cfg.ServiceName = AppProfile.ServiceName;
        cfg.ServiceDisplayName = AppProfile.ServiceDisplay;
        cfg.ServiceRestartDelaySeconds = 5;
    }
}
