using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ampm2.Sys;

/// <summary>Which kind of app is asking: decides the release file to fetch and how it is installed.</summary>
public enum UpdateTarget { WindowsInstaller, MacBundle }

public sealed record UpdateAsset(string Name, string Url, long Size);

public sealed record UpdateRelease(Version Version, string Tag, string Title, string PageUrl, IReadOnlyList<UpdateAsset> Assets)
{
    public UpdateAsset? Checksums => Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
}

/// <summary>A failure worth showing as is (no stack trace, no exception type).</summary>
public sealed class UpdateException(string message) : Exception(message);

/// <summary>
/// Finds, downloads, verifies and installs a newer ampm2 from GitHub releases.
/// Windows: runs the new installer silently once this copy has quit, and the installer reopens ampm2.
/// macOS: unpacks the new ampm2.app next to the running one, then a small script swaps the bundles after
/// ampm2 quits and reopens it. Every download is checked against the release's SHA256SUMS.txt.
/// AMPM2_UPDATE_FEED points the check at another release JSON (a URL or a local file) for testing, and
/// AMPM2_UPDATE_INSTALLER_ARGS adds installer switches (for example /CURRENTUSER /DIR=...).
/// </summary>
public static class Updater
{
    public const string DefaultFeed = "https://api.github.com/repos/AliMehraei/ampm2/releases/latest";
    public static string Feed => Environment.GetEnvironmentVariable("AMPM2_UPDATE_FEED") is { Length: > 0 } f ? f : DefaultFeed;

    /// <summary>Set once at startup by each app.</summary>
    public static UpdateTarget Target { get; set; } = OperatingSystem.IsMacOS() ? UpdateTarget.MacBundle : UpdateTarget.WindowsInstaller;

    public static Version Current => ParseVersion(AppInfo.Version) ?? new Version(0, 0, 0);

    /// <summary>"v1.4.0", "1.4.0" or "1.4.0-beta" → 1.4.0; null when it is not a version.</summary>
    public static Version? ParseVersion(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];
        if (!Version.TryParse(s, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    public static UpdateRelease ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        string tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var version = ParseVersion(tag) ?? throw new UpdateException($"The latest release has no version number (tag \"{tag}\").");
        var assets = new List<UpdateAsset>();
        if (r.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var a in arr.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                long size = a.TryGetProperty("size", out var z) && z.TryGetInt64(out var zz) ? zz : 0;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url)) assets.Add(new UpdateAsset(name, url, size));
            }
        string title = r.TryGetProperty("name", out var nm) && nm.GetString() is { Length: > 0 } ns ? ns : $"ampm2 {version.ToString(3)}";
        string page = r.TryGetProperty("html_url", out var h) && h.GetString() is { Length: > 0 } hs ? hs : AppInfo.GitHubUrl + "/releases/latest";
        return new UpdateRelease(version, tag, title, page, assets);
    }

    /// <summary>The release file this app installs, e.g. ampm2-setup-1.4.0.exe or ampm2-1.4.0-macos-arm64.zip.</summary>
    public static string PackageName(Version v) => Target == UpdateTarget.WindowsInstaller
        ? $"ampm2-setup-{v.ToString(3)}.exe"
        : $"ampm2-{v.ToString(3)}-macos-{(RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64")}.zip";

    public static UpdateAsset? Package(UpdateRelease r) =>
        r.Assets.FirstOrDefault(a => a.Name.Equals(PackageName(r.Version), StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether this copy can replace itself; otherwise <paramref name="reason"/> says why (the UI then offers the download page).</summary>
    public static bool CanInstallHere(out string reason)
    {
        reason = "";
        if (Target == UpdateTarget.WindowsInstaller)
        {
            if (!OperatingSystem.IsWindows()) { reason = "The Windows installer only runs on Windows."; return false; }
            return true;
        }
        if (!OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("AMPM2_UPDATE_BUNDLE") is not { Length: > 0 })
        {
            reason = "Installing updates works on macOS; here, download the new version from the release page.";
            return false;
        }
        if (RunningBundle() == null) { reason = "ampm2 is not running from an ampm2.app bundle, so it cannot replace itself."; return false; }
        return true;
    }

    /// <summary>The ampm2.app folder this process runs from (…/ampm2.app/Contents/MacOS/ampm2), or null.</summary>
    public static string? RunningBundle()
    {
        if (Environment.GetEnvironmentVariable("AMPM2_UPDATE_BUNDLE") is { Length: > 0 } test) return test;
        var exe = Environment.ProcessPath;
        for (var d = exe == null ? null : Path.GetDirectoryName(exe); d != null; d = Path.GetDirectoryName(d))
            if (d.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }

    // ---------------- network ----------------

    private static HttpClient NewClient()
    {
        var c = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true })
        { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"ampm2/{AppInfo.Version}");
        return c;
    }

    private static bool IsLocal(string urlOrPath) => !urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                                     && !urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static async Task<UpdateRelease> FetchLatestAsync(CancellationToken ct = default)
    {
        var feed = Feed;
        if (IsLocal(feed)) return ParseRelease(await File.ReadAllTextAsync(feed, ct).ConfigureAwait(false));
        using var http = NewClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var req = new HttpRequestMessage(HttpMethod.Get, feed);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req, cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new UpdateException("GitHub did not answer in time. Check your connection and try again."); }
        catch (HttpRequestException ex) { throw new UpdateException("Couldn't reach GitHub. Check your connection and try again." + Hint(ex)); }
        using (resp)
        {
            if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                throw new UpdateException("GitHub's limit for update checks was reached. Try again in an hour.");
            if (resp.StatusCode == HttpStatusCode.NotFound) throw new UpdateException("No ampm2 release was found on GitHub.");
            if (!resp.IsSuccessStatusCode) throw new UpdateException($"GitHub answered {(int)resp.StatusCode} {resp.ReasonPhrase}. Try again later.");
            return ParseRelease(await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
        }
    }

    private static string Hint(HttpRequestException ex) => ex.InnerException?.Message is { Length: > 0 } m ? $" ({m.TrimEnd('.')})" : "";

    /// <summary>Folder for downloads; cleared before each one.</summary>
    public static string DownloadDir => Path.Combine(Path.GetTempPath(), "ampm2-update" + DataPaths.Profile);

    /// <summary>
    /// Downloads this app's package to <see cref="DownloadDir"/> and checks it against SHA256SUMS.txt.
    /// Progress is 0..1, or -1 while the size is unknown. Returns the verified file.
    /// </summary>
    public static async Task<string> DownloadAsync(UpdateRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var pkg = Package(release) ?? throw new UpdateException($"Release {release.Version.ToString(3)} has no {PackageName(release.Version)}.");
        var sums = release.Checksums ?? throw new UpdateException("This release has no checksums, so it cannot be verified. Download it from the release page.");

        var dir = DownloadDir;
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);

        using var http = NewClient();
        var sumsText = Encoding.UTF8.GetString(await ReadAllAsync(http, sums.Url, ct).ConfigureAwait(false));
        var expected = ExpectedHash(sumsText, pkg.Name) ?? throw new UpdateException($"SHA256SUMS.txt has no entry for {pkg.Name}.");

        var path = Path.Combine(dir, pkg.Name);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = pkg.Size, done = 0;
        await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
        {
            await using var input = await OpenAsync(http, pkg.Url, ct, len => { if (total <= 0) total = len; }).ConfigureAwait(false);
            var buf = new byte[1 << 16];
            int n; double last = -2;
            while ((n = await input.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                hash.AppendData(buf, 0, n);
                done += n;
                double p = total > 0 ? Math.Min(1, (double)done / total) : -1;
                if (p < 0 || p - last >= 0.01) { progress?.Report(p); last = p; }
            }
        }
        var actual = Convert.ToHexString(hash.GetHashAndReset());
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new UpdateException("The download is damaged: its checksum does not match SHA256SUMS.txt. Nothing was installed. Try again.");
        }
        progress?.Report(1);
        return path;
    }

    /// <summary>The hash for <paramref name="file"/> in a "hash  name" (sha256sum) list, or null.</summary>
    public static string? ExpectedHash(string sums, string file)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            int sp = line.IndexOfAny(new[] { ' ', '\t' });
            if (sp <= 0) continue;
            var name = line[sp..].Trim().TrimStart('*');
            if (name.Equals(file, StringComparison.OrdinalIgnoreCase) && line[..sp].Length == 64) return line[..sp];
        }
        return null;
    }

    private static async Task<byte[]> ReadAllAsync(HttpClient http, string url, CancellationToken ct)
    {
        await using var s = await OpenAsync(http, url, ct, null).ConfigureAwait(false);
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    private static async Task<Stream> OpenAsync(HttpClient http, string url, CancellationToken ct, Action<long>? length)
    {
        if (IsLocal(url))
        {
            var fs = new FileStream(url, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true);
            length?.Invoke(fs.Length);
            return fs;
        }
        HttpResponseMessage resp;
        try { resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
        catch (HttpRequestException ex) { throw new UpdateException("The download failed. Check your connection and try again." + Hint(ex)); }
        if (!resp.IsSuccessStatusCode)
        {
            resp.Dispose();
            throw new UpdateException($"The download failed: GitHub answered {(int)resp.StatusCode} {resp.ReasonPhrase}.");
        }
        if (resp.Content.Headers.ContentLength is { } len) length?.Invoke(len);
        return await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    // ---------------- install ----------------

    /// <summary>
    /// Hands the verified package to a helper that waits for this process to exit, installs, and reopens ampm2.
    /// The caller must quit the app right after this returns. Throws <see cref="UpdateException"/> when
    /// nothing was started (for example the administrator prompt was declined).
    /// </summary>
    public static void StartInstall(string package, UpdateRelease release)
    {
        if (!CanInstallHere(out var reason)) throw new UpdateException(reason);
        if (Target == UpdateTarget.WindowsInstaller) StartWindowsInstaller(package);
        else StartMacSwap(package, release);
    }

    private static void StartWindowsInstaller(string setup)
    {
        // /RELAUNCH makes the installer reopen ampm2 when it is done (see installer\ampm2.iss).
        var extra = Environment.GetEnvironmentVariable("AMPM2_UPDATE_INSTALLER_ARGS") ?? "";
        var args = ("/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH " + extra).Trim();
        bool perUser = extra.Contains("/CURRENTUSER", StringComparison.OrdinalIgnoreCase);
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        // Wait for ampm2 to exit first: the installer refuses to run while ampm2 holds its single-instance lock.
        var script =
            "$ErrorActionPreference='SilentlyContinue'\n" +
            $"Wait-Process -Id {Environment.ProcessId} -Timeout 60\n" +
            $"Start-Process -FilePath {Q(setup)} -ArgumentList {Q(args)}\n";
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        // Ask for administrator rights now, while ampm2 is still open, so a declined prompt leaves everything as it was.
        if (!perUser && !Environment.IsPrivilegedProcess) psi.Verb = "runas";
        try { Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new UpdateException("The update was cancelled: installing needs administrator permission."); }
        catch (Exception ex) { throw new UpdateException("Couldn't start the installer: " + ex.Message); }
    }

    private static void StartMacSwap(string zip, UpdateRelease release)
    {
        var bundle = RunningBundle()!;
        var parent = Path.GetDirectoryName(bundle.TrimEnd('/'))!;
        var stage = Path.Combine(parent, $".ampm2-update-{Environment.ProcessId}");
        try
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UpdateException($"ampm2 can't replace itself in {parent} (no permission). Download it from the release page and drag ampm2.app there yourself.");
        }

        try
        {
            // ditto keeps permissions, symlinks and the code signature intact; plain unzip is the fallback outside macOS.
            if (File.Exists("/usr/bin/ditto")) Run("/usr/bin/ditto", "-x", "-k", zip, stage);
            else ZipFile.ExtractToDirectory(zip, stage);
            var app = Path.Combine(stage, "ampm2.app");
            var exe = Path.Combine(app, "Contents", "MacOS", "ampm2");
            var plist = Path.Combine(app, "Contents", "Info.plist");
            if (!File.Exists(exe) || !File.Exists(plist)) throw new UpdateException("The download does not contain ampm2.app.");
            var info = File.ReadAllText(plist);
            if (!info.Contains($"<string>{release.Version.ToString(3)}</string>"))
                throw new UpdateException($"The downloaded app is not version {release.Version.ToString(3)}.");

            var script = Path.Combine(stage, "swap.sh");
            File.WriteAllText(script, SwapScript, new UTF8Encoding(false));
            var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            foreach (var a in new[] { script, Environment.ProcessId.ToString(), bundle, app, stage }) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        catch
        {
            try { Directory.Delete(stage, true); } catch { }
            throw;
        }
    }

    private static void Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new UpdateException($"Unpacking the update failed: {err.Trim()}");
    }

    /// <summary>Waits for ampm2 to quit, swaps in the new bundle (restoring the old one on failure) and reopens it.</summary>
    public const string SwapScript = """
        #!/bin/sh
        # ampm2 updater: $1 = pid of the running ampm2, $2 = installed ampm2.app, $3 = new ampm2.app, $4 = staging folder
        pid="$1"; old="$2"; new="$3"; stage="$4"
        i=0
        while kill -0 "$pid" 2>/dev/null && [ "$i" -lt 300 ]; do sleep 0.2; i=$((i + 1)); done
        back="$stage/previous.app"
        if mv "$old" "$back"; then
          if mv "$new" "$old"; then rm -rf "$back"; else mv "$back" "$old"; fi
        fi
        xattr -dr com.apple.quarantine "$old" 2>/dev/null
        rm -rf "$stage"
        if [ "$(uname)" = Darwin ]; then /usr/bin/open "$old" --args --updated
        else "$old/Contents/MacOS/ampm2" --updated >/dev/null 2>&1 &
        fi
        """;
}
