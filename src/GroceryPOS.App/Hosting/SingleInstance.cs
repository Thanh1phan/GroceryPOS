using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GroceryPOS.App.Hosting;

/// <summary>
/// One running copy per data folder (two copies would fight over parked orders, the automatic backup and the receipt
/// printer). A second start brings the running window to the front instead. A fixed, data-independent mutex is held
/// as well so the installer (<c>AppMutex</c> in installer/GroceryPOS.iss) can ask the user to close the app before upgrading.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>Must match <c>AppMutex</c> in installer/GroceryPOS.iss.</summary>
    public const string InstallerMutexName = "GroceryPOS.App.Running";

    private readonly Mutex _instance;
    private readonly Mutex _installer;
    private bool _disposed;

    private SingleInstance(Mutex instance, Mutex installer)
    {
        _instance = instance;
        _installer = installer;
    }

    /// <summary>Passed by a copy that restarts itself (after a restore) so the new copy waits for the old one to exit.</summary>
    public const string RestartArgument = "--restarted";

    /// <summary>
    /// Returns null when another copy already uses <paramref name="dataDirectory"/> (that copy is activated).
    /// <paramref name="waitForRelease"/> lets a restarted copy wait for its predecessor to exit.
    /// </summary>
    public static SingleInstance? TryAcquire(string dataDirectory, TimeSpan waitForRelease = default)
    {
        var instance = new Mutex(initiallyOwned: false, MutexName(dataDirectory));
        bool owned;
        try
        {
            owned = instance.WaitOne(waitForRelease);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // the previous copy ended without releasing it (Environment.Exit, crash) – it is ours now
        }

        if (!owned)
        {
            instance.Dispose();
            ActivateOtherInstance();
            return null;
        }

        return new SingleInstance(instance, new Mutex(initiallyOwned: false, InstallerMutexName));
    }

    /// <summary>Mutex name derived from the (case-insensitive) data folder, so portable copies on a USB stick do not block each other.</summary>
    public static string MutexName(string dataDirectory)
    {
        var normalized = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)), 0, 8);
        return $"GroceryPOS.Instance.{hash}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _instance.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from another thread or already abandoned – nothing to do on exit.
        }

        _instance.Dispose();
        _installer.Dispose();
    }

    private static void ActivateOtherInstance()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id || process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                if (NativeMethods.IsIconic(process.MainWindowHandle))
                {
                    NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SwRestore);
                }

                NativeMethods.SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }
    }

    private static class NativeMethods
    {
        public const int SwRestore = 9;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);
    }
}
