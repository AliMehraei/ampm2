using System;
using System.IO;

namespace Ampm2.Sys;

/// <summary>Where ampm2 keeps its own files, per platform.</summary>
public static class DataPaths
{
    /// <summary>AMPM2_PROFILE=name gives a separate data folder and instance lock (used for testing next to a real copy).</summary>
    public static string Profile => Environment.GetEnvironmentVariable("AMPM2_PROFILE") is { Length: > 0 } p ? "-" + p : "";

    /// <summary>Windows: %APPDATA%\ampm2 · macOS: ~/Library/Application Support/ampm2 · Linux: ~/.config/ampm2</summary>
    public static string Dir
    {
        get
        {
            string root;
            if (OperatingSystem.IsMacOS())
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
            else
                root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);   // %APPDATA% or ~/.config
            return Path.Combine(root, "ampm2" + Profile);
        }
    }
}

/// <summary>
/// pm2's daemon endpoints. Windows: fixed named pipes (\\.\pipe\rpc.sock, pub.sock) whatever PM2_HOME is.
/// macOS/Linux: Unix sockets inside PM2_HOME (default ~/.pm2). AMPM2_RPC_PIPE / AMPM2_PUB_PIPE override both (testing).
/// </summary>
public static class Pm2Endpoints
{
    public static string Home
    {
        get
        {
            var h = Environment.GetEnvironmentVariable("PM2_HOME");
            if (string.IsNullOrEmpty(h) && !OperatingSystem.IsWindows()) h = Pm2.Pm2Cli.LoginShellVariable("PM2_HOME");
            return string.IsNullOrEmpty(h) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pm2") : h;
        }
    }

    public static string Rpc => Environment.GetEnvironmentVariable("AMPM2_RPC_PIPE") is { Length: > 0 } o ? o
        : OperatingSystem.IsWindows() ? "rpc.sock" : Path.Combine(Home, "rpc.sock");

    public static string Pub => Environment.GetEnvironmentVariable("AMPM2_PUB_PIPE") is { Length: > 0 } o ? o
        : OperatingSystem.IsWindows() ? "pub.sock" : Path.Combine(Home, "pub.sock");

    /// <summary>True when the endpoints are pm2's real ones (not a test override).</summary>
    public static bool IsDefault => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AMPM2_RPC_PIPE"));
}
