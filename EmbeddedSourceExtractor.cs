using System.Reflection;

namespace SysCondaWizard;

/// <summary>
/// Extracts the Astro project files that were embedded directly into this exe
/// at build time via the EmbeddedResource glob in the csproj.
///
/// Each file is stored as a resource with a logical name like:
///   sysconda-source/src/pages/index.astro
///   sysconda-source/package.json
///   sysconda-source/astro.config.mjs
///
/// At install time we recreate that tree under the destination directory.
/// </summary>
internal static class EmbeddedSourceExtractor
{
    private const string Prefix = "sysconda-source/";

    // Keep ResourceName for backward compat with the validation check in Step1_Location
    public const string ResourceName = Prefix;

    /// <summary>
    /// Returns true if the exe contains at least one embedded source file.
    /// </summary>
    public static bool IsAvailable =>
        Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .Any(n => n.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Writes every embedded source file into <paramref name="destDir"/>,
    /// preserving the original directory structure.
    /// </summary>
    public static async Task ExtractToAsync(string destDir)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resources = asm.GetManifestResourceNames()
                           .Where(n => n.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                           .ToList();

        if (resources.Count == 0)
            throw new InvalidOperationException(
                "No se encontraron archivos embebidos en el ejecutable.\n" +
                "Recompila el wizard con SysCondaSourceDir configurado en el .csproj.");

        Directory.CreateDirectory(destDir);

        foreach (var resourceName in resources)
        {
            // Strip the "sysconda-source/" prefix to get the relative path
            var relativePath = resourceName[Prefix.Length..]
                .Replace('/', Path.DirectorySeparatorChar);

            var targetPath = Path.Combine(destDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            using var stream = asm.GetManifestResourceStream(resourceName)!;
            await using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fs);
        }
    }
}
