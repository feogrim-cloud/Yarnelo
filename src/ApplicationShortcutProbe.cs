using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Polka
{
    internal static partial class Native
    {
        // Offline and recall-on-access entries can be present in a directory but
        // unavailable until Windows reconnects the volume or cloud provider.
        const uint UnsafeTargetAttributes = 0x00000400 | 0x00001000 | 0x00040000 | 0x00400000;
        const uint InvalidFileAttributes = 0xFFFFFFFF;
        const uint DirectoryAttribute = 0x10;

        enum LocalTarget { Uncertain, Missing, File, Directory }

        // Returns evidence for the dock's delayed, repeated confirmation. No
        // shell resolution, target execution or filesystem mutation takes place.
        internal static string FindMissingApplication(string shortcutPath)
        {
            try
            {
                string source = PlainDosPath(shortcutPath);
                if (source == null) return null;
                string extension = Path.GetExtension(source);
                uint volumeSerial;
                if (String.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                    return InspectLocalTarget(source, out volumeSerial) == LocalTarget.Missing
                        ? ApplicationIdentity(source, volumeSerial)
                        : null;

                bool isLink = String.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase);
                bool isUrl = String.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase);
                if ((!isLink && !isUrl) || InspectLocalTarget(source, out volumeSerial) != LocalTarget.File)
                    return null;
                uint sourceSerial = volumeSerial;
                byte[] bytes = Store.ReadVerifiedShortcut(source);
                if (isUrl) return FindUninstalledSteamApplication(bytes);

                uint originalSerial;
                string relative;
                string target = ReadLocalExecutableLink(bytes, out originalSerial, out relative);
                if (target == null || InspectLocalTarget(target, out volumeSerial) != LocalTarget.Missing ||
                    volumeSerial != originalSerial) return null;
                if (!String.IsNullOrEmpty(relative))
                {
                    // A relative target can keep the same link working after
                    // the original absolute installation path disappears.
                    if (Path.IsPathRooted(relative)) return null;
                    string alternative = PlainDosPath(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), relative)));
                    uint alternativeSerial;
                    if (alternative == null ||
                        (!String.Equals(alternative, target, StringComparison.OrdinalIgnoreCase) &&
                            (InspectLocalTarget(alternative, out alternativeSerial) != LocalTarget.Missing ||
                                alternativeSerial != sourceSerial))) return null;
                }
                return ApplicationIdentity(target, volumeSerial) + "|" + sourceSerial.ToString("X8", CultureInfo.InvariantCulture) +
                    "|" + ShortcutRevision(bytes);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (Win32Exception) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        static string ApplicationIdentity(string path, uint serial)
        {
            return "exe|" + serial.ToString("X8", CultureInfo.InvariantCulture) + "|" + path;
        }

        static string ShortcutRevision(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        static string PlainDosPath(string path)
        {
            if (String.IsNullOrEmpty(path) || path.Length < 3 || path.Length >= 260 ||
                !((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')) ||
                path[1] != ':' || (path[2] != '\\' && path[2] != '/')) return null;
            path = path.Replace('/', '\\');
            if (path.Length > 3 && path[path.Length - 1] == '\\') path = path.TrimEnd('\\');
            if (path.IndexOfAny(new[] { ':', '*', '?', '\"', '<', '>', '|', '%' }, 2) >= 0) return null;
            foreach (char character in path) if (Char.IsControl(character)) return null;

            string absolute = Path.GetFullPath(path);
            if (!String.Equals(absolute, path, StringComparison.OrdinalIgnoreCase)) return null;
            if (path.Length == 3) return absolute;
            foreach (string segment in path.Substring(3).Split('\\'))
            {
                if (segment.Length == 0 || segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.EndsWith(" ", StringComparison.Ordinal)) return null;
                string stem = segment.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    stem == "CONIN$" || stem == "CONOUT$" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                        stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                        "0123456789¹²³".IndexOf(stem[3]) >= 0)) return null;
            }
            return absolute;
        }

        static LocalTarget InspectLocalTarget(string absolute, out uint serial, bool allowMissingParent = true)
        {
            serial = 0;
            string root = absolute.Substring(0, 3);
            var device = new StringBuilder(2048);
            // GetDriveType alone can report SUBST drives as fixed. Ignore
            // mapped letters and redirected devices before filesystem access.
            if (QueryApplicationDriveDevice(root.Substring(0, 2), device, device.Capacity) == 0 ||
                !device.ToString().StartsWith(@"\Device\HarddiskVolume", StringComparison.OrdinalIgnoreCase) ||
                GetApplicationDriveType(root) != 3) return LocalTarget.Uncertain;

            uint maximumComponentLength, flags;
            if (!GetApplicationVolumeInformation(root, IntPtr.Zero, 0, out serial, out maximumComponentLength,
                out flags, IntPtr.Zero, 0) || serial == 0) return LocalTarget.Uncertain;
            uint rootAttributes = GetApplicationFileAttributes(root);
            if (rootAttributes == InvalidFileAttributes || (rootAttributes & UnsafeTargetAttributes) != 0 ||
                (rootAttributes & DirectoryAttribute) == 0) return LocalTarget.Uncertain;
            if (absolute.Length == 3) return LocalTarget.Directory;

            // Walk from the verified volume downwards. Check each parent for
            // junctions and offline data before the OS can follow it.
            int boundary = 3;
            while (boundary < absolute.Length)
            {
                int next = absolute.IndexOf('\\', boundary);
                bool terminal = next < 0;
                string component = terminal ? absolute : absolute.Substring(0, next);
                uint attributes = GetApplicationFileAttributes(component);
                if (attributes == InvalidFileAttributes)
                {
                    // PATH_NOT_FOUND could mean that an already checked
                    // ancestor just changed. Only FILE_NOT_FOUND is evidence.
                    return (terminal || allowMissingParent) && Marshal.GetLastWin32Error() == 2
                        ? LocalTarget.Missing : LocalTarget.Uncertain;
                }
                if ((attributes & UnsafeTargetAttributes) != 0) return LocalTarget.Uncertain;
                bool directory = (attributes & DirectoryAttribute) != 0;
                if (terminal) return directory ? LocalTarget.Directory : LocalTarget.File;
                if (!directory) return LocalTarget.Uncertain;
                boundary = next + 1;
            }
            return LocalTarget.Uncertain;
        }

        // MS-SHLLINK LinkInfo holds an explicit local path and its volume ID.
        // ID-list-only, advertised MSI and expandable links need shell
        // semantics, so their intent cannot be verified by this reader.
        static string ReadLocalExecutableLink(byte[] bytes, out uint serial, out string relative)
        {
            serial = 0;
            relative = null;
            if (!CompleteShellLink(bytes)) return null;
            uint linkFlags = BitConverter.ToUInt32(bytes, 20);
            const uint ambiguous = 0x100 | 0x200 | 0x1000 | 0x02000000 | 0x04000000; // ForceNoLinkInfo, HasExpString, HasDarwinID, PreferEnvironmentPath, KeepLocalIDListForUNCTarget
            if ((linkFlags & 2) == 0 || (linkFlags & ambiguous) != 0 ||
                (BitConverter.ToUInt32(bytes, 24) & (DirectoryAttribute | UnsafeTargetAttributes)) != 0)
                return null;

            int start = 76;
            if ((linkFlags & 1) != 0) start += 2 + BitConverter.ToUInt16(bytes, start);
            int end = start + (int)BitConverter.ToUInt32(bytes, start);
            uint header = BitConverter.ToUInt32(bytes, start + 4);
            if (BitConverter.ToUInt32(bytes, start + 8) != 1) return null;
            uint volumeOffset = BitConverter.ToUInt32(bytes, start + 12);
            if (volumeOffset < header || volumeOffset > end - start - 16) return null;
            int volume = start + (int)volumeOffset;
            uint volumeSize = BitConverter.ToUInt32(bytes, volume);
            if (volumeSize < 16 || volumeSize > end - volume || BitConverter.ToUInt32(bytes, volume + 4) != 3)
                return null;
            serial = BitConverter.ToUInt32(bytes, volume + 8);
            if (serial == 0) return null;

            uint baseOffset = BitConverter.ToUInt32(bytes, start + 16);
            uint suffixOffset = BitConverter.ToUInt32(bytes, start + 24);
            bool unicode = header >= 36 && BitConverter.ToUInt32(bytes, start + 28) != 0;
            if (unicode) baseOffset = BitConverter.ToUInt32(bytes, start + 28);
            bool unicodeSuffix = header >= 36 && BitConverter.ToUInt32(bytes, start + 32) != 0;
            if (unicodeSuffix) suffixOffset = BitConverter.ToUInt32(bytes, start + 32);
            uint minimum = Math.Max(header, volumeOffset + volumeSize);
            string basePath = ReadLinkInfoString(bytes, start, end, baseOffset, minimum, unicode);
            string suffix = ReadLinkInfoString(bytes, start, end, suffixOffset, minimum, unicodeSuffix);
            if (String.IsNullOrEmpty(basePath) || suffix == null) return null;

            if ((linkFlags & 8) != 0)
            {
                // CompleteShellLink already validated all StringData lengths.
                int cursor = end;
                int stride = (linkFlags & 128) != 0 ? 2 : 1;
                if ((linkFlags & 4) != 0) cursor += 2 + BitConverter.ToUInt16(bytes, cursor) * stride;
                int length = BitConverter.ToUInt16(bytes, cursor) * stride;
                if (length > 520) return null;
                if (stride == 2)
                    relative = new UnicodeEncoding(false, false, true).GetString(bytes, cursor + 2, length);
                else
                {
                    for (int index = cursor + 2; index < cursor + 2 + length; index++)
                        if (bytes[index] > 127) return null;
                    relative = Encoding.ASCII.GetString(bytes, cursor + 2, length);
                }
            }
            string target = PlainDosPath(basePath + suffix);
            return target != null && String.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase)
                ? target
                : null;
        }

        static string ReadLinkInfoString(byte[] bytes, int start, int end, uint offset, uint minimum, bool unicode)
        {
            if (offset < minimum || offset >= end - start) return null;
            int first = start + (int)offset;
            int stride = unicode ? 2 : 1;
            for (int cursor = first; cursor + stride <= end && cursor - first < 520; cursor += stride)
            {
                if (bytes[cursor] == 0 && (!unicode || bytes[cursor + 1] == 0))
                    return unicode
                        ? new UnicodeEncoding(false, false, true).GetString(bytes, first, cursor - first)
                        : Encoding.ASCII.GetString(bytes, first, cursor - first);
                // An ANSI-only link created under another Windows code page
                // could otherwise point to an incorrectly decoded filename.
                if (!unicode && bytes[cursor] > 127) return null;
            }
            return null;
        }


        static string FindUninstalledSteamApplication(byte[] shortcut)
        {
            string url;
            using (var stream = new MemoryStream(shortcut, false))
                url = ReadInternetShortcutTarget(stream);
            const string prefix = "steam://rungameid/";
            uint appId;
            if (url == null || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !UInt32.TryParse(url.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out appId) ||
                appId == 0) return null;
            string steamPath, id = appId.ToString(CultureInfo.InvariantCulture);

            // Missing registry records are inconclusive. Steam must explicitly
            // mark this app uninstalled, with no update or launch in progress.
            using (RegistryKey steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false))
            using (RegistryKey app = steam == null ? null : steam.OpenSubKey(@"Apps\" + id, false))
            {
                if (app == null || !Equals(app.GetValue("Installed"), 0) ||
                    !Equals(app.GetValue("Running", 0), 0) || !Equals(app.GetValue("Updating", 0), 0)) return null;
                steamPath = PlainDosPath(steam.GetValue("SteamPath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
            }
            if (steamPath == null) return null;
            string identity = ConfirmSteamLibraries(steamPath, id);
            return identity == null ? null : "steam|" + id + "|" + identity + "|" + ShortcutRevision(shortcut);
        }

        internal static string ConfirmSteamLibraries(string steamPath, string id)
        {
            uint serial;
            string steamExecutable = PlainDosPath(Path.Combine(steamPath, "steam.exe"));
            string config = PlainDosPath(Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"));
            if (steamExecutable == null || config == null ||
                InspectLocalTarget(steamExecutable, out serial) != LocalTarget.File ||
                InspectLocalTarget(config, out serial) != LocalTarget.File) return null;

            // Steam may temporarily mark a game uninstalled when its library
            // is disconnected. Require the client and all registered libraries
            // online, and corroborate the registry state using both disk
            // manifests and the library's app index.
            byte[] configBytes = Store.ReadVerifiedShortcut(config);
            if (configBytes.Length > 1024 * 1024) return null;
            string content;
            using (var stream = new MemoryStream(configBytes, false))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                content = reader.ReadToEnd();
            Dictionary<string, object> libraries = SteamLibraryVdf.Parse(content);
            if (libraries == null || libraries.Count == 0 || libraries.Count > 128) return null;
            var serials = new SortedDictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            bool containsPrimary = false;
            foreach (var library in libraries)
            {
                uint index;
                if (!UInt32.TryParse(library.Key, NumberStyles.None, CultureInfo.InvariantCulture, out index)) return null;
                var fields = library.Value as Dictionary<string, object>;
                object rawPath, rawIndex;
                if (fields == null || !fields.TryGetValue("path", out rawPath) || !fields.TryGetValue("apps", out rawIndex)) return null;
                var apps = rawIndex as Dictionary<string, object>;
                string root = PlainDosPath(rawPath as string);
                if (root == null || apps == null || apps.ContainsKey(id)) return null;
                if (index == 0)
                {
                    if (containsPrimary || !String.Equals(root, steamPath, StringComparison.OrdinalIgnoreCase)) return null;
                    containsPrimary = true;
                }
                string steamApps = PlainDosPath(Path.Combine(root, "steamapps"));
                string manifest = steamApps == null ? null : PlainDosPath(Path.Combine(steamApps, "appmanifest_" + id + ".acf"));
                if (manifest == null || InspectLocalTarget(steamApps, out serial) != LocalTarget.Directory || serials.ContainsKey(root))
                    return null;
                uint manifestSerial;
                if (InspectLocalTarget(manifest, out manifestSerial, false) != LocalTarget.Missing || manifestSerial != serial)
                    return null;
                serials.Add(root, serial);
            }
            if (!containsPrimary) return null;
            var evidence = new StringBuilder(ShortcutRevision(configBytes));
            foreach (var library in serials)
                evidence.Append('|').Append(library.Value.ToString("X8", CultureInfo.InvariantCulture)).Append(':').Append(library.Key);
            return evidence.ToString();
        }

        sealed class SteamLibraryVdf
        {
            readonly string data;
            int cursor;
            int entries;

            SteamLibraryVdf(string data) { this.data = data; }

            internal static Dictionary<string, object> Parse(string data)
            {
                var reader = new SteamLibraryVdf(data);
                if (!String.Equals(reader.ReadQuoted(), "libraryfolders", StringComparison.OrdinalIgnoreCase)) return null;
                var result = reader.ReadObject(0);
                reader.SkipWhitespace();
                return reader.cursor == data.Length ? result : null;
            }

            Dictionary<string, object> ReadObject(int depth)
            {
                SkipWhitespace();
                if (depth > 3 || cursor == data.Length || data[cursor++] != '{') return null;
                var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    SkipWhitespace();
                    if (cursor == data.Length) return null;
                    if (data[cursor] == '}') { cursor++; return values; }
                    string key = ReadQuoted();
                    if (key == null || values.ContainsKey(key) || ++entries > 12000) return null;
                    SkipWhitespace();
                    if (cursor == data.Length) return null;
                    object value = data[cursor] == '{' ? (object)ReadObject(depth + 1) : ReadQuoted();
                    if (value == null) return null;
                    values.Add(key, value);
                }
            }

            string ReadQuoted()
            {
                SkipWhitespace();
                if (cursor == data.Length || data[cursor++] != '"') return null;
                var result = new StringBuilder();
                while (cursor < data.Length && result.Length < 4096)
                {
                    char character = data[cursor++];
                    if (character == '"') return result.ToString();
                    if (character == '\\')
                    {
                        if (cursor == data.Length) return null;
                        character = data[cursor++];
                        if (character != '\\' && character != '"') return null;
                    }
                    if (Char.IsControl(character)) return null;
                    result.Append(character);
                }
                return null;
            }

            void SkipWhitespace()
            {
                while (cursor < data.Length)
                {
                    if (Char.IsWhiteSpace(data[cursor])) { cursor++; continue; }
                    if (cursor + 1 == data.Length || data[cursor] != '/' || data[cursor + 1] != '/') return;
                    while (cursor < data.Length && data[cursor] != '\n') cursor++;
                }
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint QueryApplicationDriveDevice(string name, StringBuilder target, int capacity);

        [DllImport("kernel32.dll", EntryPoint = "GetDriveTypeW", CharSet = CharSet.Unicode)]
        static extern uint GetApplicationDriveType(string root);

        [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetApplicationVolumeInformation(string root, IntPtr label, uint labelSize,
            out uint serialNumber, out uint maximumComponentLength, out uint flags, IntPtr filesystemName, uint nameSize);

        [DllImport("kernel32.dll", EntryPoint = "GetFileAttributesW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetApplicationFileAttributes(string path);
    }
}
