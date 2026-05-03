using System;
using System.Runtime.InteropServices;

namespace RotomMindmap.Services;

public static class WindowsShellService
{
    private const uint SeeMaskInvokeIdList = 12;
    private const int ShowNormal = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo execInfo);

    public static void OpenDirectory(string absoluteDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(absoluteDirectoryPath))
        {
            throw new ArgumentException("Directory path must not be empty.", nameof(absoluteDirectoryPath));
        }

        var execInfo = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            lpVerb = "open",
            lpFile = absoluteDirectoryPath,
            nShow = ShowNormal
        };

        if (!ShellExecuteEx(ref execInfo))
        {
            throw new InvalidOperationException($"Failed to open directory: {Marshal.GetLastWin32Error()}");
        }
    }

    public static void RevealInExplorer(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            throw new ArgumentException("Path must not be empty.", nameof(absolutePath));
        }

        var execInfo = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskInvokeIdList,
            lpVerb = "open",
            lpFile = "explorer.exe",
            lpParameters = $"/select,\"{absolutePath}\"",
            nShow = ShowNormal
        };

        if (!ShellExecuteEx(ref execInfo))
        {
            throw new InvalidOperationException($"Failed to reveal path in Explorer: {Marshal.GetLastWin32Error()}");
        }
    }
}
