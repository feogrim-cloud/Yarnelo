using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

// Used only by Setup/Uninstall. It never starts a dock, changes a profile,
// terminates a process, or touches another application's startup entry.
internal static class SetupMaintenance
{
    private delegate bool EnumWindowProc(IntPtr window, IntPtr argument);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr argument);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder text, ref int capacity);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    private static string ProcessPath(int processId)
    {
        IntPtr handle = OpenProcess(0x1000, false, (uint)processId); // QUERY_LIMITED_INFORMATION
        if (handle == IntPtr.Zero) return null;
        try
        {
            int capacity = 32768;
            StringBuilder text = new StringBuilder(capacity);
            return QueryFullProcessImageName(handle, 0, text, ref capacity) ? text.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    private static bool EqualPath(string left, string right)
    {
        return left != null && string.Equals(Path.GetFullPath(left), right, StringComparison.OrdinalIgnoreCase);
    }

    private static int CloseOwnApplication(string executable)
    {
        List<int> ownProcesses = new List<int>();
        foreach (Process process in Process.GetProcessesByName("Yarnelo"))
        {
            using (process)
            {
                string path = ProcessPath(process.Id);
                // A same-name process that cannot be identified is not ours to close.
                if (EqualPath(path, executable)) ownProcesses.Add(process.Id);
            }
        }
        foreach (int processId in ownProcesses)
        {
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                if (owner != processId || !EqualPath(ProcessPath(processId), executable)) return true;
                StringBuilder title = new StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                if (title.ToString() != "Yarnelo — панель ярлыков") return true;
                IntPtr ignored;
                SendMessageTimeout(window, 0x0010, IntPtr.Zero, IntPtr.Zero, 0x0002, 2000, out ignored); // WM_CLOSE, ABORTIFHUNG
                return false;
            }, IntPtr.Zero);
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    if (!process.WaitForExit(15000)) return 50;
            }
            catch (ArgumentException) { /* Gracefully exited before lookup. */ }
        }
        return 0;
    }

    private static void RemoveOwnStartup(string executable)
    {
        RegistryView[] views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry32, RegistryView.Registry64 }
            : new[] { RegistryView.Registry32 };
        foreach (RegistryView view in views)
        using (RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
        using (RegistryKey key = user.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
            if (key == null) continue;
            foreach (string name in new[] { "Yarnelo", "Polka Desktop Dock" })
            {
                string command = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (command == null) continue;
                command = command.Trim();
                if (string.Equals(command, executable, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(command, "\"" + executable + "\"", StringComparison.OrdinalIgnoreCase))
                    key.DeleteValue(name, false);
            }
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || (args[0] != "close" && args[0] != "uninstall")) return 80;
            string executable = Path.GetFullPath(args[1]);
            if (!Path.IsPathRooted(args[1]) || !string.Equals(Path.GetFileName(executable), "Yarnelo.exe", StringComparison.OrdinalIgnoreCase)) return 80;
            int result = CloseOwnApplication(executable);
            if (result == 0 && args[0] == "uninstall") RemoveOwnStartup(executable);
            return result;
        }
        catch { return 80; }
    }
}
