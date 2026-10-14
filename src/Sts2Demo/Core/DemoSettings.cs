using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2Demo.Core;

public static class DemoSettings
{
    public static bool SeparateButton { get; set; }
    public static bool AllActivities { get; set; } = true;
    private const string Path = "user://friend-view.cfg";

    public static void Load()
    {
        var config = new ConfigFile();
        if (config.Load(Path) != Error.Ok) return;
        var entry = config.GetValue("demo", "separate_button", false);
        var availability = config.GetValue("demo", "all_activities", true);
        SeparateButton = entry.VariantType == Variant.Type.Bool && entry.AsBool();
        AllActivities = availability.VariantType != Variant.Type.Bool || availability.AsBool();
    }

    public static void Save()
    {
        var config = new ConfigFile();
        config.SetValue("demo", "separate_button", SeparateButton);
        config.SetValue("demo", "all_activities", AllActivities);
        if (config.Save(Path) != Error.Ok) Log.Warn("[Friend View] Settings could not be saved.");
    }
}
