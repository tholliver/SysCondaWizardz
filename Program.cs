using System.Security.Principal;

namespace SysCondaWizard;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (ServiceHostRuntime.TryRun(args))
            return;

        ApplicationConfiguration.Initialize();

        if (!IsAdministrator())
        {
            MessageBox.Show(
                "Este wizard requiere permisos de administrador.\n\nEjecuta el programa como Administrador.",
                "sys.conda — Setup Wizard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        Application.Run(new WizardForm());
    }

    static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
