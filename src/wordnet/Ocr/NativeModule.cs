using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WordNet.Ocr;

internal sealed class NativeModule : IDisposable
{
    private IntPtr handle;

    internal NativeModule(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Ocr.NativeLibrary: Provision Tesseract 5 for the process architecture.", path);
#if NET8_0_OR_GREATER
        handle = NativeLibrary.Load(path, typeof(NativeModule).Assembly, DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories);
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) handle = LoadLibraryEx(path, IntPtr.Zero, 0x1100);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) handle = MacOpen(Utf8(path), 2);
        else handle = LinuxOpen(Utf8(path), 2);
        if (handle == IntPtr.Zero) throw new DllNotFoundException("Ocr.NativeLibrary: Tesseract or a native dependency could not be loaded.");
#endif
    }

    internal T Function<T>(string name) where T : Delegate
    {
#if NET8_0_OR_GREATER
        IntPtr address = NativeLibrary.GetExport(handle, name);
#else
        IntPtr address = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? GetProcAddress(handle, Utf8(name))
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacSymbol(handle, Utf8(name)) : LinuxSymbol(handle, Utf8(name));
        if (address == IntPtr.Zero) throw new EntryPointNotFoundException("Ocr.NativeApi: Missing Tesseract 5 C API symbol: " + name);
#endif
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    internal static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
#if NET8_0_OR_GREATER
        NativeLibrary.Free(handle);
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) _ = FreeLibrary(handle);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) _ = MacClose(handle);
        else _ = LinuxClose(handle);
#endif
        handle = IntPtr.Zero;
    }

#if !NET8_0_OR_GREATER
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetProcAddress(IntPtr module, byte[] name);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr LinuxOpen(byte[] path, int flags);
    [DllImport("libdl.so.2", EntryPoint = "dlsym")]
    private static extern IntPtr LinuxSymbol(IntPtr handle, byte[] name);
    [DllImport("libdl.so.2", EntryPoint = "dlclose")]
    private static extern int LinuxClose(IntPtr handle);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen")]
    private static extern IntPtr MacOpen(byte[] path, int flags);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlsym")]
    private static extern IntPtr MacSymbol(IntPtr handle, byte[] name);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlclose")]
    private static extern int MacClose(IntPtr handle);
#endif
}
