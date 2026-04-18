using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace SysCondaWizard;

internal static class PostgresBinaryLocator
{
    public const string RequiredMajorVersion = "18";

    public static string FindPgDumpPath()
    {
        return FindBinaryPath("pg_dump.exe") ?? DefaultPath("pg_dump.exe");
    }

    public static string FindPgRestorePath()
    {
        return FindBinaryPath("pg_restore.exe") ?? DefaultPath("pg_restore.exe");
    }

    public static string? FindBinDirectory()
    {
        return CandidateBinDirectories().FirstOrDefault(ContainsRequiredBinaries);
    }

    public static bool IsSupportedBinaryPath(string? path, string fileName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        if (!string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var binDir = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(binDir) || !ContainsRequiredBinaries(binDir))
        {
            return false;
        }

        return IsRequiredVersionPath(binDir);
    }

    private static string? FindBinaryPath(string fileName)
    {
        foreach (var directory in CandidateBinDirectories())
        {
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateBinDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var binDir = Path.Combine(root, "PostgreSQL", RequiredMajorVersion, "bin");
            if (Directory.Exists(binDir) && seen.Add(binDir))
            {
                yield return binDir;
            }
        }

        foreach (var installDir in ReadInstallDirsFromRegistry())
        {
            var binDir = Path.Combine(installDir, "bin");
            if (Directory.Exists(binDir) && seen.Add(binDir))
            {
                yield return binDir;
            }
        }

        foreach (var binDir in ReadServiceBinDirsFromRegistry())
        {
            if (Directory.Exists(binDir) && seen.Add(binDir))
            {
                yield return binDir;
            }
        }
    }

    private static IEnumerable<string> ReadInstallDirsFromRegistry()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var baseKey = hive.OpenSubKey(@"SOFTWARE\PostgreSQL\Installations");
            if (baseKey == null)
            {
                continue;
            }

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                using var installKey = baseKey.OpenSubKey(subKeyName);
                var version = installKey?.GetValue("Version")?.ToString();
                if (string.IsNullOrWhiteSpace(version) || !version.StartsWith(RequiredMajorVersion + ".", StringComparison.Ordinal))
                {
                    continue;
                }

                var baseDir = installKey?.GetValue("Base Directory")?.ToString();
                if (!string.IsNullOrWhiteSpace(baseDir))
                {
                    yield return baseDir;
                }
            }
        }
    }

    private static IEnumerable<string> ReadServiceBinDirsFromRegistry()
    {
        const string servicesKeyPath = @"SYSTEM\CurrentControlSet\Services";
        const string imagePathValueName = "ImagePath";

        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var servicesKey = hive.OpenSubKey(servicesKeyPath);
            if (servicesKey == null)
            {
                continue;
            }

            foreach (var subKeyName in servicesKey.GetSubKeyNames())
            {
                if (!subKeyName.Contains("postgres", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var serviceKey = servicesKey.OpenSubKey(subKeyName);
                var imagePath = serviceKey?.GetValue(imagePathValueName)?.ToString();
                var binDir = TryExtractBinDirectory(imagePath);
                if (!string.IsNullOrWhiteSpace(binDir))
                {
                    yield return binDir;
                }
            }
        }
    }

    private static string? TryExtractBinDirectory(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var match = Regex.Match(
            Environment.ExpandEnvironmentVariables(imagePath),
            "(?<path>[A-Za-z]:\\[^\"\\r\\n]*?PostgreSQL\\18(?:\\.\\d+)?\\bin)\\postgres(?:\\.exe)?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        var binDir = match.Groups["path"].Value;
        return Directory.Exists(binDir) ? binDir : null;
    }

    private static bool ContainsRequiredBinaries(string binDir)
    {
        return File.Exists(Path.Combine(binDir, "pg_dump.exe")) &&
               File.Exists(Path.Combine(binDir, "pg_restore.exe"));
    }

    private static bool IsRequiredVersionPath(string path)
    {
        return path.Contains($@"\PostgreSQL\{RequiredMajorVersion}\", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(path, $@"\\PostgreSQL\\{RequiredMajorVersion}(?:\.\d+)?\\", RegexOptions.IgnoreCase);
    }

    private static string DefaultPath(string fileName)
    {
        return $@"C:\Program Files\PostgreSQL\{RequiredMajorVersion}\bin\{fileName}";
    }
}

