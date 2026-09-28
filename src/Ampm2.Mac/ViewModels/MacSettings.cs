using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ampm2.Sys;

namespace Ampm2.Mac.ViewModels;

public sealed class MacSettings
{
    public string Theme { get; set; } = "System";          // System | Dark | Light
    public int MetricsIntervalSec { get; set; } = 2;
    public int ListIntervalSec { get; set; } = 60;
    public bool CloseToMenuBar { get; set; } = true;
    public bool MinimizeToMenuBar { get; set; } = true;       // minimizing hides the window (menu bar icon only) instead of the Dock
    /// <summary>Bumped when a default changes for existing users; see <see cref="Upgrade"/>.</summary>
    public int SettingsVersion { get; set; }
    public bool ConfirmDestructive { get; set; } = true;
    public bool AutoSyncSavedList { get; set; } = true;
    public bool SaveAfterStartingSaved { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;         // check GitHub in the background, banner when a release is newer
    public string DismissedUpdate { get; set; } = "";         // version whose update banner was closed
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 720;

    private static string FilePath => Path.Combine(DataPaths.Dir, "settings.json");

    public static MacSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return (JsonSerializer.Deserialize(File.ReadAllText(FilePath), MacSettingsJson.Default.MacSettings) ?? new MacSettings()).Upgrade();
        }
        catch { }
        return new MacSettings { SettingsVersion = Current };
    }

    private const int Current = 1;

    /// <summary>One-time changes for settings saved by an older version (a saved value otherwise always wins over a new default).</summary>
    private MacSettings Upgrade()
    {
        if (SettingsVersion < 1) MinimizeToMenuBar = true;   // 1.6.1: minimize to the menu bar is on by default
        if (SettingsVersion < Current) { SettingsVersion = Current; Save(); }
        return this;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataPaths.Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, MacSettingsJson.Default.MacSettings));
        }
        catch { }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(MacSettings))]
internal partial class MacSettingsJson : JsonSerializerContext { }
