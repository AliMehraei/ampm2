using System;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using Ampm2.Sys;

namespace Ampm2.Ui;

/// <summary>One row of the Saved list.</summary>
public sealed class SavedApp : ObservableObject
{
    public SavedApp(string name, JsonObject def, DateTime updated) { Name = name; SetDef(def, updated); }

    public string Name { get; }
    private JsonObject _def = new();
    public JsonObject Def => _def;
    public string Script { get; private set; } = "";
    public string Cwd { get; private set; } = "";
    public string Json { get; private set; } = "";
    public string UpdatedText { get; private set; } = "";
    public string Summary { get; private set; } = "";

    public void SetDef(JsonObject def, DateTime updated)
    {
        _def = def;
        Script = def["script"]?.ToString() ?? "";
        Cwd = def["cwd"]?.ToString() ?? "";
        Json = def.ToJsonString(AppLibrary.Indented);
        UpdatedText = updated == DateTime.MinValue ? "" : "saved " + updated.ToString(updated.Year == DateTime.Now.Year ? "MMM d, HH:mm" : "yyyy-MM-dd");
        var mode = def["exec_mode"]?.ToString() == "cluster" ? $"cluster ×{def["instances"]?.ToString() ?? "?"}" : "fork";
        var envCount = def["env"] is JsonObject env ? env.Count : 0;
        Summary = $"{mode}{(envCount > 0 ? $" · {envCount} env var{(envCount == 1 ? "" : "s")}" : "")}";
        Raise(string.Empty);
    }

    /// <summary>online | stopped | errored | missing (not in pm2) | unknown (not connected)</summary>
    private string _pm2State = "unknown";
    public string Pm2State
    {
        get => _pm2State;
        set { if (Set(ref _pm2State, value)) { Raise(nameof(StateText)); Raise(nameof(StateBrush)); Raise(nameof(StateSoftBrush)); Raise(nameof(IsMissing)); } }
    }
    public bool IsMissing => _pm2State == "missing";
    public string StateText => _pm2State switch
    {
        "missing" => "not in pm2",
        "unknown" => "pm2 not connected",
        var s => "in pm2 · " + s,
    };
    private string Key => _pm2State switch { "online" => "Online", "errored" => "Errored", "missing" => "Launching", _ => "Stopped" };
    public Brush StateBrush => (Brush)Application.Current.Resources[Key];
    public Brush StateSoftBrush => (Brush)Application.Current.Resources[Key + "Soft"];
    public void ThemeChanged() { Raise(nameof(StateBrush)); Raise(nameof(StateSoftBrush)); }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}
