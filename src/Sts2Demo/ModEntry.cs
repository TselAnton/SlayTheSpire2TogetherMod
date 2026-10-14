using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using Sts2Demo.Ui;
using Sts2Demo.Core;

namespace Sts2Demo;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "Sts2Demo";
    public const string Version = "1.2.1";
    internal static bool Initialized { get; private set; }
    public static void Initialize()
    {
        if (OperatingSystem.IsLinux()) Log.Info($"[Friend View] {LinuxHarmonyBootstrap.Prepare()}.");
        new Harmony("together.sts2.demo").PatchAll(typeof(ModEntry).Assembly);
        Initialized = true;
        Log.Info($"[Friend View] Loaded {Version}. Read-only view; default profile only when Friend View is the sole loaded mod.");
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class AttachDemo
{
    static void Postfix(NCombatUi __instance) => Callable.From(() =>
    {
        if (GodotObject.IsInstanceValid(__instance) && __instance.IsInsideTree())
            __instance.GetParent().AddChild(new DemoController { Name = "TogetherDemo", Ui = __instance });
    }).CallDeferred();
}

// Only block local UI input while viewing. These patches do not intercept game actions,
// networking commands, enemy turns, or another player's model.
[HarmonyPatch(typeof(NPlayerHand), "CanPlayCards")]
internal static class BlockLocalPlay
{
    static bool Prefix(ref bool __result)
    {
        if (!DemoController.IsViewing) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand._UnhandledInput))]
internal static class BlockHandShortcuts
{
    static bool Prefix() => !DemoController.IsViewing;
}

[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand.SelectCards))]
internal static class ExitForRequiredChoice
{
    static void Prefix() => DemoController.Current?.ExitDemo();
}

// The engine allows cosmetic mod mismatches but does not negotiate custom packet types.
// Require this protocol in its existing co-op compatibility check, while keeping the
// manifest cosmetic. An unmodded/mismatched client gets the normal ModMismatch dialog.
[HarmonyPatch(typeof(ModManager), nameof(ModManager.GetGameplayRelevantModNameList))]
internal static class RequireSameDemoProtocol
{
    static void Postfix(ref List<string>? __result)
    {
        __result ??= [];
        string requirement = $"{ModEntry.Id}-{ModEntry.Version}";
        if (!__result.Contains(requirement)) __result.Add(requirement);
    }
}

[HarmonyPatch(typeof(NRemoteMouseCursorContainer), "UpdateCursorVisibility")]
internal static class HideDuplicateRemoteCursor
{
    static void Postfix(NRemoteMouseCursorContainer __instance)
    {
        if ((DemoController.ObservedPlayerId ?? RewardDemoController.ObservedPlayerId) is not ulong target) return;
        foreach (var cursor in DemoUi.Descendants<NRemoteMouseCursor>(__instance))
            if (cursor.PlayerId == target) cursor.Visible = false;
    }
}

[HarmonyPatch(typeof(NRemoteTargetingIndicator), nameof(NRemoteTargetingIndicator.StartDrawingFrom))]
internal static class HideObservedRemoteLine
{
    static void Postfix(NRemoteTargetingIndicator __instance)
    {
        var player = (MegaCrit.Sts2.Core.Entities.Players.Player?)AccessTools.Field(typeof(NRemoteTargetingIndicator), "_player").GetValue(__instance);
        if (player?.NetId == DemoController.ObservedPlayerId) __instance.Modulate = Colors.Transparent;
    }
}
