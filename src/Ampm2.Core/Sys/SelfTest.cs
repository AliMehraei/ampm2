using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ampm2.Pm2;

namespace Ampm2.Sys;

/// <summary>
/// `ampm2.exe --selftest &lt;report.txt&gt;`: exercises the pm2 wire protocol against whatever daemon the
/// AMPM2_RPC_PIPE / AMPM2_PUB_PIPE pipes point to, and writes PASS/FAIL lines. Read-only unless
/// AMPM2_SELFTEST_ACTIONS=1 (then it also stops/starts/restarts pm_id 0).
/// </summary>
public static class SelfTest
{
    /// <summary>IProgress that reports inline (Progress&lt;T&gt; posts to a thread-pool context, too late to assert on).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    public static async Task<int> RunAsync(string reportPath)
    {
        var sb = new StringBuilder();
        int fails = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (!ok) fails++;
            sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
        }

        sb.AppendLine($"ampm2 {AppInfo.Version} self-test on {AppInfo.Platform} ({AppInfo.Runtime})");
        sb.AppendLine($"  rpc endpoint : {Pm2Endpoints.Rpc}");
        sb.AppendLine($"  pm2 home     : {Pm2Endpoints.Home}");
        sb.AppendLine($"  pm2 cli      : {Pm2Cli.Pm2Path ?? "(not found)"}");
        sb.AppendLine($"  node         : {Pm2Cli.NodePath ?? "(not found)"}");
        sb.AppendLine($"  data folder  : {DataPaths.Dir}");
        if (!OperatingSystem.IsWindows())
        {
            var shellEnv = Pm2Cli.LoginShellEnvironment();
            Check("login shell environment read", shellEnv.Count > 0 && shellEnv.ContainsKey("PATH"), $"{shellEnv.Count} variables");
            Check("ps cpu-time parser", Math.Abs(UnixProcessMetrics.ParseCpuTime("1:02.50") - 62.5) < 0.01 && Math.Abs(UnixProcessMetrics.ParseCpuTime("1-02:03:04") - 93784) < 0.01);
        }

        // help guide: both platforms have content, and every topic the apps link to exists
        {
            var win = Ampm2.Help.HelpContent.Topics(Ampm2.Help.HelpPlatform.Windows);
            var mac = Ampm2.Help.HelpContent.Topics(Ampm2.Help.HelpPlatform.Mac);
            var linked = new[] { "saved", "reboot", "admin" };
            bool ok = win.Count >= 15 && mac.Count >= 14 && win.All(t => t.Blocks.Count > 0) && mac.All(t => t.Blocks.Count > 0)
                      && linked.All(id => win.Any(t => t.Id == id) && mac.Any(t => t.Id == id)) && win.Any(t => t.Id == "strays")
                      && !mac.Any(t => t.Id is "strays" or "tray") && !win.Any(t => t.Id == "menubar");
            Check("help guide topics", ok, $"{win.Count} Windows topics, {mac.Count} macOS topics");
            var md = Ampm2.Help.HelpContent.ToMarkdown();
            Check("help guide exports to Markdown", md.StartsWith("# ampm2 help") && md.Contains("## The Saved list"), $"{md.Length} characters");
        }

        // updater: release parsing, version order, package choice, checksum verification (local files, no network)
        {
            Check("update version parsing", Updater.ParseVersion("v1.10.0") > Updater.ParseVersion("1.9.3") && Updater.ParseVersion("1.4.0-beta") == new Version(1, 4, 0)
                                            && Updater.ParseVersion("latest") == null && Updater.Current.Major >= 1, $"running {Updater.Current}");
            var dir = Path.Combine(Path.GetTempPath(), "ampm2-selftest-update-" + Environment.ProcessId);
            Directory.CreateDirectory(dir);
            try
            {
                var next = new Version(Updater.Current.Major, Updater.Current.Minor + 1, 0);
                var pkgName = Updater.PackageName(next);
                var pkg = Path.Combine(dir, "src-" + pkgName);
                File.WriteAllBytes(pkg, Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray());
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pkg))).ToLowerInvariant();
                var sums = Path.Combine(dir, "SHA256SUMS.txt");
                File.WriteAllText(sums, $"{new string('0', 64)}  other-file.zip\n{hash}  {pkgName}\n");
                string J(string s) => System.Text.Json.JsonSerializer.Serialize(s);
                var json = $$"""
                    {"tag_name":"v{{next.ToString(3)}}","name":"ampm2 {{next.ToString(3)}}","html_url":"https://example.invalid/r",
                     "assets":[{"name":{{J(pkgName)}},"browser_download_url":{{J(pkg)}},"size":300000},
                               {"name":"SHA256SUMS.txt","browser_download_url":{{J(sums)}},"size":10}]}
                    """;
                var rel = Updater.ParseRelease(json);
                Check("update release parsed", rel.Version == next && rel.Version > Updater.Current && Updater.Package(rel)?.Name == pkgName && rel.Checksums != null,
                      $"{rel.Title}, {rel.Assets.Count} assets, package {Updater.Package(rel)?.Name}");
                Check("update checksum list lookup", Updater.ExpectedHash(File.ReadAllText(sums), pkgName) == hash && Updater.ExpectedHash(File.ReadAllText(sums), "missing.exe") == null);

                double lastProgress = -5;
                var got = await Updater.DownloadAsync(rel, new SyncProgress(p => lastProgress = p));
                Check("update download verified", File.Exists(got) && new FileInfo(got).Length == 300_000 && Math.Abs(lastProgress - 1) < 1e-9, got);

                File.WriteAllText(sums, $"{new string('a', 64)}  {pkgName}\n");
                string err = "";
                try { await Updater.DownloadAsync(rel); } catch (UpdateException ex) { err = ex.Message; }
                Check("update refuses a damaged download", err.Contains("checksum") && !File.Exists(Path.Combine(Updater.DownloadDir, pkgName)), err);

                var noSums = rel with { Assets = rel.Assets.Where(a => a.Name != "SHA256SUMS.txt").ToList() };
                err = "";
                try { await Updater.DownloadAsync(noSums); } catch (UpdateException ex) { err = ex.Message; }
                Check("update refuses a release without checksums", err.Contains("no checksums"), err);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
                try { Directory.Delete(Updater.DownloadDir, true); } catch { }
            }

            // Unix only, opt-in: AMPM2_SELFTEST_SWAP=<empty folder>. Stages a real bundle swap of a fake
            // <folder>/Applications/ampm2.app; the swap runs after this process exits (tools/wsl-test.sh checks it).
            if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("AMPM2_SELFTEST_SWAP") is { Length: > 0 } swapRoot)
            {
                var next = new Version(Updater.Current.Major, Updater.Current.Minor + 1, 0);
                var installed = Path.Combine(swapRoot, "Applications", "ampm2.app");
                Directory.CreateDirectory(Path.Combine(installed, "Contents", "MacOS"));
                File.WriteAllText(Path.Combine(installed, "Contents", "MacOS", "ampm2"), "old build\n");
                File.WriteAllText(Path.Combine(installed, "Contents", "Info.plist"), "<string>0.0.1</string>\n");
                var zip = Path.Combine(swapRoot, "update.zip");
                using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var plist = z.CreateEntry("ampm2.app/Contents/Info.plist");
                    using (var w = new StreamWriter(plist.Open())) w.Write($"<key>CFBundleShortVersionString</key><string>{next.ToString(3)}</string>\n");
                    var exe = z.CreateEntry("ampm2.app/Contents/MacOS/ampm2");
                    exe.ExternalAttributes = Convert.ToInt32("100755", 8) << 16;
                    using (var w = new StreamWriter(exe.Open())) w.Write($"#!/bin/sh\n# new build\necho \"$@\" > \"{swapRoot}/relaunched\"\n");
                }
                Updater.Target = UpdateTarget.MacBundle;
                Environment.SetEnvironmentVariable("AMPM2_UPDATE_BUNDLE", installed);
                string wrong = "";
                try { Updater.StartInstall(zip, new UpdateRelease(new Version(next.Major, next.Minor + 5, 0), "x", "test", "", Array.Empty<UpdateAsset>())); }
                catch (UpdateException ex) { wrong = ex.Message; }
                Check("mac bundle swap refuses the wrong version", wrong.Contains("is not version"), wrong);
                string err = "";
                try { Updater.StartInstall(zip, new UpdateRelease(next, "v" + next, "test", "", Array.Empty<UpdateAsset>())); }
                catch (Exception ex) { err = ex.Message; }
                var stage = Path.Combine(swapRoot, "Applications", $".ampm2-update-{Environment.ProcessId}");
                Check("mac bundle swap staged", err.Length == 0 && File.Exists(Path.Combine(stage, "ampm2.app", "Contents", "MacOS", "ampm2")) && File.Exists(Path.Combine(stage, "swap.sh")),
                      err.Length > 0 ? err : stage);

                Environment.SetEnvironmentVariable("AMPM2_UPDATE_BUNDLE", null);
            }
        }

        // codec round trip, split across arbitrary chunk boundaries
        {
            var frame = Amp.Encode(new[] { Amp.Json("{\"a\":[1,2,3]}"), Amp.Str("id:7"), Array.Empty<byte>() });
            var got = new List<List<byte[]>>();
            var parser = new AmpParser();
            for (int i = 0; i < frame.Length; i += 3) parser.Feed(frame.AsSpan(i, Math.Min(3, frame.Length - i)), f => got.Add(f));
            parser.Feed(frame, f => got.Add(f));
            Check("amp codec round trip (chunked + whole)", got.Count == 2 && got.All(f => f.Count == 3 && Amp.AsString(f[1]) == "id:7" && f[2].Length == 0 && Amp.AsString(f[0]) == "{\"a\":[1,2,3]}"));
        }

        Pm2Rpc? rpc = null;
        Pm2Bus? bus = null;
        try
        {
            rpc = await Pm2Rpc.ConnectAsync(Pm2Endpoints.Rpc, CancellationToken.None);
            Check("connect rpc pipe", true, $"{Pm2Endpoints.Rpc}, server pid {rpc.DaemonPid}");
            Check("named pipe server pid resolved", rpc.DaemonPid > 0, rpc.DaemonPid.ToString());

            var ver = await rpc.GetVersionAsync();
            Check("getVersion", ver.Length > 0, ver);

            var list = await rpc.GetProcessesAsync();
            Check("getMonitorData parses processes", list.Count > 0, string.Join(", ", list.Select(p => $"{p.Id}:{p.Name}:{p.Status}:pid{p.Pid}")));
            if (list.Count > 0)
            {
                var p0 = list[0];
                Check("process fields", p0.Name.Length > 0 && p0.Status.Length > 0 && p0.Script.Length > 0, $"script={p0.Script} cwd={p0.Cwd} mode={p0.ExecMode} out={p0.OutLog}");
            }

            // ---- Saved list ----
            {
                var api = list.FirstOrDefault(p => p.Name == "api");
                var def = api?.Definition;
                Check("definition captured from pm2", def != null && def["script"]?.ToString() == api!.Script, def?.ToJsonString() ?? "null");
                var env = def?["env"] as System.Text.Json.Nodes.JsonObject;
                Check("definition keeps the app's own env", env?["PORT"]?.ToString() == "39871", env?.ToJsonString() ?? "no env");
                var leaked = env == null ? new List<string>() : env.Select(kv => kv.Key)
                    .Where(k => k is "PATH" or "Path" or "USERPROFILE" or "APPDATA" or "HOME" || k.StartsWith("PM2_") || k.EndsWith("_API_KEY")).ToList();
                Check("definition drops inherited/system env", leaked.Count == 0, leaked.Count == 0 ? $"{env?.Count ?? 0} vars kept" : string.Join(",", leaked));

                var lib = AppLibrary.Load();
                foreach (var p in list) if (p.Definition != null) lib.Upsert(p.Definition);
                lib.Save();
                var reloaded = AppLibrary.Load();
                Check("saved list round trip", reloaded.Count == lib.Count && reloaded.Count >= 3 && reloaded.Apps.All(a => System.Text.Json.Nodes.JsonNode.DeepEquals(a.Def, lib.Get(a.Name))),
                    $"{reloaded.Count} apps in {AppLibrary.FilePath}");

                // import: ecosystem with relative paths, and pm2's own dump
                var tmpDir = Path.Combine(Path.GetTempPath(), "ampm2-import-test");
                Directory.CreateDirectory(Path.Combine(tmpDir, "srv"));
                File.WriteAllText(Path.Combine(tmpDir, "srv", "index.js"), "");
                var eco = Path.Combine(tmpDir, "ecosystem.config.js");
                File.WriteAllText(eco, "module.exports = { apps: [ { name: 'web', script: './srv/index.js', env: { PORT: 8080 } }, { script: 'worker.js', cwd: 'srv' } ] };");
                try
                {
                    var imported = AppLibrary.ReadFile(eco);
                    Check("import .config.js (evaluated by node)", imported.Count == 2 &&
                        imported[0]["script"]?.ToString() == Path.Combine(tmpDir, "srv", "index.js") && imported[0]["cwd"]?.ToString() == tmpDir &&
                        imported[1]["name"]?.ToString() == "worker" && imported[1]["cwd"]?.ToString() == Path.Combine(tmpDir, "srv"),
                        string.Join(" | ", imported.Select(i => i.ToJsonString())));
                }
                catch (Exception ex) { Check("import .config.js (evaluated by node)", false, ex.Message); }
                var dump = Path.Combine(Environment.GetEnvironmentVariable("PM2_HOME") ?? Path.Combine(Path.GetTempPath(), "ampm2-test-pm2"), "dump.pm2");
                if (File.Exists(dump))
                {
                    try { var fromDump = AppLibrary.ReadFile(dump); Check("import pm2 dump.pm2", fromDump.Count > 0, string.Join(", ", fromDump.Select(d => AppLibrary.Name(d)))); }
                    catch (Exception ex) { Check("import pm2 dump.pm2", false, ex.Message); }
                }
                try { Directory.Delete(tmpDir, true); } catch { }

                // the real scenario: an app vanishes from pm2, the Saved list brings it back
                if (Environment.GetEnvironmentVariable("AMPM2_SELFTEST_ACTIONS") == "1" && Pm2Cli.Pm2Path is { } cli && list.FirstOrDefault(p => p.Name == "idle") is { } idle)
                {
                    var saved = reloaded.Get("idle")!;
                    await rpc.DeleteAsync(idle.Id);
                    Check("app deleted from pm2", (await rpc.GetProcessesAsync()).All(p => p.Name != "idle"));
                    var f = Path.Combine(DataPaths.Dir, "selftest-start.json");
                    File.WriteAllText(f, AppLibrary.Ecosystem(new[] { saved }));
                    var r = await Pm2Cli.Pm2Async(new[] { "start", f });
                    File.Delete(f);
                    var back = (await rpc.GetProcessesAsync()).FirstOrDefault(p => p.Name == "idle");
                    Check("app restored from the Saved list", r.Ok && back != null && back.Definition?["env"]?["PORT"]?.ToString() == "39872",
                        back == null ? r.Output.Split('\n').LastOrDefault() ?? "" : $"id {back.Id}, {back.Status}, env PORT={back.Definition?["env"]?["PORT"]}");
                }
            }

            try { await rpc.CallAsync("definitelyNotAMethod", "[]", TimeSpan.FromSeconds(5)); Check("unknown method reports error", false); }
            catch (Pm2Exception ex) { Check("unknown method reports error", ex.Message.Contains("does not exist"), ex.Message); }

            var metrics = ProcessMetricsFactory.Create();
            list = await rpc.GetProcessesAsync();   // the saved-list test above may have replaced a process
            var roots = list.Where(p => p.Pid > 0 && p.Status == "online").Select(p => p.Pid).ToList();
            metrics.Sample(roots);
            await Task.Delay(1100);
            var usage = metrics.Sample(roots);
            Check("native metrics sample online pids", roots.Count == 0 || usage.Count == roots.Count,
                string.Join(", ", usage.Select(kv => $"pid{kv.Key}: {kv.Value.CpuPercent}% {Fmt.Bytes(kv.Value.MemoryBytes)} tree={kv.Value.ProcessCount}")));

            try
            {
                bus = await Pm2Bus.ConnectAsync(Pm2Endpoints.Pub, CancellationToken.None);
                Check("connect bus pipe", true, Pm2Endpoints.Pub);
                var logTcs = new TaskCompletionSource<LogEvent>();
                var evTcs = new TaskCompletionSource<string>();
                bus.Log += e => logTcs.TrySetResult(e);
                bus.ProcessEvent += (ev, id) => evTcs.TrySetResult($"{ev}:{id}");
                bus.LogFilter = list.Count > 0 ? list[0].Id : 0;
                var lt = await Task.WhenAny(logTcs.Task, Task.Delay(5000));
                Check("live log line received (log:out/log:err)", lt == logTcs.Task, lt == logTcs.Task ? logTcs.Task.Result.Text.Trim() : "none within 5 s");

                if (Environment.GetEnvironmentVariable("AMPM2_SELFTEST_ACTIONS") == "1" && list.Count > 0)
                {
                    int id = list[0].Id;
                    await rpc.StopAsync(id);
                    var st = (await rpc.GetProcessesAsync()).First(p => p.Id == id).Status;
                    Check("stopProcessId", st == "stopped", st);
                    var et = await Task.WhenAny(evTcs.Task, Task.Delay(5000));
                    Check("process:event received", et == evTcs.Task, et == evTcs.Task ? evTcs.Task.Result : "none");
                    await rpc.StartAsync(id);
                    st = (await rpc.GetProcessesAsync()).First(p => p.Id == id).Status;
                    Check("startProcessId", st == "online", st);
                    int before = (await rpc.GetProcessesAsync()).First(p => p.Id == id).Restarts;
                    await rpc.RestartAsync(id);
                    int after = (await rpc.GetProcessesAsync()).First(p => p.Id == id).Restarts;
                    Check("restartProcessId increments restarts", after > before, $"{before} -> {after}");
                    await rpc.ResetAsync(id);
                    after = (await rpc.GetProcessesAsync()).First(p => p.Id == id).Restarts;
                    Check("resetMetaProcessId", after == 0, after.ToString());
                    if (list.Count > 1)
                    {
                        int del = list[^1].Id;
                        await rpc.DeleteAsync(del);
                        Check("deleteProcessId", (await rpc.GetProcessesAsync()).All(p => p.Id != del));
                    }
                }
            }
            catch (PipeConnectException ex) { Check("connect bus pipe", false, ex.Message); }
        }
        catch (PipeConnectException ex) { Check("connect rpc pipe", false, $"{ex.Kind}: {ex.Message}"); }
        catch (Exception ex) { Check("unexpected exception", false, ex.ToString()); }
        finally { bus?.Dispose(); rpc?.Dispose(); }

        sb.AppendLine(fails == 0 ? "ALL PASSED" : $"{fails} FAILED");
        await File.WriteAllTextAsync(reportPath, sb.ToString());
        return fails == 0 ? 0 : 1;
    }
}
