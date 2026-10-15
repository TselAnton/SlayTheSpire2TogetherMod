using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens;
using System.Runtime.CompilerServices;

namespace Sts2Demo.Ui;

// A local pile inspector owns its displayed list until closed. Remote card moves
// still update the synchronized model and the combat pile counters underneath.
[HarmonyPatch(typeof(NCardPileScreen), "OnPileContentsChanged")]
internal static class DemoInspection
{
    private sealed class Snapshot { }
    private static readonly ConditionalWeakTable<NCardPileScreen, Snapshot> Opened = new();
    static bool Prefix(NCardPileScreen __instance)
    {
        if (!DemoController.IsViewing) return true;
        if (Opened.TryGetValue(__instance, out _)) return false;
        Opened.Add(__instance, new());
        return true;
    }
}
