using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace Pos.App.Services;

/// <summary>
/// Applies an app update on Windows: downloads the new build's zip, unpacks it, and hands off to a
/// tiny batch script that waits for this process to exit, copies the new files over the install
/// folder, and relaunches. The swap has to happen from OUTSIDE the app because a running exe can't
/// overwrite itself.
///
/// The machine's own <c>.env</c> is never touched — it is deleted from the unpacked copy before
/// the swap, so a till keeps pointing at its own server after updating.
///
/// Best-effort and reversible up to the last step: any failure while downloading or unpacking
/// throws before a single file of the install is changed, so a bad or half-downloaded update
/// leaves the working app exactly as it was.
/// </summary>
public sealed class AppUpdater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Downloads and stages the update, then launches the swap-and-restart helper and asks the app
    /// to shut down. Returns only if it could NOT start (so the caller can show the error); on
    /// success the process is on its way out.
    /// </summary>
    /// <param name="url">The build zip's URL, from the server manifest.</param>
    /// <param name="onProgress">0–1 download progress, for a bar.</param>
    /// <param name="shutdown">How to close the app once the helper is launched.</param>
    public async Task<string?> DownloadAndApplyAsync(string url, IProgress<double>? onProgress, Action shutdown,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Update ka download link nahi mila.";
        }

        try
        {
            var work = Path.Combine(Path.GetTempPath(), "PosAppUpdate");
            var extractDir = Path.Combine(work, "new");
            var zipPath = Path.Combine(work, "update.zip");

            // Fresh staging area each time, so a previous half-run can't poison this one.
            if (Directory.Exists(work))
            {
                Directory.Delete(work, recursive: true);
            }
            Directory.CreateDirectory(work);

            await DownloadAsync(url, zipPath, onProgress, ct);

            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // The zip may wrap everything in one top folder; the real source is wherever the exe is.
            var source = FindAppRoot(extractDir);
            if (source is null)
            {
                return "Download me app files nahi mile — link galat build ka lagta hai.";
            }

            // Keep this machine's server config: drop the packaged .env so the swap can't replace it.
            foreach (var env in Directory.GetFiles(source, ".env", SearchOption.TopDirectoryOnly))
            {
                File.Delete(env);
            }

            var target = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var exeName = Path.GetFileName(Environment.ProcessPath ?? "Pos.App.exe");
            var script = WriteUpdaterScript(work, source, target, exeName);

            Process.Start(new ProcessStartInfo
            {
                FileName = script,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = work,
            });

            shutdown();
            return null;
        }
        catch (Exception ex)
        {
            return "Update fail ho gaya: " + ex.Message;
        }
    }

    private static async Task DownloadAsync(string url, string dest, IProgress<double>? onProgress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0)
            {
                onProgress?.Report((double)read / total);
            }
        }
        onProgress?.Report(1);
    }

    /// <summary>The folder inside the unpacked zip that actually holds the app (the one with the
    /// exe), whether the zip was flat or wrapped in a single top folder.</summary>
    private static string? FindAppRoot(string extractDir)
    {
        if (Directory.GetFiles(extractDir, "*.exe", SearchOption.TopDirectoryOnly).Length > 0)
        {
            return extractDir;
        }

        var subDirs = Directory.GetDirectories(extractDir);
        if (subDirs.Length == 1 &&
            Directory.GetFiles(subDirs[0], "*.exe", SearchOption.TopDirectoryOnly).Length > 0)
        {
            return subDirs[0];
        }

        // Fall back to the first folder anywhere that contains an exe.
        foreach (var exe in Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories))
        {
            return Path.GetDirectoryName(exe);
        }
        return null;
    }

    /// <summary>
    /// Writes the batch that does the actual swap: wait for this exe to close, copy the new files
    /// in, relaunch. /EXCLUDE is deliberately not used — the packaged .env was already removed, so
    /// a plain overwrite copy is enough and leaves the local .env in place.
    /// </summary>
    private static string WriteUpdaterScript(string workDir, string source, string target, string exeName)
    {
        var path = Path.Combine(workDir, "apply-update.bat");
        var script =
            "@echo off\r\n" +
            "chcp 65001 >nul\r\n" +
            $"set \"EXE={exeName}\"\r\n" +
            $"set \"SRC={source}\"\r\n" +
            $"set \"DST={target}\"\r\n" +
            ":wait\r\n" +
            "tasklist /FI \"IMAGENAME eq %EXE%\" 2>nul | find /I \"%EXE%\" >nul\r\n" +
            "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )\r\n" +
            "xcopy \"%SRC%\\*\" \"%DST%\\\" /E /Y /I /Q >nul\r\n" +
            "start \"\" \"%DST%\\%EXE%\"\r\n";
        File.WriteAllText(path, script, System.Text.Encoding.UTF8);
        return path;
    }
}
