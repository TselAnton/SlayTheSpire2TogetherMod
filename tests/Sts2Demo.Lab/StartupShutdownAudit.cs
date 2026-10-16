using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2DemoLab;

// Opt-in: the normal lab used to have no crashpad backend and never exercised
// native Sentry teardown. Assert both sides of the actual patched game method.
[HarmonyPatch(typeof(SentryService), nameof(SentryService.Shutdown))]
internal static class StartupShutdownAudit
{
    static void Prefix(out bool __state)
    {
        __state = CommandLineHelper.GetValue("demo-startup-audit") == "on"
            && SentryService.IsEnabled;
        if (__state && !Engine.GetSingleton("SentrySDK").Call("is_enabled").AsBool())
            Log.Error("[StartupAudit] FAIL: native Sentry was not initialized; this run cannot validate shutdown.");
    }

    static void Postfix(bool __state)
    {
        if (!__state) return;
        var sdk = Engine.GetSingleton("SentrySDK");
        if (SentryService.IsEnabled || !sdk.Call("is_enabled").AsBool())
            Log.Error("[StartupAudit] FAIL: expected managed shutdown now and native shutdown deferred.");
        else
            Log.Info("[StartupAudit] PASS: managed reporting disabled; native logger retained through signal dispatch.");
        Callable.From(() =>
        {
            if (sdk.Call("is_enabled").AsBool())
                Log.Error("[StartupAudit] FAIL: native shutdown did not run after signal dispatch.");
            else
                Log.Info("[StartupAudit] PASS: native SDK shut down after signal dispatch.");
        }).CallDeferred();
    }
}
