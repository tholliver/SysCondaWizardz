namespace SysCondaWizard;

/// <summary>
/// Per-app identity for AdminConda.
/// Edit these constants and the two PropertyGroup lines in the .csproj, then rebuild.
/// </summary>
public static class AppProfile
{
    public const string AppName = "AdminConda";
    public const string WizardTitle = "AdminConda — Setup Wizard";
    //
    public const string ServiceName = "adminconda";
    public const string ServiceDisplay = "AdminConda Service";
    //
    public const string DbName = "adminconda_db";
    public const string DbUser = "postgres";

    public const string TaskName = "adminconda_pg_backup";

    public const string EmbedPrefix = "adminconda-source/";

    public const string DefaultRootDir = @"C:\adminconda";
    public const int DefaultAppPort = 4322;
}
