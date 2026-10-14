namespace Sts2Demo.Core;

internal enum DemoInputAction { None, Exit, ToggleFriend, Upgrades, Navigate, Block }

/// <summary>Context ownership of native actions; no physical gamepad button numbers.</summary>
internal static class DemoInputPolicy
{
    public static DemoInputAction Resolve(string action, bool combatView, bool rewardView,
        bool friendFocused, bool upgradesAvailable)
    {
        bool viewing = combatView || rewardView;
        if (viewing && action == "ui_cancel") return DemoInputAction.Exit;
        // Native select is A (focused controls); native accept is Y (global confirm).
        if (action == "ui_select" && friendFocused) return DemoInputAction.ToggleFriend;
        if (rewardView && action == "ui_accept" && upgradesAvailable) return DemoInputAction.Upgrades;
        if (viewing && action is "ui_left" or "ui_right" or "ui_up" or "ui_down") return DemoInputAction.Navigate;
        if (rewardView || combatView && action == "ui_select") return DemoInputAction.Block;
        // In combat ui_accept still belongs to the LOCAL End Turn button.
        return DemoInputAction.None;
    }
}
