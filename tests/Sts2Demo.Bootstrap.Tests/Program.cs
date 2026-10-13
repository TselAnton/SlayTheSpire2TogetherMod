using Sts2Demo.Core;

int assertions = 0;
void Check(bool condition, string description)
{
    assertions++;
    if (!condition) throw new Exception(description);
}

var linker = new FakeLinker { Available = "libgcc_s.so.1" };
var result = LinuxHarmonyBootstrap.LoadProvider(linker);
Check(result.Name == "libgcc_s.so.1" && result.Handle != 0, "Prefer system libgcc");
Check(linker.Flags.Single() == 0x102, "Use GLOBAL, not default LOCAL loading");
Check(linker.Closed.Count == 0, "Successful handle stays loaded");
Check(linker.Symbols.Contains((0, LinuxHarmonyBootstrap.UnwindSymbol)), "Verify symbol in global namespace");

linker = new FakeLinker { Available = "libunwind.so.1" };
result = LinuxHarmonyBootstrap.LoadProvider(linker);
Check(result.Name == "libunwind.so.1" && linker.Opened.Count == 2, "Fallback to system libunwind");

linker = new FakeLinker { Available = "libgcc_s.so.1", Exported = false };
try { LinuxHarmonyBootstrap.LoadProvider(linker); throw new Exception("Missing export accepted"); }
catch (DllNotFoundException e)
{
    Check(e.Message.Contains(LinuxHarmonyBootstrap.UnwindSymbol), "Actionable missing-symbol diagnostic");
    Check(linker.Closed.Count == 1, "Release unsuccessful handle");
}

linker = new FakeLinker { Available = "libgcc_s.so.1", Global = false };
try { LinuxHarmonyBootstrap.LoadProvider(linker); throw new Exception("Local-only symbol accepted"); }
catch (DllNotFoundException) { Check(linker.Closed.Count == 1, "Reject symbol visible only in local handle"); }

string status = LinuxHarmonyBootstrap.Prepare();
Check(OperatingSystem.IsLinux() ? status.Contains("RTLD_GLOBAL") : status == "not needed on this platform", "Real platform guard/native provider");
Check(OperatingSystem.IsLinux() ? LinuxHarmonyBootstrap.Prepare().Contains("already prepared") : LinuxHarmonyBootstrap.Prepare() == status, "Idempotent preparation");
var own = new ProfileMod("Sts2Demo", true, false, false);
Check(ProfilePolicy.UseDefault(true, [own], "Sts2Demo"), "Only cosmetic Friend View uses default profile");
Check(!ProfilePolicy.UseDefault(false, [own], "Sts2Demo"), "Never redirect during initialization");
Check(!ProfilePolicy.UseDefault(true, [], "Sts2Demo"), "No mods never trigger override");
Check(!ProfilePolicy.UseDefault(true, [own, new("Other", true, false, false)], "Sts2Demo"), "Even another cosmetic mod retains isolation");
Check(!ProfilePolicy.UseDefault(true, [own with { Loaded = false }], "Sts2Demo"), "Failed/loading self retains isolation");
Check(!ProfilePolicy.UseDefault(true, [own with { HasErrors = true }], "Sts2Demo"), "Self initialization errors retain isolation");
Check(!ProfilePolicy.UseDefault(true, [own with { AffectsGameplay = true }], "Sts2Demo"), "Gameplay flag retains isolation");
Check(!ProfilePolicy.UseDefault(true, [own with { Id = "Other" }], "Sts2Demo"), "Other sole mod cannot get default profile");
var selection = new ProfileSelection();
Check(!selection.UseDefault(false, [own], "Sts2Demo") && selection.UseDefault(true, [own], "Sts2Demo"), "Early calls do not freeze a premature decision");
Check(selection.UseDefault(true, [own, new("Other", true, false, false)], "Sts2Demo"), "Runtime mod detection cannot redirect paths mid-session");
selection = new ProfileSelection();
Check(!selection.UseDefault(true, [own, new("Other", true, false, false)], "Sts2Demo"), "New session with other mod retains protection");
Check(!selection.UseDefault(true, [own], "Sts2Demo"), "Protected session never changes paths either");
Console.WriteLine($"PASS: {assertions} bootstrap/profile assertions; {status}.");

sealed class FakeLinker : LinuxHarmonyBootstrap.ILinker
{
    public string Available = "";
    public bool Exported = true;
    public bool Global = true;
    public List<string> Opened = [];
    public List<int> Flags = [];
    public List<nint> Closed = [];
    public List<(nint, string)> Symbols = [];
    public nint Open(string name, int flags) { Opened.Add(name); Flags.Add(flags); return name == Available ? 42 : 0; }
    public nint Symbol(nint handle, string name) { Symbols.Add((handle, name)); return Exported && (handle != 0 || Global) ? 99 : 0; }
    public void Close(nint handle) => Closed.Add(handle);
    public string Error() => "library unavailable";
}
