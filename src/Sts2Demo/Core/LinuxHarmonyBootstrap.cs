using System.Runtime.InteropServices;

namespace Sts2Demo.Core;

/// <summary>Make the system unwind provider visible to MonoMod's Linux exception helper.</summary>
internal static class LinuxHarmonyBootstrap
{
    internal const int GlobalNow = 0x102; // glibc RTLD_NOW | RTLD_GLOBAL
    internal const string UnwindSymbol = "_Unwind_RaiseException";
    private static nint _provider;

    internal interface ILinker
    {
        nint Open(string name, int flags);
        nint Symbol(nint handle, string name);
        void Close(nint handle);
        string Error();
    }

    public static string Prepare()
    {
        if (!OperatingSystem.IsLinux()) return "not needed on this platform";
        if (_provider != 0) return "Linux unwind provider already prepared";
        var loaded = LoadProvider(new GlibcLinker());
        // Intentionally retain for process lifetime. Later Harmony patches use these symbols too.
        _provider = loaded.Handle;
        return $"Linux unwind provider: {loaded.Name} (RTLD_NOW | RTLD_GLOBAL)";
    }

    internal static (nint Handle, string Name) LoadProvider(ILinker linker)
    {
        var errors = new List<string>();
        foreach (var name in new[] { "libgcc_s.so.1", "libunwind.so.1", "libunwind.so.8", "libunwind.so" })
        {
            nint handle = linker.Open(name, GlobalNow);
            if (handle == 0) { errors.Add($"{name}: {linker.Error()}"); continue; }
            if (linker.Symbol(handle, UnwindSymbol) != 0 && linker.Symbol(0, UnwindSymbol) != 0)
                return (handle, name);
            errors.Add($"{name}: {UnwindSymbol} is not exported globally");
            linker.Close(handle);
        }
        throw new DllNotFoundException("Friend View cannot expose the system unwind library to Harmony/MonoMod. "
            + "No system files were changed. " + string.Join("; ", errors));
    }

    private sealed class GlibcLinker : ILinker
    {
        public nint Open(string name, int flags) => Dlopen(name, flags);
        public nint Symbol(nint handle, string name) => Dlsym(handle, name);
        public void Close(nint handle) => Dlclose(handle);
        public string Error() => Marshal.PtrToStringUTF8(Dlerror()) ?? "dlopen failed";

        [DllImport("libdl.so.2", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint Dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);
        [DllImport("libdl.so.2", EntryPoint = "dlsym", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint Dlsym(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport("libdl.so.2", EntryPoint = "dlclose", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Dlclose(nint handle);
        [DllImport("libdl.so.2", EntryPoint = "dlerror", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint Dlerror();
    }
}
