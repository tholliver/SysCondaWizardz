namespace SysCondaWizard;

/// <summary>Contract every wizard step must implement.</summary>
public interface IWizardStep
{
    string Title { get; }

    /// <summary>Build and return the panel/control for this step. Called each time the step is shown.</summary>
    Control BuildUI(WizardConfig config);

    /// <summary>Return a validation error string, or null if valid.</summary>
    string? Validate(WizardConfig config);

    /// <summary>Persist UI values back into config (called after Validate succeeds).</summary>
    void Save(WizardConfig config);
}
