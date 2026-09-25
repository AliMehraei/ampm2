using System;
using System.Reflection;

namespace Ampm2;

/// <summary>Product details shown in both apps' About section.</summary>
public static class AppInfo
{
    public const string Name = "ampm2";
    public const string Tagline = "A lightweight manager for pm2 processes";
    public const string Author = "Ali Mehraei";
    public const string Email = "ali.mehraei.dev@gmail.com";
    /// <summary>Project page (the About section hides the link when empty).</summary>
    public const string GitHubUrl = "https://github.com/AliMehraei/ampm2";
    public const string Website = "https://www.argbyte.com";
    public const string License = "PolyForm Noncommercial 1.0.0 + education";
    public const string LicenseSummary = "Free for non-commercial and educational use worldwide · commercial use needs a license";
    public const string LicenseUrl = "https://github.com/AliMehraei/ampm2/blob/main/LICENSE.md";

    /// <summary>Version of the running app (the entry assembly), e.g. "1.1.0".</summary>
    public static string Version
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info)) return info.Split('+')[0];   // drop the source-revision suffix
            return asm.GetName().Version?.ToString(3) ?? "";
        }
    }

    public static string Platform =>
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "unknown";

    public static string Runtime => $".NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    public static string Copyright => $"© {DateTime.Now.Year} {Author}";
}
