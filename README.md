# SysCondaWizard — Multi-App Installer Solution

## Structure

```
WizardSolution/
├── Shared/                  ← All C# logic — never edit these for new apps
│   ├── AppProfile.cs        ← (DO NOT put AppProfile here — it lives per-project)
│   ├── WizardConfig.cs
│   ├── WizardForm.cs
│   ├── Step1_Location.cs
│   ├── Step2_EnvConfig.cs
│   ├── Step3_Service.cs
│   ├── Step4_Backup.cs
│   ├── Step5_Install.cs
│   ├── EmbeddedSourceExtractor.cs
│   ├── ServiceHostRuntime.cs
│   ├── BackupScriptGenerator.cs
│   ├── PostgresBinaryLocator.cs
│   ├── IWizardStep.cs
│   ├── Program.cs
│   ├── WizardUI.cs
│   └── app.manifest
│
├── Wizard.SysConda/         ← sys.conda installer
│   ├── AppProfile.cs        ← sys.conda identity
│   └── Wizard.SysConda.csproj
│
├── Wizard.AppTwo/           ← Template for your next app
│   ├── AppProfile.cs        ← AppTwo identity (edit this)
│   └── Wizard.AppTwo.csproj
│
└── WizardSolution.sln

```

## Adding a new app

1. Copy `Wizard.AppTwo/` → `Wizard.YourApp/`
2. Edit `AppProfile.cs` — change the 7 constants
3. Edit `Wizard.YourApp.csproj` — set `SysCondaSourceDir` and `AppEmbedPrefix`
   - `AppEmbedPrefix` must match `EmbedPrefix` in `AppProfile.cs` (without trailing slash)
4. Add the new project to `WizardSolution.sln`
5. Build:
   ```
   dotnet publish Wizard.YourApp/Wizard.YourApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
   ```

## Building

```bash
# Build sys.conda installer
dotnet publish Wizard.SysConda/Wizard.SysConda.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Build AppTwo installer
dotnet publish Wizard.AppTwo/Wizard.AppTwo.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
