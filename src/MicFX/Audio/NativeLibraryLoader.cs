using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MicFX.Audio;

/// <summary>
/// Loads the native RNNoise library that is embedded in this assembly.
///
/// Shipping it as a loose file next to the exe does not survive the
/// single-file portable build or the installer (both distribute MicFX.exe
/// alone), so the DLL is embedded as a resource and unpacked on first use into
/// %LOCALAPPDATA%\MicFX\native. A DllImportResolver then points the
/// DllImport("rnnoise") declarations at the unpacked copy. When no resource is
/// present (developer builds), the resolver stands aside and the default
/// probing rules apply, so a library sitting next to the binary still works.
/// </summary>
internal static class NativeLibraryLoader
{
    private const string ResourceName = "MicFX.rnnoise.dll";
    private const string ImportName = "rnnoise";

    private static readonly object gate = new();
    private static bool initialized;
    private static IntPtr handle;

    /// <summary>Why loading failed, for the status bar. Null when it worked or was never attempted.</summary>
    public static string? LoadError { get; private set; }

    public static void EnsureRegistered()
    {
        lock (gate)
        {
            if (initialized) return;
            initialized = true;

            try
            {
                var assembly = typeof(NativeLibraryLoader).Assembly;
                string? path = Unpack(assembly);
                if (path != null)
                    handle = NativeLibrary.Load(path);

                NativeLibrary.SetDllImportResolver(assembly, (name, _, _) =>
                    name == ImportName ? handle : IntPtr.Zero);
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
            }
        }
    }

    private static string? Unpack(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null) return null; // developer build: fall back to default probing

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicFX", "native");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "rnnoise.dll");

        // Rewrite only when missing or a different build, so upgrades refresh it
        // but ordinary launches do not touch a file that may already be loaded.
        if (!File.Exists(path) || new FileInfo(path).Length != stream.Length)
        {
            string temp = $"{path}.{Environment.ProcessId}.tmp";
            using (var file = File.Create(temp))
                stream.CopyTo(file);
            try
            {
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                // Another MicFX instance already has that copy loaded and locked;
                // its file is the same build, so just use it.
                try { File.Delete(temp); } catch { }
            }
        }
        return File.Exists(path) ? path : null;
    }
}
