using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2Demo.Core;

public static class DemoSettings
{
    public static bool SeparateButton { get; set; } = true;
    // Legacy fixture compatibility. Availability is no longer a preference.
    public static bool AllActivities { get => true; set { } }
    public static bool AutoExitTurn { get; set; } = true;
    public static bool AutoObserve { get; set; } = true;
    public static bool AutoExitRoom { get; set; } = true;
    public static bool FollowCombat { get; set; } = true;
    private const string Path = "user://friend-view.cfg";

    public static void Load()
    {
        using var config = new ConfigFile();
        if (config.Load(Path) != Error.Ok) return;
        bool Read(string key) { var v = config.GetValue("demo", key, true); return v.VariantType != Variant.Type.Bool || v.AsBool(); }
        SeparateButton = Read("separate_button");
        AutoExitTurn = Read("auto_exit_turn"); AutoObserve = Read("auto_observe");
        AutoExitRoom = Read("auto_exit_room"); FollowCombat = Read("follow_combat");
    }

    public static void Save()
    {
        using var config = new ConfigFile();
        config.SetValue("demo", "separate_button", SeparateButton);
        config.SetValue("demo", "auto_exit_turn", AutoExitTurn);
        config.SetValue("demo", "auto_observe", AutoObserve);
        config.SetValue("demo", "auto_exit_room", AutoExitRoom);
        config.SetValue("demo", "follow_combat", FollowCombat);
        if (config.Save(Path) != Error.Ok) Log.Warn("[Friend View] Settings could not be saved.");
    }
}
