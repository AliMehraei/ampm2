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
    public bool ConfirmDestructive { get; set; } = true;
    public bool AutoSyncSavedList { get; set; } = true;
    public bool SaveAfterStartingSaved { get; set; } = true;
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 720;

    private static string FilePath => Path.Combine(DataPaths.Dir, "settings.json");

    public static MacSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), MacSettingsJson.Default.MacSettings) ?? new MacSettings();
        }
        catch { }
        return new MacSettings();
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
