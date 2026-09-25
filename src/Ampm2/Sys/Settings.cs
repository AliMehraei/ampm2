using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ampm2.Sys;

public sealed class Settings
{
    public string Theme { get; set; } = "System";          // System | Dark | Light
    public int MetricsIntervalSec { get; set; } = 2;       // native CPU/memory sampling while visible
    public int ListIntervalSec { get; set; } = 60;         // backstop pm2 list refresh (pm2 itself spawns WMI per list call)
    public bool CloseToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool ConfirmDestructive { get; set; } = true;
    public bool GpuRendering { get; set; }                    // off = software rendering, the low-memory default
    public bool AutoSyncSavedList { get; set; } = true;       // copy pm2's apps into ampm2's Saved list as they change
    public bool SaveAfterStartingSaved { get; set; } = true;  // pm2 save after starting apps from the Saved list
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 720;
    public double DetailWidth { get; set; } = 420;

    public static string Profile => DataPaths.Profile;
    public static string Dir => DataPaths.Dir;
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.Settings) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
        }
        catch { }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal partial class SettingsJson : JsonSerializerContext { }
