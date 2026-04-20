using System.Diagnostics;

namespace SysCondaWizard;

/// <summary>
/// PostgreSQL connection probe — two-stage, zero NuGet dependencies.
///
/// Stage 1: pg_isready  — fast TCP check, confirms server is accepting connections.
/// Stage 2: pg_dump -s  — credential check, confirms user + password are valid.
///
/// Both binaries live in the same directory as pg_dump.exe (already located by
/// PostgresBinaryLocator), so no extra path resolution is needed.
/// </summary>
internal static class PgProbe
{
    public static (bool ok, string message) Test(
        string pgDumpPath, string host, int port, string user, string password, string database)
    {
        var binDir = Path.GetDirectoryName(pgDumpPath) ?? "";
        var pgIsReady = Path.Combine(binDir, "pg_isready.exe");

        // ── Stage 1: pg_isready — is the server accepting connections? ────────
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pgIsReady,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--host"); psi.ArgumentList.Add(host);
            psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(port.ToString());
            psi.ArgumentList.Add("--username"); psi.ArgumentList.Add(user);
            psi.ArgumentList.Add("--dbname"); psi.ArgumentList.Add(database);

            using var proc = Process.Start(psi)!;
            proc.WaitForExit(5000);

            if (!proc.HasExited)
            {
                proc.Kill();
                return (false, $"Timeout (5s) — no se pudo conectar a {host}:{port}.");
            }

            // Exit codes: 0 = accepting, 1 = rejecting, 2 = no response, 3 = no attempt
            if (proc.ExitCode != 0)
            {
                var msg = proc.ExitCode switch
                {
                    1 => $"El servidor en {host}:{port} está rechazando conexiones.",
                    2 => $"No hay respuesta del servidor en {host}:{port}.",
                    _ => $"pg_isready no pudo intentar la conexión (código {proc.ExitCode}).",
                };
                return (false, msg);
            }
        }
        catch (Exception ex)
        {
            return (false, $"pg_isready: {ex.Message}");
        }

        // ── Stage 2: pg_dump -s — do the credentials actually work? ──────────
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pgDumpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--host"); psi.ArgumentList.Add(host);
            psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(port.ToString());
            psi.ArgumentList.Add("--username"); psi.ArgumentList.Add(user);
            psi.ArgumentList.Add("--schema-only"); // no data — just auth + connection check
            psi.ArgumentList.Add("--table"); psi.ArgumentList.Add("pg_catalog.pg_tables");
            psi.ArgumentList.Add(database);

            // PGPASSWORD works for all PG auth methods: SCRAM-SHA-256, MD5, trust
            psi.EnvironmentVariables["PGPASSWORD"] = password;

            using var proc = Process.Start(psi)!;

            // Read stderr async before WaitForExit to avoid deadlock on large output
            var stderrTask = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit(5000);

            if (!proc.HasExited)
            {
                proc.Kill();
                return (false, "Timeout (5s) verificando credenciales.");
            }

            if (proc.ExitCode == 0)
                return (true, "OK");

            var err = stderrTask.Result.Trim();
            return (false, string.IsNullOrWhiteSpace(err)
                ? $"Error de autenticación (código {proc.ExitCode})."
                : err);
        }
        catch (Exception ex)
        {
            return (false, $"pg_dump: {ex.Message}");
        }
    }
}
