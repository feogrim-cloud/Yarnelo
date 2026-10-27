using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Microsoft.Win32.SafeHandles;

namespace Polka
{
    [DataContract]
    public sealed class ThemeAppearance
    {
        [DataMember(Order = 0)] public string Theme { get; set; }
        [DataMember(Order = 1)] public double Opacity { get; set; }
        [DataMember(Order = 2)] public double CornerRadius { get; set; }
    }

    // Persisted theme data stays independent of WPF and the installed desktop.
    internal static class ThemeCatalog
    {
        internal static readonly string[] Ids = { "dusk", "frost", "graphite", "ember", "aurora" };
        internal static string Normalize(string theme)
        {
            string value = (theme ?? String.Empty).Trim().ToLowerInvariant();
            return Ids.Contains(value) ? value : "dusk";
        }
        internal static double Opacity(string theme)
        {
            switch (Normalize(theme)) { case "frost": return .76; case "graphite": return .96; case "ember": return .85; case "aurora": return .88; default: return .72; }
        }
        internal static double Corners(string theme)
        {
            switch (Normalize(theme)) { case "frost": return 22; case "graphite": return 12; case "ember": return 16; case "aurora": return 24; default: return 23; }
        }
        internal static List<ThemeAppearance> Copy(IEnumerable<ThemeAppearance> appearances)
        {
            return appearances == null ? new List<ThemeAppearance>() : appearances.Where(value => value != null).Select(value => new ThemeAppearance { Theme = value.Theme, Opacity = value.Opacity, CornerRadius = value.CornerRadius }).ToList();
        }
        internal static void Sanitize(DockSettings settings)
        {
            settings.Theme = Normalize(settings.Theme);
            var valid = new List<ThemeAppearance>();
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in settings.ThemeAppearances ?? new List<ThemeAppearance>())
            {
                if (value == null) continue;
                string id = (value.Theme ?? String.Empty).Trim().ToLowerInvariant();
                if (!Ids.Contains(id) || !known.Add(id)) continue;
                valid.Add(new ThemeAppearance { Theme = id,
                    Opacity = Store.IsFinite(value.Opacity) ? Math.Max(.15, Math.Min(1, value.Opacity)) : Opacity(id),
                    CornerRadius = Store.IsFinite(value.CornerRadius) ? Math.Max(0, Math.Min(100, value.CornerRadius)) : Corners(id) });
                if (valid.Count == Ids.Length) break;
            }
            settings.ThemeAppearances = valid;
        }
        internal static void Remember(DockSettings settings)
        {
            Sanitize(settings);
            var current = settings.ThemeAppearances.FirstOrDefault(value => value.Theme == settings.Theme);
            if (current == null) { current = new ThemeAppearance { Theme = settings.Theme }; settings.ThemeAppearances.Add(current); }
            current.Opacity = settings.Opacity; current.CornerRadius = settings.CornerRadius;
        }
        internal static void Select(DockSettings settings, string theme)
        {
            Remember(settings);
            string id = Normalize(theme);
            var previous = settings.ThemeAppearances.FirstOrDefault(value => value.Theme == id);
            settings.Theme = id;
            settings.Opacity = previous == null ? Opacity(id) : previous.Opacity;
            settings.CornerRadius = previous == null ? Corners(id) : previous.CornerRadius;
            Remember(settings);
        }
    }

    [DataContract]
    public sealed class DockSettings
    {
        private double left = Double.NaN;
        private double top = Double.NaN;

        // JSON has no representation for NaN. An absent coordinate means that
        // the window should choose its initial position on the current display.
        [IgnoreDataMember]
        public double Left { get { return left; } set { left = value; } }
        [IgnoreDataMember]
        public double Top { get { return top; } set { top = value; } }
        [DataMember(Name = "Left", Order = 0, EmitDefaultValue = false)]
        private double? SavedLeft
        {
            get { return Store.IsFinite(left) ? (double?)left : null; }
            set { left = value ?? Double.NaN; }
        }
        [DataMember(Name = "Top", Order = 1, EmitDefaultValue = false)]
        private double? SavedTop
        {
            get { return Store.IsFinite(top) ? (double?)top : null; }
            set { top = value ?? Double.NaN; }
        }
        [DataMember(Order = 2)] public double Width { get; set; }
        [DataMember(Order = 3)] public double Height { get; set; }
        [DataMember(Order = 4)] public double Opacity { get; set; }
        [DataMember(Order = 5)] public double IconSize { get; set; }
        [DataMember(Order = 6)] public double Zoom { get; set; }
        [DataMember(Order = 7)] public bool AlwaysOnTop { get; set; }
        [DataMember(Order = 8)] public bool MoveDesktopShortcuts { get; set; }
        [DataMember(Order = 9)] public bool ShowLabels { get; set; }
        [DataMember(Order = 10)] public bool LaunchAtLogin { get; set; }
        [DataMember(Order = 11)] public List<DockItem> Items { get; set; }
        [DataMember(Order = 12)] public int Rows { get; set; }
        [DataMember(Order = 13)] public string Language { get; set; }
        [DataMember(Order = 14)] public double CornerRadius { get; set; }
        [DataMember(Order = 15)] public string Placement { get; set; }
        [DataMember(Order = 16)] public bool AutoAddDesktopShortcuts { get; set; }
        [DataMember(Order = 17)] public string Theme { get; set; }
        [DataMember(Order = 18, EmitDefaultValue = false)] public List<ThemeAppearance> ThemeAppearances { get; set; }

        public DockSettings() { SetDefaults(); }

        [OnDeserializing]
        private void BeforeDeserialization(StreamingContext context) { SetDefaults(); }

        private void SetDefaults()
        {
            left = top = Double.NaN;
            Width = 570;
            Height = 280;
            Opacity = 0.72;
            IconSize = 52;
            Zoom = 1.28;
            AlwaysOnTop = false;
            MoveDesktopShortcuts = true;
            ShowLabels = false;
            LaunchAtLogin = false;
            AutoAddDesktopShortcuts = false;
            Rows = 1;
            Language = L10n.Normalize(null);
            CornerRadius = 23;
            Placement = "free";
            Theme = "dusk";
            ThemeAppearances = new List<ThemeAppearance>();
            Items = new List<DockItem>();
        }
    }

    [DataContract]
    public sealed class DockItem
    {
        [DataMember(Order = 0)] public string Id { get; set; }
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public string Path { get; set; }
        [DataMember(Order = 3)] public string OriginalPath { get; set; }
        [DataMember(Order = 4)] public bool ManagedShortcut { get; set; }

        // These fields make an interrupted copy/save/delete transaction
        // recoverable without deleting anything from the desktop on startup.
        [DataMember(Name = "DesktopSourceMoved", Order = 5, EmitDefaultValue = false)]
        internal bool DesktopSourceMoved;
        [DataMember(Name = "PendingSourceDelete", Order = 6, EmitDefaultValue = false)]
        internal bool PendingSourceDelete;
        [DataMember(Order = 7, EmitDefaultValue = false)]
        public bool PendingPublicMove { get; set; }
        // A title deliberately entered by the user is never translated.
        [DataMember(Order = 8, EmitDefaultValue = false)]
        public bool UserNamed { get; set; }
        // The elevated shared-desktop helper records its exact restore path so
        // the running dock can exclude that deliberate restore from auto-add.
        [DataMember(Name = "LastRestoredPath", Order = 9, EmitDefaultValue = false)]
        internal string LastRestoredPath;
        // Written by the ordinary dock before requesting a shared restore.
        // The helper may create exactly LastRestoredPath and never rewrites
        // this profile. The dock reconciles the intent after exit or restart.
        [DataMember(Name = "PendingPublicRestore", Order = 10, EmitDefaultValue = false)]
        internal bool PendingPublicRestore;
    }

    public sealed class Store
    {
        public const int MaximumItems = 30;
        internal enum PersonalTransferResult { NotApplicable, Completed, Retry, Preserved }
        private readonly string settingsPath;
        private readonly string backupPath;
        private readonly string libraryDirectory;
        private readonly string desktopDirectory;
        private readonly string publicDesktopDirectory;
        private readonly bool secureReads;
        private bool keepExistingBackup;

        public string DataDirectory { get; private set; }
        public DockSettings Settings { get; private set; }
        public string LastNotice { get; private set; }
        internal string LastRestoredPath { get; private set; }
        internal string LastPersonalTransferPath { get; private set; }
        internal PersonalTransferResult LastPersonalTransferResult { get; private set; }
        public bool IsFirstRun { get; private set; }
        internal string DesktopDirectory { get { return desktopDirectory; } }
        internal string PublicDesktopDirectory { get { return publicDesktopDirectory; } }

        public Store(string dataDir = null, string desktopOverride = null, string publicDesktopOverride = null,
            bool secureReads = false)
        {
            this.secureReads = secureReads;
            DataDirectory = NormalizePath(dataDir ?? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PolkaDock"));
            settingsPath = System.IO.Path.Combine(DataDirectory, "settings.json");
            backupPath = settingsPath + ".bak";
            libraryDirectory = System.IO.Path.Combine(DataDirectory, "Library");
            desktopDirectory = NormalizeOptionalPath(desktopOverride ??
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            publicDesktopDirectory = NormalizeOptionalPath(publicDesktopOverride ??
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            LastNotice = String.Empty;
            if (secureReads) RejectReparsePoints(DataDirectory);
            if (!secureReads) Directory.CreateDirectory(DataDirectory);
            if (secureReads)
            {
                RejectReparsePoints(DataDirectory);
                RejectReparsePoints(libraryDirectory);
            }
            if (!secureReads) Directory.CreateDirectory(libraryDirectory);
            if (secureReads)
            {
                RejectReparsePoints(libraryDirectory);
                RejectReparsePoints(settingsPath);
                RejectReparsePoints(backupPath);
                if (!Directory.Exists(DataDirectory) || !Directory.Exists(libraryDirectory) || !File.Exists(settingsPath))
                    throw new IOException(L10n.T("SharedFoldersFailed"));
            }
            IsFirstRun = !File.Exists(settingsPath) && !File.Exists(backupPath);
            bool repair = Load();
            Settings.Language = L10n.Normalize(Settings.Language);
            Settings.CornerRadius = Clamp(Settings.CornerRadius, 0, 100, 23);
            string placement = (Settings.Placement ?? "free").ToLowerInvariant();
            Settings.Placement = new[] { "free", "bottom", "top", "left", "right" }.Contains(placement) ? placement : "free";
            L10n.SetLanguage(Settings.Language);
            if (!secureReads) repair = RecoverInterruptedImports() || repair;
            SanitizeSettings();
            if (repair && !secureReads)
            {
                try { Save(); }
                catch (Exception ex)
                {
                    if (!IsFileError(ex)) throw;
                    AddNotice(L10n.T("RecoveredSaveFailed") + ex.Message);
                }
            }
        }

        public void Save()
        {
            if (secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            SanitizeSettings();
            string temporaryPath = System.IO.Path.Combine(DataDirectory,
                "settings." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(
                        stream, Encoding.UTF8, false, true, "  "))
                    {
                        new DataContractJsonSerializer(typeof(DockSettings)).WriteObject(writer, Settings);
                        writer.Flush();
                        stream.Flush(true);
                    }
                }
                if (File.Exists(settingsPath))
                    File.Replace(temporaryPath, settingsPath, keepExistingBackup ? null : backupPath, true);
                else
                    File.Move(temporaryPath, settingsPath);
                keepExistingBackup = false;
            }
            finally { TryDelete(temporaryPath); }
        }

        public bool SetTheme(string theme)
        {
            string requested = ThemeCatalog.Normalize(theme);
            if (ThemeCatalog.Normalize(Settings.Theme) == requested) return false;
            string previousTheme = Settings.Theme;
            double previousOpacity = Settings.Opacity, previousCorners = Settings.CornerRadius;
            var previousAppearances = ThemeCatalog.Copy(Settings.ThemeAppearances);
            ThemeCatalog.Select(Settings, requested);
            try { Save(); }
            catch
            {
                Settings.Theme = previousTheme; Settings.Opacity = previousOpacity; Settings.CornerRadius = previousCorners;
                Settings.ThemeAppearances = previousAppearances;
                throw;
            }
            return true;
        }

        public DockItem Add(string path)
        {
            if (secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            LastNotice = String.Empty;
            LastPersonalTransferPath = null;
            LastPersonalTransferResult = PersonalTransferResult.NotApplicable;
            string shellPath = CanonicalShellPath(path);
            string source = shellPath ?? NormalizePath(path);
            DockItem duplicate = Settings.Items.FirstOrDefault(delegate(DockItem item)
            {
                return SamePath(item.OriginalPath, source) || SamePath(item.Path, source);
            });
            if (duplicate != null)
            {
                RetryPersonalDesktopTransfer(source);
                RefreshPublicDuplicate(duplicate, source);
                return duplicate;
            }
            if (Settings.Items.Count >= MaximumItems)
                throw new InvalidOperationException(L10n.T("DockFull"));

            bool isDirectory = false;
            if (shellPath == null)
            {
                if (!File.Exists(source) && !Directory.Exists(source))
                    throw new FileNotFoundException(L10n.T("TargetMissing"), source);
                isDirectory = Directory.Exists(source);
            }
            DockItem added = new DockItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = shellPath == "shell:MyComputerFolder" ? L10n.T("Computer") :
                    shellPath == "shell:RecycleBinFolder" ? L10n.T("Trash") :
                    isDirectory ? new DirectoryInfo(source).Name : System.IO.Path.GetFileNameWithoutExtension(source),
                Path = source,
                OriginalPath = source,
                ManagedShortcut = false
            };

            bool publicShortcut = !isDirectory && shellPath == null && IsPublicDesktopShortcut(source);
            if (!isDirectory && shellPath == null && (IsUserDesktopShortcut(source) || publicShortcut))
            {
                string imported = System.IO.Path.Combine(libraryDirectory, added.Id + System.IO.Path.GetExtension(source));
                try
                {
                    File.Copy(source, imported, false);
                    added.Path = imported;
                    added.ManagedShortcut = true;
                    added.PendingSourceDelete = Settings.MoveDesktopShortcuts && !publicShortcut;
                    added.DesktopSourceMoved = false;
                    added.PendingPublicMove = Settings.MoveDesktopShortcuts && publicShortcut;
                    if (added.PendingPublicMove)
                        AddNotice(L10n.T("SharedCopyNotice"));
                }
                catch (Exception ex)
                {
                    if (!IsFileError(ex)) throw;
                    // Referencing the original is a safe fallback if this location
                    // does not permit the app to create its managed copy.
                    TryDelete(imported);
                    AddNotice(L10n.T("ImportCopyFailed") + ex.Message);
                }
            }

            Settings.Items.Add(added);
            try { Save(); }
            catch
            {
                Settings.Items.Remove(added);
                if (added.ManagedShortcut && IsOwnedLibraryFile(added)) TryDeleteOwnedShortcut(added.Path);
                throw;
            }

            if (added.PendingSourceDelete)
            {
                LastPersonalTransferPath = source;
                LastPersonalTransferResult = FinishPersonalDesktopTransfer(added);
            }
            return added;
        }

        private void RefreshPublicDuplicate(DockItem item, string source)
        {
            if (!Settings.MoveDesktopShortcuts || !item.ManagedShortcut || !IsOwnedLibraryFile(item) ||
                item.PendingPublicRestore || !IsPublicDesktopShortcut(source) || !SamePath(item.OriginalPath, source)) return;
            bool pending = item.PendingPublicMove;
            bool matches = false, verified = false;
            try
            {
                ValidatePublicStorage(item);
                matches = ReadVerifiedShortcut(item.Path).SequenceEqual(ReadVerifiedShortcut(source));
                verified = true;
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                AddNotice(L10n.T("ImportRemoveFailed") + ex.Message);
            }
            item.PendingPublicMove = matches;
            try { if (pending != matches) Save(); }
            catch { item.PendingPublicMove = pending; throw; }
            if (matches) AddNotice(L10n.T("SharedCopyNotice"));
            else if (verified) AddNotice(L10n.T("ImportChanged"));
        }

        internal PersonalTransferResult RetryPersonalDesktopTransfer(string path)
        {
            if (secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            LastPersonalTransferPath = null;
            LastPersonalTransferResult = PersonalTransferResult.NotApplicable;
            if (!Settings.MoveDesktopShortcuts || !IsUserDesktopShortcut(path)) return LastPersonalTransferResult;
            DockItem item = Settings.Items.FirstOrDefault(value => value.ManagedShortcut &&
                !value.PendingPublicRestore && SamePath(value.OriginalPath, path) && IsOwnedLibraryFile(value));
            if (item == null) return LastPersonalTransferResult;
            LastPersonalTransferPath = NormalizePath(path);
            LastPersonalTransferResult = FinishPersonalDesktopTransfer(item);
            return LastPersonalTransferResult;
        }

        private PersonalTransferResult FinishPersonalDesktopTransfer(DockItem item)
        {
            bool previousMoved = item.DesktopSourceMoved;
            byte[] bytes;
            try
            {
                bytes = ReadVerifiedShortcut(item.Path);
                if (File.Exists(item.OriginalPath) && !bytes.SequenceEqual(ReadVerifiedShortcut(item.OriginalPath)))
                {
                    item.PendingSourceDelete = false;
                    SavePersonalTransferState();
                    AddNotice(L10n.T("ImportChanged"));
                    return PersonalTransferResult.Preserved;
                }
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                item.PendingSourceDelete = IsSharingFailure(ex);
                SavePersonalTransferState();
                AddNotice(L10n.T("ImportRemoveFailed") + ex.Message);
                return item.PendingSourceDelete ? PersonalTransferResult.Retry : PersonalTransferResult.Preserved;
            }

            bool previousPending = item.PendingSourceDelete;
            item.DesktopSourceMoved = true;
            item.PendingSourceDelete = true;
            try { Save(); }
            catch (Exception ex)
            {
                item.DesktopSourceMoved = previousMoved;
                item.PendingSourceDelete = previousPending;
                if (!IsFileError(ex)) throw;
                AddNotice(L10n.T("ImportRemoveFailed") + ex.Message);
                return PersonalTransferResult.Retry;
            }
            try
            {
                // Ownership/restoration intent is durable before DELETE access
                // is requested for the exact, same-byte desktop file handle.
                if (File.Exists(item.OriginalPath)) DeleteVerifiedShortcut(item.OriginalPath, bytes);
                if (File.Exists(item.OriginalPath))
                {
                    // The installer can immediately recreate the name after
                    // our verified handle was deleted. Inspect it afresh on
                    // another stable sample rather than consuming that event.
                    SavePersonalTransferState();
                    AddNotice(L10n.T("ImportRemoveFailed") + L10n.T("SourceStillExists"));
                    return PersonalTransferResult.Retry;
                }
                item.PendingSourceDelete = false;
                SavePersonalTransferState();
                return PersonalTransferResult.Completed;
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                item.DesktopSourceMoved = previousMoved;
                item.PendingSourceDelete = IsSharingFailure(ex);
                SavePersonalTransferState();
                AddNotice(L10n.T("ImportRemoveFailed") + ex.Message);
                return item.PendingSourceDelete ? PersonalTransferResult.Retry : PersonalTransferResult.Preserved;
            }
        }

        private void SavePersonalTransferState()
        {
            try { Save(); }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                AddNotice(L10n.T("FinalSavePending") + ex.Message);
            }
        }

        private static bool IsSharingFailure(Exception error)
        {
            for (Exception current = error; current != null; current = current.InnerException)
            {
                var native = current as Win32Exception;
                int code = native == null ? current.HResult & 0xffff : native.NativeErrorCode;
                if (code == 32 || code == 33) return true;
            }
            return false;
        }

        public bool IsPublicShortcut(DockItem item)
        {
            return item != null && IsPublicDesktopShortcut(String.IsNullOrEmpty(item.OriginalPath) ? item.Path : item.OriginalPath);
        }

        // Upgrades references written by older versions without changing the
        // user's icon order or identity. It never removes a shared source.
        public int PreparePublicCopies()
        {
            if (secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            LastNotice = String.Empty;
            int prepared = 0;
            foreach (DockItem item in Settings.Items)
            {
                if (item.ManagedShortcut || !IsPublicShortcut(item)) continue;
                string original = String.IsNullOrEmpty(item.OriginalPath) ? item.Path : item.OriginalPath;
                if (!File.Exists(original)) continue;
                Guid parsed;
                if (!Guid.TryParseExact(item.Id, "N", out parsed))
                {
                    AddNotice(L10n.T("SharedIdFailed") + item.Name + L10n.T("OriginalUnchangedSuffix"));
                    continue;
                }
                string imported = System.IO.Path.Combine(libraryDirectory, item.Id + System.IO.Path.GetExtension(original));
                bool created = false;
                string oldPath = item.Path;
                string oldOriginal = item.OriginalPath;
                bool oldMoved = item.DesktopSourceMoved;
                bool oldPendingDelete = item.PendingSourceDelete;
                bool oldPendingPublic = item.PendingPublicMove;
                try
                {
                    if (File.Exists(imported))
                    {
                        if (!FilesEqual(imported, original))
                            throw new IOException(L10n.T("CopiesDiffer"));
                    }
                    else
                    {
                        File.Copy(original, imported, false);
                        created = true;
                    }
                    item.Path = imported;
                    item.OriginalPath = original;
                    item.ManagedShortcut = true;
                    item.DesktopSourceMoved = false;
                    item.PendingSourceDelete = false;
                    item.PendingPublicMove = Settings.MoveDesktopShortcuts;
                    Save();
                    prepared++;
                }
                catch (Exception ex)
                {
                    item.Path = oldPath;
                    item.OriginalPath = oldOriginal;
                    item.ManagedShortcut = false;
                    item.DesktopSourceMoved = oldMoved;
                    item.PendingSourceDelete = oldPendingDelete;
                    item.PendingPublicMove = oldPendingPublic;
                    if (created) TryDeleteOwnedShortcut(imported);
                    if (!IsFileError(ex)) throw;
                    AddNotice(L10n.T("SharedCopyPreparePrefix") + item.Name + L10n.T("SharedCopyPrepareSuffix") + ex.Message);
                }
            }
            if (prepared > 0 && Settings.MoveDesktopShortcuts)
                AddNotice(L10n.T("SharedPrepared"));
            return prepared;
        }

        public bool NeedsPublicRestore(DockItem item)
        {
            return item != null && item.ManagedShortcut && item.DesktopSourceMoved &&
                IsOwnedLibraryFile(item) && IsPublicShortcut(item);
        }

        public void ValidatePublicStorage(DockItem item)
        {
            if (item == null || !item.ManagedShortcut || !IsOwnedLibraryFile(item) || !IsPublicShortcut(item))
                throw new IOException(L10n.T("SharedPathsFailed"));
            RejectReparsePoints(DataDirectory);
            RejectReparsePoints(libraryDirectory);
            RejectReparsePoints(item.Path);
            RejectReparsePoints(publicDesktopDirectory);
            RejectReparsePoints(item.OriginalPath);
            if (!Directory.Exists(DataDirectory) || !Directory.Exists(libraryDirectory) ||
                !Directory.Exists(publicDesktopDirectory))
                throw new IOException(L10n.T("SharedFoldersFailed"));
        }

        // This method never requests elevation itself. The UI invokes it only
        // from its explicitly requested helper for the shared desktop action.
        public bool CompletePublicMove(DockItem item)
        {
            if (secureReads) return CompletePreparedPublicMove(item);
            LastNotice = String.Empty;
            DockItem stored = FindStoredItem(item);
            if (stored == null || !stored.ManagedShortcut || !IsOwnedLibraryFile(stored) ||
                !IsPublicShortcut(stored))
            {
                LastNotice = L10n.T("SharedCopyVerifyFailed");
                return false;
            }
            byte[] importedBytes;
            try
            {
                ValidatePublicStorage(stored);
                importedBytes = ReadVerifiedShortcut(stored.Path);
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                LastNotice = L10n.T("SharedCopySafeFailed") + ex.Message;
                return false;
            }
            if (stored.DesktopSourceMoved && !stored.PendingSourceDelete && !stored.PendingPublicMove) return true;
            bool sourceExists = File.Exists(stored.OriginalPath);
            bool sourceMatches = true;
            try { if (sourceExists) sourceMatches = importedBytes.SequenceEqual(ReadVerifiedShortcut(stored.OriginalPath)); }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                LastNotice = L10n.T("SharedSourceSafeFailed") + ex.Message;
                return false;
            }
            if (!sourceMatches)
            {
                LastNotice = L10n.T("SharedChanged");
                return false;
            }

            bool wasMoved = stored.DesktopSourceMoved;
            bool wasPendingDelete = stored.PendingSourceDelete;
            bool wasPendingPublic = stored.PendingPublicMove;
            stored.DesktopSourceMoved = true;
            stored.PendingSourceDelete = true;
            stored.PendingPublicMove = true;
            try { Save(); }
            catch (Exception ex)
            {
                stored.DesktopSourceMoved = wasMoved;
                stored.PendingSourceDelete = wasPendingDelete;
                stored.PendingPublicMove = wasPendingPublic;
                if (!IsFileError(ex)) throw;
                LastNotice = L10n.T("RestoreRecordFailed") + ex.Message;
                return false;
            }
            try
            {
                ValidatePublicStorage(stored);
                if (File.Exists(stored.OriginalPath)) DeleteVerifiedShortcut(stored.OriginalPath, importedBytes);
                if (File.Exists(stored.OriginalPath)) throw new IOException(L10n.T("SharedSourceStillExists"));
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                stored.DesktopSourceMoved = false;
                stored.PendingSourceDelete = false;
                stored.PendingPublicMove = true;
                LastNotice = L10n.T("SharedNeedsAdmin") + ex.Message;
                try { Save(); }
                catch (Exception saveError)
                {
                    if (!IsFileError(saveError)) throw;
                    AddNotice(L10n.T("CheckNextLaunch"));
                }
                return false;
            }
            stored.DesktopSourceMoved = true;
            stored.PendingSourceDelete = false;
            stored.PendingPublicMove = false;
            try { Save(); }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                LastNotice = L10n.T("SharedMovedPending") + ex.Message;
            }
            return true;
        }

        // Restores first, saves the resulting state, and deliberately retains
        // the owned library bytes for the caller's subsequent Remove operation.
        public void RestorePublic(DockItem item)
        {
            if (secureReads) { RestorePreparedPublic(item); return; }
            LastNotice = String.Empty;
            LastRestoredPath = null;
            DockItem stored = FindStoredItem(item);
            if (stored == null || !stored.ManagedShortcut || !IsOwnedLibraryFile(stored) || !IsPublicShortcut(stored))
                throw new IOException(L10n.T("SharedRestoreVerifyFailed"));
            ValidatePublicStorage(stored);
            if (!stored.DesktopSourceMoved)
            {
                stored.PendingPublicMove = false;
                Save();
                return;
            }
            string restoredPath;
            try
            {
                byte[] importedBytes = ReadVerifiedShortcut(stored.Path);
                ValidatePublicStorage(stored);
                restoredPath = RestoreShortcutBytes(importedBytes, stored.OriginalPath);
                LastRestoredPath = restoredPath;
                stored.LastRestoredPath = restoredPath;
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                LastNotice = L10n.T("SharedRestoreAdmin") + ex.Message;
                throw new IOException(LastNotice, ex);
            }
            bool pendingPublic = stored.PendingPublicMove;
            bool pendingDelete = stored.PendingSourceDelete;
            stored.DesktopSourceMoved = false;
            stored.PendingPublicMove = false;
            stored.PendingSourceDelete = false;
            try { Save(); }
            catch (Exception ex)
            {
                stored.DesktopSourceMoved = true;
                stored.PendingPublicMove = pendingPublic;
                stored.PendingSourceDelete = pendingDelete;
                if (!IsFileError(ex)) throw;
                LastNotice = L10n.T("SharedRestoredSavePending");
                throw new IOException(LastNotice, ex);
            }
            LastNotice = L10n.T("SharedRestoredPrefix") + System.IO.Path.GetFileName(restoredPath);
        }

        // Only the ordinary dock persists intent. The elevated helper receives
        // a read-only snapshot and is limited to its verified shared shortcut.
        internal void PreparePublicOperation(string operation, string itemId)
        {
            if (secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            var targets = operation == "--public-move" ? SelectPublicMoveTargets(itemId, false) :
                operation == "--public-restore" ? Settings.Items.Where(value => value.Id == itemId).ToArray() : new DockItem[0];
            if (targets.Length == 0 || (operation == "--public-restore" && targets.Length != 1))
                throw new IOException(L10n.T("SharedRestoreVerifyFailed"));
            var before = targets.Select(value => new PublicIntentState(value)).ToArray();
            try
            {
                foreach (DockItem item in targets)
                {
                    ValidatePublicStorage(item);
                    if (operation == "--public-move")
                    {
                        item.DesktopSourceMoved = true;
                        item.PendingSourceDelete = item.PendingPublicMove = true;
                        item.PendingPublicRestore = false;
                        item.LastRestoredPath = null;
                    }
                    else
                    {
                        if (!NeedsPublicRestore(item)) throw new IOException(L10n.T("SharedRestoreVerifyFailed"));
                        item.LastRestoredPath = SelectPublicRestoreDestination(item);
                        item.PendingPublicRestore = true;
                        item.PendingSourceDelete = item.PendingPublicMove = false;
                    }
                }
                Save();
            }
            catch { foreach (var state in before) state.Restore(); throw; }
        }

        internal DockItem[] GetPreparedPublicMoveItems(string itemIds)
        {
            if (!secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            return SelectPublicMoveTargets(itemIds, true);
        }

        private DockItem[] SelectPublicMoveTargets(string itemIds, bool requirePrepared)
        {
            DockItem[] targets;
            if (itemIds == null)
                targets = Settings.Items.Where(value => value != null && value.PendingPublicMove).ToArray();
            else
            {
                if (itemIds.Length == 0 || itemIds.Length > MaximumItems * 33 - 1)
                    throw new IOException(L10n.T("SharedCopyVerifyFailed"));
                string[] requested = itemIds.Split(',');
                if (requested.Length > MaximumItems) throw new IOException(L10n.T("SharedCopyVerifyFailed"));
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var selected = new List<DockItem>();
                foreach (string id in requested)
                {
                    Guid parsed;
                    if (!Guid.TryParseExact(id, "N", out parsed) || !known.Add(id))
                        throw new IOException(L10n.T("SharedCopyVerifyFailed"));
                    DockItem[] matching = Settings.Items.Where(value => value != null &&
                        String.Equals(value.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matching.Length != 1) throw new IOException(L10n.T("SharedCopyVerifyFailed"));
                    selected.Add(matching[0]);
                }
                targets = selected.ToArray();
            }
            // Validate the whole batch before the caller can mutate an item or
            // delete any source. An automatic import can request only its new
            // IDs, leaving older pending/cancelled shortcuts untouched.
            if (targets.Length == 0) throw new IOException(L10n.T("SharedCopyVerifyFailed"));
            foreach (DockItem item in targets)
            {
                ValidatePublicStorage(item);
                if (!item.PendingPublicMove || (requirePrepared &&
                    (!item.DesktopSourceMoved || !item.PendingSourceDelete || item.PendingPublicRestore)))
                    throw new IOException(L10n.T("SharedCopyVerifyFailed"));
            }
            return targets;
        }

        internal void CancelPublicRestoreIntent(string itemId)
        {
            if (secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            DockItem item = Settings.Items.FirstOrDefault(value => value.Id == itemId && value.PendingPublicRestore);
            if (item == null) return;
            PublicIntentState before = new PublicIntentState(item);
            item.PendingPublicRestore = false;
            item.LastRestoredPath = null;
            try { Save(); } catch { before.Restore(); throw; }
        }

        internal bool IsPendingPublicRestorePath(string path)
        {
            return Settings.Items.Any(value => value.PendingPublicRestore && SamePath(value.LastRestoredPath, path));
        }

        internal string[] ReconcilePublicRestoreIntents()
        {
            if (secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            var pending = Settings.Items.Where(value => value.PendingPublicRestore).ToArray();
            var before = pending.Select(value => new PublicIntentState(value)).ToArray();
            var completed = new List<string>();
            foreach (DockItem item in pending)
                if (TryCompletePublicRestoreIntent(item)) completed.Add(item.LastRestoredPath);
            if (completed.Count == 0) return new string[0];
            try { Save(); return completed.ToArray(); }
            catch (Exception ex)
            {
                foreach (var state in before) state.Restore();
                if (!IsFileError(ex)) throw;
                AddNotice(L10n.T("SharedRestoredSavePending"));
                return new string[0];
            }
        }

        private sealed class PublicIntentState
        {
            private readonly DockItem item;
            private readonly bool moved, pendingDelete, pendingMove, pendingRestore;
            private readonly string restoredPath;
            internal PublicIntentState(DockItem item)
            {
                this.item = item; moved = item.DesktopSourceMoved; pendingDelete = item.PendingSourceDelete;
                pendingMove = item.PendingPublicMove; pendingRestore = item.PendingPublicRestore; restoredPath = item.LastRestoredPath;
            }
            internal void Restore()
            {
                item.DesktopSourceMoved = moved; item.PendingSourceDelete = pendingDelete;
                item.PendingPublicMove = pendingMove; item.PendingPublicRestore = pendingRestore; item.LastRestoredPath = restoredPath;
            }
        }

        private bool CompletePreparedPublicMove(DockItem item)
        {
            LastNotice = String.Empty;
            DockItem stored = FindStoredItem(item);
            try
            {
                ValidatePublicStorage(stored);
                if (!stored.DesktopSourceMoved || !stored.PendingSourceDelete || !stored.PendingPublicMove || stored.PendingPublicRestore)
                    throw new IOException(L10n.T("SharedRestoreVerifyFailed"));
                byte[] bytes = ReadVerifiedShortcut(stored.Path);
                if (File.Exists(stored.OriginalPath)) DeleteVerifiedShortcut(stored.OriginalPath, bytes);
                if (File.Exists(stored.OriginalPath)) throw new IOException(L10n.T("SharedSourceStillExists"));
                return true;
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                LastNotice = L10n.T("SharedCopySafeFailed") + ex.Message;
                return false;
            }
        }

        private void RestorePreparedPublic(DockItem item)
        {
            LastNotice = String.Empty;
            DockItem stored = FindStoredItem(item);
            ValidatePublicStorage(stored);
            if (!stored.DesktopSourceMoved || stored.PendingSourceDelete || stored.PendingPublicMove || !stored.PendingPublicRestore ||
                !IsPublicDesktopShortcut(stored.LastRestoredPath))
                throw new IOException(L10n.T("SharedRestoreVerifyFailed"));
            string destination = NormalizePath(stored.LastRestoredPath);
            RejectReparsePoints(destination);
            byte[] bytes = ReadVerifiedShortcut(stored.Path);
            if (File.Exists(destination))
            {
                if (!bytes.SequenceEqual(ReadVerifiedShortcut(destination)))
                    throw new IOException(L10n.T("SharedChanged"));
            }
            else
            {
                // The ordinary dock already selected and journaled this exact
                // name. A new collision fails; the helper never silently picks
                // another destination or overwrites any existing namespace.
                WriteNewPublicShortcut(destination, bytes);
            }
            LastRestoredPath = destination;
        }

        private static void WriteNewPublicShortcut(string destination, byte[] bytes)
        {
            SafeFileHandle handle = CreateFile(destination, 0x40010000u, 0, IntPtr.Zero, 1,
                0x80200000u, IntPtr.Zero); // WRITE | DELETE; CREATE_NEW; WRITE_THROUGH | OPEN_REPARSE_POINT
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error(); handle.Dispose();
                throw new IOException(L10n.T("SharedRestoreAdmin") + new Win32Exception(error).Message);
            }
            using (var stream = new FileStream(handle, FileAccess.Write, 4096, false))
            {
                try
                {
                    RequireRegularSingleFile(stream.SafeFileHandle);
                    RequireFinalHandlePath(stream.SafeFileHandle, NormalizePath(destination));
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                catch
                {
                    // Remove only our newly created incomplete file through
                    // its exclusive handle, never a subsequent pathname.
                    FileDispositionInformation disposition = new FileDispositionInformation { DeleteFile = 1 };
                    SetFileInformationByHandle(stream.SafeFileHandle, 4, ref disposition,
                        (uint)Marshal.SizeOf(typeof(FileDispositionInformation)));
                    throw;
                }
            }
        }

        private string SelectPublicRestoreDestination(DockItem item)
        {
            byte[] bytes = ReadVerifiedShortcut(item.Path);
            if (IsPublicDesktopShortcut(item.LastRestoredPath) && VerifiedFilesEqual(item.Path, item.LastRestoredPath))
                return NormalizePath(item.LastRestoredPath);
            string folder = System.IO.Path.GetDirectoryName(item.OriginalPath);
            string stem = System.IO.Path.GetFileNameWithoutExtension(item.OriginalPath), extension = System.IO.Path.GetExtension(item.OriginalPath);
            for (int number = 0; number < 10000; number++)
            {
                string destination = number == 0 ? item.OriginalPath : System.IO.Path.Combine(folder,
                    stem + L10n.T("RestoredNamePrefix") + number.ToString() + ")" + extension);
                RejectReparsePoints(destination);
                if (!File.Exists(destination) && !Directory.Exists(destination)) return destination;
                if (File.Exists(destination) && bytes.SequenceEqual(ReadVerifiedShortcut(destination))) return destination;
            }
            throw new IOException(L10n.T("SharedRestoreNameFailed"));
        }

        private bool TryCompletePublicRestoreIntent(DockItem item)
        {
            if (!item.PendingPublicRestore || !item.ManagedShortcut || !IsOwnedLibraryFile(item) || !IsPublicShortcut(item) ||
                !IsPublicDesktopShortcut(item.LastRestoredPath) || !VerifiedFilesEqual(item.Path, item.LastRestoredPath)) return false;
            item.DesktopSourceMoved = item.PendingSourceDelete = item.PendingPublicMove = item.PendingPublicRestore = false;
            LastRestoredPath = item.LastRestoredPath;
            return true;
        }

        public void Remove(DockItem item)
        {
            if (secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            LastNotice = String.Empty;
            LastRestoredPath = null;
            DockItem stored = FindStoredItem(item);
            if (stored == null) return;
            int index = Settings.Items.IndexOf(stored);
            string restoredPath = null;
            bool owned = stored.ManagedShortcut && IsOwnedLibraryFile(stored);
            if (NeedsPublicRestore(stored))
            {
                LastNotice = L10n.T("SharedRestoreRequired");
                throw new IOException(LastNotice);
            }
            bool pendingPersonalTransfer = owned && stored.PendingSourceDelete && IsUserDesktopShortcut(stored.OriginalPath);
            if (owned && (stored.DesktopSourceMoved || pendingPersonalTransfer) && File.Exists(stored.Path))
            {
                if (!IsUserDesktopShortcut(stored.OriginalPath))
                    throw new IOException(L10n.T("OriginalLocationFailed"));
                try { restoredPath = RestoreShortcut(stored.Path, stored.OriginalPath); LastRestoredPath = restoredPath; }
                catch (Exception ex)
                {
                    if (!IsFileError(ex)) throw;
                    LastNotice = L10n.T("RestoreFailed") + ex.Message;
                    throw new IOException(LastNotice, ex);
                }
            }
            else if (owned && stored.DesktopSourceMoved && !File.Exists(stored.Path))
                AddNotice(L10n.T("CopyMissingRestore"));

            Settings.Items.RemoveAt(index);
            try { Save(); }
            catch (Exception ex)
            {
                Settings.Items.Insert(index, stored);
                if (restoredPath != null)
                    AddNotice(L10n.T("RestoredRemovePending"));
                throw new IOException(L10n.T("DockChangeSaveFailed") + LastNotice, ex);
            }
            // No original, shared, or arbitrary path is ever deleted here.
            if (owned)
            {
                try { DeleteOwnedShortcut(stored.Path); }
                catch (Exception ex)
                {
                    if (!IsFileError(ex)) throw;
                    AddNotice(L10n.T("BackupCleanupPending"));
                }
            }
            if (restoredPath != null)
                AddNotice(L10n.T("RestoredPrefix") + System.IO.Path.GetFileName(restoredPath));
        }

        private bool Load()
        {
            Settings = new DockSettings();
            L10n.SetLanguage(Settings.Language);
            bool mainExists = File.Exists(settingsPath);
            bool backupExists = File.Exists(backupPath);
            if (!mainExists && !backupExists) return false;
            Exception mainError;
            DockSettings loaded;
            if (mainExists && TryRead(settingsPath, out loaded, out mainError))
            {
                Settings = loaded;
                L10n.SetLanguage(Settings.Language);
                return false;
            }
            if (mainExists)
            {
                PreserveUnreadable(settingsPath);
                keepExistingBackup = true;
            }
            Exception backupError;
            if (backupExists && TryRead(backupPath, out loaded, out backupError))
            {
                Settings = loaded;
                L10n.SetLanguage(Settings.Language);
                AddNotice(L10n.T("SettingsRecovered"));
                return true;
            }
            if (backupExists) PreserveUnreadable(backupPath);
            AddNotice(L10n.T("SettingsReset"));
            return true;
        }

        private bool TryRead(string path, out DockSettings loaded, out Exception error)
        {
            loaded = null;
            error = null;
            try
            {
                using (MemoryStream stream = new MemoryStream(ReadStorageBytes(path), false))
                {
                    if (stream.Length > 4 * 1024 * 1024) throw new SerializationException(L10n.T("SettingsTooLarge"));
                    loaded = (DockSettings)new DataContractJsonSerializer(typeof(DockSettings)).ReadObject(stream);
                    if (loaded == null) throw new SerializationException(L10n.T("SettingsEmpty"));
                    ValidateItemSet(loaded);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex) && !(ex is SerializationException) && !(ex is XmlException) &&
                    !(ex is FormatException) && !(ex is ArgumentException)) throw;
                if (secureReads)
                    throw new IOException(L10n.T("SecureLoadFailed") + ex.Message, ex);
                error = ex;
                return false;
            }
        }

        private void PreserveUnreadable(string path)
        {
            if (secureReads) throw new IOException(L10n.T("SharedHelperReadOnly"));
            try
            {
                string preserved = path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                    "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json";
                byte[] bytes = ReadStorageBytes(path);
                using (FileStream stream = new FileStream(preserved, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                AddNotice(L10n.T("PreserveSettingsFailed") + ex.Message);
            }
        }

        private bool RecoverInterruptedImports()
        {
            if (Settings.Items == null) Settings.Items = new List<DockItem>();
            bool changed = false;
            foreach (DockItem item in Settings.Items.Where(delegate(DockItem value) { return value != null; }))
            {
                if (!item.ManagedShortcut) continue;
                if (!IsOwnedLibraryFile(item) || (!IsUserDesktopShortcut(item.OriginalPath) && !IsPublicDesktopShortcut(item.OriginalPath)))
                {
                    item.ManagedShortcut = false;
                    item.DesktopSourceMoved = item.PendingSourceDelete = false;
                    item.PendingPublicMove = false;
                    item.PendingPublicRestore = false;
                    item.LastRestoredPath = null;
                    changed = true;
                    continue;
                }
                if (item.PendingPublicRestore)
                {
                    // A helper can finish after the dock has restarted. Keep a
                    // missing intent so observation excludes its exact future
                    // restore name; reconcile on startup or the next poll.
                    if (TryCompletePublicRestoreIntent(item)) changed = true;
                    if (item.PendingPublicRestore) continue;
                }
                if (!item.PendingSourceDelete) continue;
                bool publicItem = IsPublicDesktopShortcut(item.OriginalPath);
                if (publicItem && secureReads)
                {
                    try { ValidatePublicStorage(item); }
                    catch (Exception ex)
                    {
                        if (!IsFileError(ex)) throw;
                        AddNotice(L10n.T("PendingSharedUnsafe") + ex.Message);
                        continue;
                    }
                }
                bool importedExists = File.Exists(item.Path);
                bool sourceExists = File.Exists(item.OriginalPath);
                // If a different shortcut has since appeared at the original
                // name, preserve the imported shortcut as a moved item so that
                // removal restores it under a free name instead of losing it.
                item.DesktopSourceMoved = importedExists && (!sourceExists || !(publicItem && secureReads ?
                    VerifiedFilesEqual(item.Path, item.OriginalPath) : FilesEqual(item.Path, item.OriginalPath)));
                item.PendingSourceDelete = false;
                item.PendingPublicMove = IsPublicDesktopShortcut(item.OriginalPath) && !item.DesktopSourceMoved;
                if (!importedExists && sourceExists)
                {
                    item.Path = item.OriginalPath;
                    item.ManagedShortcut = false;
                    item.PendingPublicMove = false;
                }
                changed = true;
                AddNotice(L10n.T("ImportCheckedPrefix") + (item.Name ?? L10n.T("ShortcutLower")) + ".");
            }
            return changed;
        }

        private void SanitizeSettings()
        {
            if (Settings == null) Settings = new DockSettings();
            ValidateItemSet(Settings);
            Settings.Left = IsFinite(Settings.Left) ? Math.Max(-100000, Math.Min(100000, Settings.Left)) : Double.NaN;
            Settings.Top = IsFinite(Settings.Top) ? Math.Max(-100000, Math.Min(100000, Settings.Top)) : Double.NaN;
            Settings.Width = Clamp(Settings.Width, 160, 2400, 570);
            Settings.Height = Clamp(Settings.Height, 80, 1600, 280);
            Settings.Opacity = Clamp(Settings.Opacity, 0.15, 1, 0.72);
            Settings.CornerRadius = Clamp(Settings.CornerRadius, 0, 100, 23);
            ThemeCatalog.Sanitize(Settings);
            Settings.IconSize = Clamp(Settings.IconSize, 24, 96, 52);
            Settings.Zoom = Clamp(Settings.Zoom, 1, 1.8, 1.28);
            Settings.Rows = Math.Max(1, Math.Min(4, Settings.Rows));
            Settings.Language = L10n.Normalize(Settings.Language);
            if (Settings.Items == null) Settings.Items = new List<DockItem>();
            Settings.Items.RemoveAll(delegate(DockItem item) { return item == null || String.IsNullOrWhiteSpace(item.Path); });
            foreach (DockItem item in Settings.Items)
            {
                if (String.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                if (String.IsNullOrWhiteSpace(item.OriginalPath)) item.OriginalPath = item.Path;
                if (String.IsNullOrWhiteSpace(item.Name))
                {
                    try { item.Name = System.IO.Path.GetFileNameWithoutExtension(item.OriginalPath); }
                    catch (ArgumentException) { item.Name = L10n.T("Shortcut"); }
                }
                if (item.Name != null && item.Name.Length > 512) item.Name = item.Name.Substring(0, 512);
            }
        }

        // An ambiguous or oversized persisted list follows normal recovery:
        // retain the original JSON and library, then use a valid backup. Never
        // truncate managed entries or change IDs tied to library filenames.
        private static void ValidateItemSet(DockSettings settings)
        {
            if (settings.Items == null) return;
            int count = 0;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DockItem item in settings.Items)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.Path)) continue;
                if (++count > MaximumItems) throw new SerializationException(L10n.T("SettingsItemLimit", MaximumItems));
                if (!String.IsNullOrWhiteSpace(item.Id) && !ids.Add(item.Id))
                    throw new SerializationException(L10n.T("SettingsDuplicateIds"));
            }
        }

        private string RestoreShortcut(string imported, string original)
        {
            string restoreDirectory = System.IO.Path.GetDirectoryName(original);
            Directory.CreateDirectory(restoreDirectory);
            string stem = System.IO.Path.GetFileNameWithoutExtension(original);
            string extension = System.IO.Path.GetExtension(original);
            for (int number = 0; number < 10000; number++)
            {
                string destination = number == 0 ? original : System.IO.Path.Combine(restoreDirectory,
                    stem + L10n.T("RestoredNamePrefix") + number.ToString() + ")" + extension);
                if (File.Exists(destination) || Directory.Exists(destination)) continue;
                try
                {
                    File.Copy(imported, destination, false);
                    return destination;
                }
                catch (IOException)
                {
                    if (File.Exists(destination) || Directory.Exists(destination)) continue;
                    throw;
                }
            }
            throw new IOException(L10n.T("RestoreNameFailed"));
        }

        private string RestoreShortcutBytes(byte[] bytes, string original)
        {
            string restoreDirectory = System.IO.Path.GetDirectoryName(original);
            string stem = System.IO.Path.GetFileNameWithoutExtension(original);
            string extension = System.IO.Path.GetExtension(original);
            for (int number = 0; number < 10000; number++)
            {
                RejectReparsePoints(restoreDirectory);
                string destination = number == 0 ? original : System.IO.Path.Combine(restoreDirectory,
                    stem + L10n.T("RestoredNamePrefix") + number.ToString() + ")" + extension);
                try
                {
                    // CreateNew never follows or overwrites an existing file,
                    // hard link, or reparse point at the collision candidate.
                    using (FileStream stream = new FileStream(destination, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    return destination;
                }
                catch (IOException)
                {
                    if (File.Exists(destination) || Directory.Exists(destination)) continue;
                    throw;
                }
            }
            throw new IOException(L10n.T("SharedRestoreNameFailed"));
        }

        private bool IsUserDesktopShortcut(string path)
        {
            try
            {
                if (String.IsNullOrEmpty(desktopDirectory) || String.IsNullOrEmpty(path)) return false;
                string full = NormalizePath(path);
                string parent = NormalizeOptionalPath(System.IO.Path.GetDirectoryName(full));
                string extension = System.IO.Path.GetExtension(full);
                return String.Equals(parent, desktopDirectory, StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(parent, publicDesktopDirectory, StringComparison.OrdinalIgnoreCase) &&
                    (String.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is NotSupportedException) && !(ex is PathTooLongException)) throw;
                return false;
            }
        }

        private bool IsPublicDesktopShortcut(string path)
        {
            try
            {
                if (String.IsNullOrEmpty(publicDesktopDirectory) || String.IsNullOrEmpty(path)) return false;
                string full = NormalizePath(path);
                string parent = NormalizeOptionalPath(System.IO.Path.GetDirectoryName(full));
                string extension = System.IO.Path.GetExtension(full);
                return String.Equals(parent, publicDesktopDirectory, StringComparison.OrdinalIgnoreCase) &&
                    (String.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is NotSupportedException) && !(ex is PathTooLongException)) throw;
                return false;
            }
        }

        private DockItem FindStoredItem(DockItem item)
        {
            if (item == null) return null;
            DockItem exact = Settings.Items.FirstOrDefault(delegate(DockItem candidate)
            {
                return Object.ReferenceEquals(candidate, item);
            });
            if (exact != null || String.IsNullOrEmpty(item.Id)) return exact;
            DockItem match = null;
            foreach (DockItem candidate in Settings.Items)
                if (candidate != null && String.Equals(candidate.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                {
                    if (match != null) throw new SerializationException(L10n.T("SettingsDuplicateIds"));
                    match = candidate;
                }
            return match;
        }

        private bool IsOwnedLibraryFile(DockItem item)
        {
            try
            {
                Guid parsed;
                if (item == null || String.IsNullOrEmpty(item.Path) ||
                    !Guid.TryParseExact(item.Id, "N", out parsed)) return false;
                string full = NormalizePath(item.Path);
                string extension = System.IO.Path.GetExtension(full);
                return String.Equals(NormalizeOptionalPath(System.IO.Path.GetDirectoryName(full)), libraryDirectory,
                    StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(System.IO.Path.GetFileNameWithoutExtension(full), item.Id, StringComparison.OrdinalIgnoreCase) &&
                    (String.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is NotSupportedException) && !(ex is PathTooLongException)) throw;
                return false;
            }
        }

        private static string CanonicalShellPath(string path)
        {
            if (String.Equals(path, "shell:MyComputerFolder", StringComparison.OrdinalIgnoreCase)) return "shell:MyComputerFolder";
            if (String.Equals(path, "shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase)) return "shell:RecycleBinFolder";
            return null;
        }

        private static string NormalizeOptionalPath(string path)
        {
            return String.IsNullOrWhiteSpace(path) ? null : NormalizePath(path);
        }

        private static string NormalizePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException(L10n.T("PathMissing"), "path");
            string full = System.IO.Path.GetFullPath(path);
            string root = System.IO.Path.GetPathRoot(full);
            return full.Length > root.Length ? full.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar) : full;
        }

        private static bool SamePath(string first, string second)
        {
            if (String.IsNullOrWhiteSpace(first) || String.IsNullOrWhiteSpace(second)) return false;
            string firstShell = CanonicalShellPath(first);
            string secondShell = CanonicalShellPath(second);
            if (firstShell != null || secondShell != null)
                return String.Equals(firstShell, secondShell, StringComparison.OrdinalIgnoreCase);
            try { return String.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is NotSupportedException) && !(ex is PathTooLongException)) throw;
                return false;
            }
        }

        internal static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static double Clamp(double value, double minimum, double maximum, double fallback)
        {
            return IsFinite(value) ? Math.Max(minimum, Math.Min(maximum, value)) : fallback;
        }
        private static bool IsFileError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException;
        }
        private void AddNotice(string message)
        {
            LastNotice = String.IsNullOrEmpty(LastNotice) ? message : LastNotice + Environment.NewLine + message;
        }
        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { if (!IsFileError(ex)) throw; }
        }
        private static void DeleteOwnedShortcut(string path)
        {
            if (!File.Exists(path)) return;
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        private static void TryDeleteOwnedShortcut(string path)
        {
            try { DeleteOwnedShortcut(path); }
            catch (Exception ex) { if (!IsFileError(ex)) throw; }
        }
        private static bool FilesEqual(string first, string second)
        {
            try
            {
                using (FileStream firstStream = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream secondStream = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (firstStream.Length != secondStream.Length) return false;
                    byte[] firstBuffer = new byte[8192];
                    byte[] secondBuffer = new byte[8192];
                    int count;
                    while ((count = firstStream.Read(firstBuffer, 0, firstBuffer.Length)) != 0)
                    {
                        int secondCount = 0;
                        while (secondCount < count)
                        {
                            int read = secondStream.Read(secondBuffer, secondCount, count - secondCount);
                            if (read == 0) return false;
                            secondCount += read;
                        }
                        for (int index = 0; index < count; index++)
                            if (firstBuffer[index] != secondBuffer[index]) return false;
                    }
                    return secondStream.ReadByte() == -1;
                }
            }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                // A locked or unreadable original is never deleted by Add;
                // recovery conservatively retains the managed restoration.
                return false;
            }
        }

        internal static void RejectReparsePoints(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new IOException(L10n.T("VerifyPathMissing"));
            string full = NormalizePath(path);
            Stack<string> ancestors = new Stack<string>();
            string current = full;
            while (!String.IsNullOrEmpty(current))
            {
                ancestors.Push(current);
                string parent = System.IO.Path.GetDirectoryName(current);
                if (String.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
                current = parent;
            }
            while (ancestors.Count > 0)
            {
                string component = ancestors.Pop();
                try
                {
                    if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException(L10n.T("ReparseUnsupported") + component);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        private static bool VerifiedFilesEqual(string first, string second)
        {
            try { return ReadVerifiedShortcut(first).SequenceEqual(ReadVerifiedShortcut(second)); }
            catch (Exception ex)
            {
                if (!IsFileError(ex)) throw;
                return false;
            }
        }

        internal static byte[] ReadVerifiedShortcut(string path)
        {
            using (FileStream stream = OpenVerifiedShortcut(path, false))
                return ReadVerifiedBytes(stream);
        }

        private byte[] ReadStorageBytes(string path)
        {
            if (secureReads) return ReadVerifiedShortcut(path);
            // Normal desktop-app storage may be presented through a legitimate
            // AppData virtualization layer. Privileged helpers explicitly opt
            // into strict final-handle-path validation instead.
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return ReadVerifiedBytes(stream);
        }

        private static byte[] ReadVerifiedBytes(FileStream stream)
        {
            if (stream.Length < 0 || stream.Length > 4 * 1024 * 1024)
                throw new IOException(L10n.T("SafeFileTooLarge"));
            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) throw new IOException(L10n.T("ReadChanged"));
                offset += read;
            }
            return bytes;
        }

        private static void DeleteVerifiedShortcut(string path, byte[] expected)
        {
            // DELETE access belongs to this exact verified handle. No other
            // process can write, rename, or replace it while the handle is held.
            using (FileStream stream = OpenVerifiedShortcut(path, true))
            {
                if (!ReadVerifiedBytes(stream).SequenceEqual(expected))
                    throw new IOException(L10n.T("SharedMoveChanged"));
                FileDispositionInformation disposition = new FileDispositionInformation { DeleteFile = 1 };
                if (!SetFileInformationByHandle(stream.SafeFileHandle, 4, ref disposition,
                    (uint)Marshal.SizeOf(typeof(FileDispositionInformation))))
                    throw NativeFileError(L10n.T("VerifiedDeleteFailed"));
            }
        }

        private static FileStream OpenVerifiedShortcut(string path, bool deleting)
        {
            string expected = NormalizePath(path);
            RejectReparsePoints(expected);
            SafeFileHandle handle = CreateFile(expected, 0x80000000u | (deleting ? 0x00010000u : 0u),
                0x00000001u, IntPtr.Zero, 3, 0x00200000u, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new IOException(L10n.T("VerifyOpenFailed") + new Win32Exception(error).Message, new Win32Exception(error));
            }
            try
            {
                ByHandleFileInformation information = RequireRegularSingleFile(handle);
                if (information.FileSizeHigh != 0 || information.FileSizeLow > 4 * 1024 * 1024)
                    throw new IOException(L10n.T("SafeFileTooLarge"));
                RequireFinalHandlePath(handle, expected);
                FileStream stream = new FileStream(handle, FileAccess.Read);
                handle = null;
                return stream;
            }
            finally { if (handle != null) handle.Dispose(); }
        }

        private static ByHandleFileInformation RequireRegularSingleFile(SafeFileHandle handle)
        {
            if (GetFileType(handle) != 1) throw new IOException(L10n.T("SafeFileUnsupported")); // FILE_TYPE_DISK
            ByHandleFileInformation information;
            if (!GetFileInformationByHandle(handle, out information))
                throw NativeFileError(L10n.T("VerifyPropertiesFailed"));
            if ((information.FileAttributes & (0x00000400u | 0x00000010u)) != 0 || information.NumberOfLinks != 1)
                throw new IOException(L10n.T("SafeFileUnsupported"));
            return information;
        }

        private static void RequireFinalHandlePath(SafeFileHandle handle, string expected)
        {
            StringBuilder finalPath = new StringBuilder(512);
            uint length = GetFinalPathNameByHandle(handle, finalPath, (uint)finalPath.Capacity, 0);
            if (length == 0) throw NativeFileError(L10n.T("VerifyFinalPathFailed"));
            if (length >= finalPath.Capacity)
            {
                finalPath.Capacity = checked((int)length + 1);
                length = GetFinalPathNameByHandle(handle, finalPath, (uint)finalPath.Capacity, 0);
                if (length == 0 || length >= finalPath.Capacity)
                    throw new IOException(L10n.T("VerifyFullFinalPathFailed"));
            }
            string resolved = finalPath.ToString();
            if (resolved.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) resolved = "\\\\" + resolved.Substring(8);
            else if (resolved.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase)) resolved = resolved.Substring(4);
            if (!String.Equals(NormalizePath(resolved), expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException(L10n.T("FinalPathMismatch"));
        }

        private static IOException NativeFileError(string message)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            return new IOException(message + " " + error.Message, error);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileDispositionInformation { public int DeleteFile; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
            IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetFileType(SafeFileHandle handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass,
            ref FileDispositionInformation information, uint bufferSize);
    }
}
