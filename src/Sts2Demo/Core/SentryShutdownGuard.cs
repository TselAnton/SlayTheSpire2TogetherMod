using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;

namespace Sts2Demo.Core;

// v0.107.1 closes Sentry from an async startup continuation in process_frame.
// Native close() releases SentryGodotLogger while Godot is still iterating a
// copy of that signal's callbacks. Even Callable.IsValid then dereferences the
// freed logger. Keep the game's reporting policy and managed shutdown intact;
// only move native destruction past the current signal emission.
[HarmonyPatch(typeof(SentryService), nameof(SentryService.Shutdown))]
internal static class SentryShutdownGuard
{
    private static readonly MethodInfo NativeCall = AccessTools.Method(typeof(GodotObject),
        nameof(GodotObject.Call), [typeof(StringName), typeof(Variant[])]);
    private static readonly MethodInfo DeferredCall = AccessTools.Method(typeof(SentryShutdownGuard),
        nameof(DeferNativeShutdown));

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(NativeCall))
            {
                // Preserve labels/exception blocks and the Variant return stack slot.
                instruction.opcode = OpCodes.Call;
                instruction.operand = DeferredCall;
            }
            yield return instruction;
        }
    }

    internal static Variant DeferNativeShutdown(GodotObject target, StringName method, params Variant[] args)
    {
        if (method == "shutdown" && Engine.GetMainLoop() is SceneTree)
        {
            target.CallDeferred(method, args);
            return default;
        }
        return target.Call(method, args);
    }
}
