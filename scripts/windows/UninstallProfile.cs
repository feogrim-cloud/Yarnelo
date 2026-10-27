using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Uninstall reads the saved profile without constructing Store: that constructor
// repairs and saves settings. Restoring here is copy-only and leaves the entire
// profile/library intact, including when uninstall is cancelled or retried.
internal static class UninstallProfile
{
    [DataContract]
    private sealed class Profile
    {
        [DataMember(IsRequired = true)] public List<Item> Items { get; set; }
    }

    [DataContract]
    private sealed class Item
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Path { get; set; }
        [DataMember] public string OriginalPath { get; set; }
        [DataMember] public bool ManagedShortcut { get; set; }
        [DataMember] public bool DesktopSourceMoved { get; set; }
        [DataMember] public bool PendingSourceDelete { get; set; }
        [DataMember] public bool PendingPublicMove { get; set; }
    }

    private sealed class Restoration
    {
        internal string Original;
        internal byte[] Bytes;
    }

    internal static int RestoreForExecutable(string executable)
    {
        return Restore(ProfileDirectoryForExecutable(executable,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
    }

    internal static string ProfileDirectoryForExecutable(string executable, string localAppData)
    {
        string application = System.IO.Path.GetDirectoryName(FullPath(executable));
        string marker = System.IO.Path.Combine(application, "portable.mode");
        RejectReparsePoints(marker);
        bool portable = Exists(marker);
        if (portable && (File.GetAttributes(marker) & FileAttributes.Directory) != 0)
            throw new IOException("The portable-mode marker is not an ordinary file.");
        return portable
            ? System.IO.Path.Combine(application, "Data")
            : System.IO.Path.Combine(FullPath(localAppData), "PolkaDock");
    }

    // Overrides are internal code seams for isolated fixtures, never CLI options.
    internal static int Restore(string profileDirectory, string userDesktop, string publicDesktop)
    {
        string profile = FullPath(profileDirectory);
        string library = System.IO.Path.Combine(profile, "Library");
        string desktop = OptionalPath(userDesktop);
        string common = OptionalPath(publicDesktop);
        RejectReparsePoints(profile);
        RejectReparsePoints(library);
        Profile saved = ReadProfile(profile, library);
        if (saved == null) return 0;

        // Validate and read every required shortcut before creating any desktop
        // copy, so a missing or untrusted item prevents partial preparation.
        var restorations = new List<Restoration>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Item item in saved.Items)
        {
            if (item == null) throw new IOException("The shortcut profile contains an invalid item.");
            if (!item.ManagedShortcut || (!item.DesktopSourceMoved && !item.PendingSourceDelete && !item.PendingPublicMove)) continue;
            Guid identity;
            if (!Guid.TryParseExact(item.Id, "N", out identity) || !identities.Add(item.Id))
                throw new IOException("The shortcut profile contains an invalid managed ID.");
            string source = FullPath(item.Path);
            string original = FullPath(item.OriginalPath);
            string extension = System.IO.Path.GetExtension(source);
            if ((!Equal(extension, ".lnk") && !Equal(extension, ".url")) ||
                !Equal(System.IO.Path.GetDirectoryName(source), library) ||
                !Equal(System.IO.Path.GetFileNameWithoutExtension(source), item.Id) ||
                !Equal(System.IO.Path.GetExtension(original), extension))
                throw new IOException("A managed shortcut is outside its owned library.");
            string parent = System.IO.Path.GetDirectoryName(original);
            if ((!Equal(parent, desktop) && !Equal(parent, common)) ||
                System.IO.Path.GetFileName(original).IndexOf(':') >= 0)
                throw new IOException("A shortcut's original location is not a desktop folder.");
            RejectReparsePoints(original);
            using (SafeFileHandle directory = OpenDirectory(parent)) { }
            restorations.Add(new Restoration { Original = original, Bytes = ReadBytes(source) });
        }
        foreach (Restoration restoration in restorations) RestoreBytes(restoration.Bytes, restoration.Original);
        return restorations.Count;
    }

    private static Profile ReadProfile(string profile, string library)
    {
        bool found = false;
        Exception failure = null;
        foreach (string name in new[] { "settings.json", "settings.json.bak" })
        {
            string path = System.IO.Path.Combine(profile, name);
            RejectReparsePoints(path);
            if (!Exists(path)) continue;
            found = true;
            try
            {
                byte[] bytes = ReadBytes(path);
                using (var input = new MemoryStream(bytes, false))
                {
                    var serializer = new DataContractJsonSerializer(typeof(Profile));
                    Profile saved = serializer.ReadObject(input) as Profile;
                    if (saved == null || saved.Items == null || saved.Items.Count > 1000)
                        throw new SerializationException("The shortcut list is missing or invalid.");
                    return saved;
                }
            }
            catch (IOException error) { failure = error; }
            catch (UnauthorizedAccessException error) { failure = error; }
            catch (SerializationException error) { failure = error; }
        }
        if (found) throw new IOException("No readable shortcut profile or backup is available. Uninstall was stopped.", failure);
        if (Exists(library))
        {
            using (SafeFileHandle directory = OpenDirectory(library))
                using (IEnumerator<string> contents = Directory.EnumerateFileSystemEntries(library).GetEnumerator())
                    if (contents.MoveNext()) throw new IOException("The managed library is not empty, but its profile is missing. Uninstall was stopped.");
        }
        return null;
    }

    private static void RestoreBytes(byte[] bytes, string original)
    {
        string directory = System.IO.Path.GetDirectoryName(original);
        string stem = System.IO.Path.GetFileNameWithoutExtension(original);
        string extension = System.IO.Path.GetExtension(original);
        using (SafeFileHandle folder = OpenDirectory(directory))
        {
            // Recognize a previous cancelled/retried restoration, including a
            // numbered copy created because the original name was occupied.
            foreach (string candidate in Directory.EnumerateFileSystemEntries(directory))
            {
                string name = System.IO.Path.GetFileName(candidate);
                bool matches = Equal(name, System.IO.Path.GetFileName(original));
                if (!matches && name.StartsWith(stem + " (", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(")" + extension, StringComparison.OrdinalIgnoreCase))
                {
                    int number;
                    string numberText = name.Substring(stem.Length + 2, name.Length - stem.Length - extension.Length - 3);
                    matches = Int32.TryParse(numberText, out number) && number >= 2 && number <= 10000;
                }
                if (!matches) continue;
                RejectReparsePoints(candidate);
                if ((File.GetAttributes(candidate) & FileAttributes.Directory) != 0) continue;
                if (SameBytes(bytes, ReadBytes(candidate))) return;
            }
            for (int number = 1; number <= 10000; number++)
            {
                string target = number == 1 ? original : System.IO.Path.Combine(directory, stem + " (" + number + ")" + extension);
                RejectReparsePoints(target);
                if (Exists(target)) continue;
                SafeFileHandle handle = CreateFile(target, 0xc0000000u, 0, IntPtr.Zero, 1, 0x00200000u, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    if (error == 80 || error == 183) continue; // A concurrent creator owns this name.
                    throw NativeError("Could not create a restored desktop shortcut.", error);
                }
                try
                {
                    VerifyHandle(handle, target, false);
                    using (var stream = new FileStream(handle, FileAccess.ReadWrite))
                    {
                        handle = null;
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                }
                finally { if (handle != null) handle.Dispose(); }
                return;
            }
            throw new IOException("No free desktop shortcut name is available.");
        }
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
        return true;
    }

    private static byte[] ReadBytes(string path)
    {
        RejectReparsePoints(path);
        SafeFileHandle handle = CreateFile(path, 0x80000000u, 1, IntPtr.Zero, 3, 0x00200000u, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            throw NativeError("Could not read a preserved shortcut or profile.", error);
        }
        try
        {
            VerifyHandle(handle, path, false);
            using (var stream = new FileStream(handle, FileAccess.Read))
            {
                handle = null;
                if (stream.Length > 4 * 1024 * 1024) throw new IOException("A preserved file is too large.");
                byte[] bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read == 0) throw new IOException("A preserved file changed while being read.");
                    offset += read;
                }
                return bytes;
            }
        }
        finally { if (handle != null) handle.Dispose(); }
    }

    private static SafeFileHandle OpenDirectory(string path)
    {
        RejectReparsePoints(path);
        SafeFileHandle handle = CreateFile(path, 0x80000000u, 3, IntPtr.Zero, 3, 0x02200000u, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            throw NativeError("Could not verify a desktop or profile folder.", error);
        }
        try { VerifyHandle(handle, path, true); return handle; }
        catch { handle.Dispose(); throw; }
    }

    private static void VerifyHandle(SafeFileHandle handle, string path, bool directory)
    {
        FileInformation information;
        if (!GetFileInformationByHandle(handle, out information)) throw NativeError("Could not verify preserved file properties.", Marshal.GetLastWin32Error());
        if ((information.Attributes & 0x400u) != 0 || ((information.Attributes & 0x10u) != 0) != directory ||
            (!directory && (information.Links != 1 || information.SizeHigh != 0 || information.SizeLow > 4 * 1024 * 1024)))
            throw new IOException("A preserved file or folder has unsupported links or properties.");
        var resolved = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandle(handle, resolved, (uint)resolved.Capacity, 0);
        if (length == 0 || length >= resolved.Capacity) throw new IOException("Could not verify the final preserved file path.");
        string actual = resolved.ToString();
        if (actual.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) actual = "\\\\" + actual.Substring(8);
        else if (actual.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase)) actual = actual.Substring(4);
        if (!Equal(FullPath(actual), FullPath(path))) throw new IOException("A preserved file path was redirected.");
    }

    private static string FullPath(string path)
    {
        if (String.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path) || path.IndexOf('\0') >= 0 ||
            (!(path.Length >= 3 && Char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) &&
             !path.StartsWith("\\\\", StringComparison.Ordinal)) ||
            path.StartsWith("\\\\?\\", StringComparison.Ordinal) || path.StartsWith("\\\\.\\", StringComparison.Ordinal))
            throw new IOException("An absolute ordinary file path is required.");
        string result = System.IO.Path.GetFullPath(path);
        string root = System.IO.Path.GetPathRoot(result);
        return result.Length > root.Length ? result.TrimEnd('\\', '/') : result;
    }

    private static string OptionalPath(string path) { return String.IsNullOrWhiteSpace(path) ? null : FullPath(path); }
    private static bool Equal(string left, string right) { return left != null && right != null && String.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    private static bool Exists(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
    private static void RejectReparsePoints(string path)
    {
        for (string current = FullPath(path); !String.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("A restore path contains a reparse point."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static IOException NativeError(string message, int error) { return new IOException(message + " " + new Win32Exception(error).Message); }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
