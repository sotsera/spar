using System.Runtime.InteropServices;

namespace Sotsera.Spar.Windows;

public static class SparApplication
{
    /// <summary>Runs one window and its STA message pump on the calling thread until shutdown completes.</summary>
    /// <remarks>Call from an STA entry point. Only one window may run on that thread at a time.</remarks>
    public static void Run(SparWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Html);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.UserDataFolder);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Width, 320);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Height, 240);

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("The initial Spar native host supports Windows x64 only.");
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("Run Spar from an STA entry point, marked with [STAThread].");
        }

        SparWindow.Run(options);
    }
}
