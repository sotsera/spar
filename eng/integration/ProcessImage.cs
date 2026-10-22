using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Spar.Integration;

public static class ProcessImage
{
    public static string Read(Process process)
    {
        // Module enumeration depends on the child loader having run. Image identity does not.
        var image = new StringBuilder(32768);
        var length = image.Capacity;
        if (QueryFullProcessImageNameW(process.SafeHandle, 0, image, ref length))
        {
            return image.ToString();
        }

        var error = Marshal.GetLastWin32Error();
        if (process.HasExited)
        {
            return null;
        }

        throw new Win32Exception(error, $"Cannot query live owned process {process.Id} image.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        SafeProcessHandle process, int flags, StringBuilder image, ref int length);
}
