using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public ManifestDirectorySnapshot? AppSnapshot { get; set; }
    public ManifestDirectorySnapshot? RuntimeSnapshot { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ManifestFileEntry>? AppFiles { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ManifestFileEntry>? RuntimeFiles { get; set; }

    public static AppReleaseManifest Capture(WizardConfig cfg, string sourceDescription)
    {
        var exePath = Application.ExecutablePath;
        var exeVersion = FileVersionInfo.GetVersionInfo(exePath).ProductVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "1.0.0";

        var wizardSha = ComputeSha256(exePath);
        var appSnapshot = CaptureDirectorySnapshot(cfg.AppDirectory, GetAppExcludedNames());
        var runtimeSnapshot = Directory.Exists(cfg.ServiceRuntimeDirectory)
            ? CaptureDirectorySnapshot(cfg.ServiceRuntimeDirectory, Array.Empty<string>())
            : null;
        return new AppReleaseManifest
        {
            AppName = AppProfile.AppName,
            ReleaseId = $"{exeVersion}+{wizardSha[..8]}",
            WizardVersion = exeVersion,
            WizardExecutableSha256 = wizardSha,
            SourceDescription = sourceDescription,
            InstallMode = cfg.IsUpdateMode ? "update" : "install",
            InstalledAtUtc = DateTimeOffset.UtcNow,
            AppSnapshot = appSnapshot,
            RuntimeSnapshot = runtimeSnapshot,
            AppFiles = null,
            RuntimeFiles = null,
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
        VerifyDirectory(
            cfg.AppDirectory,
            AppSnapshot,
            AppFiles ?? [],
            GetAppExcludedNames(),
            "app",
            issues);
        if (RuntimeSnapshot != null || (RuntimeFiles?.Count ?? 0) > 0)
        {
            VerifyDirectory(
                cfg.ServiceRuntimeDirectory,
                RuntimeSnapshot,
                RuntimeFiles ?? [],
                Array.Empty<string>(),
                "runtime",
                issues);
        }

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

    private static ManifestDirectorySnapshot CaptureDirectorySnapshot(string root, IReadOnlyCollection<string> excludedNames)
    {
        var fingerprints = EnumerateDirectoryFingerprints(root, excludedNames).ToList();
        using var sha = SHA256.Create();
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, leaveOpen: true))
        {
            foreach (var fingerprint in fingerprints)
                writer.WriteLine($"{fingerprint.Path}|{fingerprint.Sha256}");
            writer.Flush();
        }

        stream.Position = 0;
        return new ManifestDirectorySnapshot
        {
            FileCount = fingerprints.Count,
            Sha256 = Convert.ToHexString(sha.ComputeHash(stream)),
        };
    }

    private static List<ManifestFileEntry> CaptureDirectory(string root, IReadOnlyCollection<string> excludedNames)
    {
        var files = new List<ManifestFileEntry>();
        if (!Directory.Exists(root))
            return files;

        foreach (var fingerprint in EnumerateDirectoryFingerprints(root, excludedNames))
        {
            files.Add(new ManifestFileEntry
            {
                Path = fingerprint.Path,
                Sha256 = fingerprint.Sha256,
            });
        }

        return files;
    }

    private static void VerifyDirectory(
        string root,
        ManifestDirectorySnapshot? expectedSnapshot,
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

        if (expectedSnapshot != null)
        {
            var actualSnapshot = CaptureDirectorySnapshot(root, excludedNames);
            if (!string.Equals(actualSnapshot.Sha256, expectedSnapshot.Sha256, StringComparison.OrdinalIgnoreCase))
                issues.Add($"SHA-256 resumido distinto en {label}");
            if (actualSnapshot.FileCount != expectedSnapshot.FileCount)
                issues.Add($"Cantidad de archivos distinta en {label}: {actualSnapshot.FileCount} != {expectedSnapshot.FileCount}");
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

    private static IEnumerable<ManifestFileEntry> EnumerateDirectoryFingerprints(string root, IReadOnlyCollection<string> excludedNames)
    {
        if (!Directory.Exists(root))
            yield break;

        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(root, file);
            if (ShouldExclude(rel, excludedNames))
                continue;

            yield return new ManifestFileEntry
            {
                Path = rel.Replace(Path.DirectorySeparatorChar, '/'),
                Sha256 = ComputeSha256(file),
            };
        }
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

internal sealed class ManifestDirectorySnapshot
{
    public int FileCount { get; set; }
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
