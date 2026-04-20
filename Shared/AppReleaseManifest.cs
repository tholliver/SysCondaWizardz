using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace SysCondaWizard;

internal sealed class AppReleaseManifest
{
    public string AppName { get; set; } = AppProfile.AppName;
    public string ReleaseId { get; set; } = "";
    public string WizardVersion { get; set; } = "";
    public string WizardExecutableSha256 { get; set; } = "";
    public string SourceDescription { get; set; } = "";
    public string InstallMode { get; set; } = "";
    public DateTimeOffset InstalledAtUtc { get; set; }
    public List<ManifestFileEntry> AppFiles { get; set; } = new();
    public List<ManifestFileEntry> RuntimeFiles { get; set; } = new();

    public static AppReleaseManifest Capture(WizardConfig cfg, string sourceDescription)
    {
        var exePath = Application.ExecutablePath;
        var exeVersion = FileVersionInfo.GetVersionInfo(exePath).ProductVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "1.0.0";

        var wizardSha = ComputeSha256(exePath);
        return new AppReleaseManifest
        {
            AppName = AppProfile.AppName,
            ReleaseId = $"{exeVersion}+{wizardSha[..12]}",
            WizardVersion = exeVersion,
            WizardExecutableSha256 = wizardSha,
            SourceDescription = sourceDescription,
            InstallMode = cfg.IsUpdateMode ? "update" : "install",
            InstalledAtUtc = DateTimeOffset.UtcNow,
            AppFiles = CaptureDirectory(cfg.AppDirectory, GetAppExcludedNames()),
            RuntimeFiles = Directory.Exists(cfg.ServiceRuntimeDirectory)
                ? CaptureDirectory(cfg.ServiceRuntimeDirectory, Array.Empty<string>())
                : new List<ManifestFileEntry>(),
        };
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static AppReleaseManifest? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AppReleaseManifest>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public AppReleaseVerificationResult Verify(WizardConfig cfg)
    {
        var issues = new List<string>();
        VerifyDirectory(cfg.AppDirectory, AppFiles, GetAppExcludedNames(), "app", issues);
        if (RuntimeFiles.Count > 0)
            VerifyDirectory(cfg.ServiceRuntimeDirectory, RuntimeFiles, Array.Empty<string>(), "runtime", issues);

        return new AppReleaseVerificationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
        };
    }

    public static bool TryVerify(WizardConfig cfg, out AppReleaseManifest manifest, out AppReleaseVerificationResult result)
    {
        manifest = TryLoad(cfg.ReleaseManifestPath) ?? new AppReleaseManifest();
        if (string.IsNullOrWhiteSpace(manifest.ReleaseId))
        {
            result = new AppReleaseVerificationResult
            {
                IsValid = false,
                Issues = [$"No se encontró un manifiesto de release válido en {cfg.ReleaseManifestPath}."]
            };
            return false;
        }

        result = manifest.Verify(cfg);
        return true;
    }

    private static List<ManifestFileEntry> CaptureDirectory(string root, IReadOnlyCollection<string> excludedNames)
    {
        var files = new List<ManifestFileEntry>();
        if (!Directory.Exists(root))
            return files;

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(root, file);
            if (ShouldExclude(rel, excludedNames))
                continue;

            files.Add(new ManifestFileEntry
            {
                Path = rel.Replace(Path.DirectorySeparatorChar, '/'),
                Sha256 = ComputeSha256(file),
            });
        }

        return files;
    }

    private static void VerifyDirectory(
        string root,
        IReadOnlyCollection<ManifestFileEntry> expectedFiles,
        IReadOnlyCollection<string> excludedNames,
        string label,
        List<string> issues)
    {
        if (!Directory.Exists(root))
        {
            issues.Add($"Falta el directorio {label}: {root}");
            return;
        }

        var expectedMap = expectedFiles.ToDictionary(
            entry => entry.Path.Replace('/', Path.DirectorySeparatorChar),
            entry => entry.Sha256,
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in expectedMap)
        {
            var fullPath = Path.Combine(root, pair.Key);
            if (!File.Exists(fullPath))
            {
                issues.Add($"Falta {label}\\{pair.Key}");
                continue;
            }

            var actualHash = ComputeSha256(fullPath);
            if (!string.Equals(actualHash, pair.Value, StringComparison.OrdinalIgnoreCase))
                issues.Add($"SHA-256 distinto en {label}\\{pair.Key}");
        }

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file);
            if (ShouldExclude(rel, excludedNames))
                continue;
            if (!expectedMap.ContainsKey(rel))
                issues.Add($"Archivo inesperado en {label}\\{rel}");
        }
    }

    private static bool ShouldExclude(string relativePath, IReadOnlyCollection<string> excludedNames)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => excludedNames.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string[] GetAppExcludedNames() => ["node_modules", ".git", "dist", ".env"];
}

internal sealed class ManifestFileEntry
{
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

internal sealed class AppReleaseVerificationResult
{
    public bool IsValid { get; set; }
    public List<string> Issues { get; set; } = new();

    public string ToDisplayText(int maxItems = 6)
    {
        if (IsValid)
            return "Integridad verificada.";

        var lines = Issues.Take(maxItems).ToList();
        if (Issues.Count > maxItems)
            lines.Add($"... y {Issues.Count - maxItems} problema(s) más.");
        return string.Join(Environment.NewLine, lines);
    }
}
