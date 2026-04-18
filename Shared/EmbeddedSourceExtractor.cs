using System.Reflection;

namespace SysCondaWizard;

/// <summary>
/// Extracts the Astro project files embedded directly into this exe at build time.
/// The embed prefix is driven by AppProfile.EmbedPrefix — must match the
/// LogicalName pattern in the .csproj ($(AppEmbedPrefix)/...).
/// </summary>
internal static class EmbeddedSourceExtractor
{
    private static string Prefix => AppProfile.EmbedPrefix;

    public static string ResourceName => Prefix;

    public static bool IsAvailable =>
        Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .Any(n => n.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase));

    public static async Task ExtractToAsync(string destDir)
    {
        var asm       = Assembly.GetExecutingAssembly();
        var resources = asm.GetManifestResourceNames()
                           .Where(n => n.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                           .ToList();

        if (resources.Count == 0)
            throw new InvalidOperationException(
                $"No se encontraron archivos embebidos en el ejecutable.\n" +
                $"Recompila el wizard con SysCondaSourceDir configurado en el .csproj, o usa ZIP/Git.");

        Directory.CreateDirectory(destDir);

        foreach (var resourceName in resources)
        {
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
