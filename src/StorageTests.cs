using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Polka;

internal static class StorageTests
{
    private static int assertions;
    private static string root;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    private static void Check(bool result, string description)
    {
        assertions++;
        if (!result) throw new Exception("FAILED: " + description);
    }

    private static Store NewStore(string scenario, out string desktop, out string data)
    {
        data = Path.Combine(root, scenario, "Data");
        desktop = Path.Combine(root, scenario, "Desktop");
        Directory.CreateDirectory(desktop);
        return new Store(data, desktop);
    }

    private static string Shortcut(string desktop, string name)
    {
        string path = Path.Combine(desktop, name);
        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 127, 255, 13, 10, 0, 69 });
        return path;
    }

    private static void Main(string[] args)
    {
        // Original regression fixtures deliberately exercise Russian recovery names.
        System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        L10n.SetLanguage("ru");
        if (args.Length == 2 && args[0] == "--reject-reparse-fixture")
        {
            bool rejectedPath = false;
            bool rejectedRead = false;
            try { Store.RejectReparsePoints(args[1]); } catch (IOException) { rejectedPath = true; }
            try { Store.ReadVerifiedShortcut(args[1]); } catch (IOException) { rejectedRead = true; }
            Check(rejectedPath && rejectedRead, "junction ancestry rejected before file read");
            Console.WriteLine("PASS: reparse fixture rejected by ancestry and verified reader.");
            return;
        }
        if (args.Length == 2 && args[0] == "--storage-junction-fixture")
        {
            byte[] before = File.ReadAllBytes(Path.Combine(args[1], "settings.json"));
            Store normal = new Store(args[1]);
            Check(normal.Settings.Items.Count == 1, "normal storage supports legitimate path alias");
            bool strictRejected = false;
            try { new Store(args[1], null, null, true); } catch (IOException) { strictRejected = true; }
            Check(strictRejected && before.SequenceEqual(File.ReadAllBytes(Path.Combine(args[1], "settings.json"))),
                "strict alias rejection never overwrites existing settings");
            Console.WriteLine("PASS: normal alias load and strict rejection both preserve configuration.");
            return;
        }
        root = Path.GetFullPath(args.Length == 0 ? "work\\test-data\\storage-" +
            DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) : args[0]);
        Directory.CreateDirectory(root);
        string desktop, data;
        Store store = NewStore("move", out desktop, out data);
        Check(store.IsFirstRun, "fresh directory reported as first run");
        Check(store.Settings.Rows == 1, "new dock defaults to one row");
        string original = Shortcut(desktop, "Приложение.lnk");
        byte[] originalBytes = File.ReadAllBytes(original);
        DockItem item = store.Add(original);
        Check(item.ManagedShortcut && !File.Exists(original) && File.Exists(item.Path), "desktop shortcut moved only after persisted import");
        Check(File.ReadAllBytes(item.Path).SequenceEqual(originalBytes), "all shortcut bytes preserved");
        Check(store.Add(original) == item && store.Add(item.Path) == item && store.Settings.Items.Count == 1, "duplicates recognized by original and managed paths even after source removal");
        store = new Store(data, desktop);
        Check(!store.IsFirstRun && store.Settings.Items.Count == 1, "configuration survived reload");
        item = store.Settings.Items[0];
        File.WriteAllText(original, "new unrelated shortcut", Encoding.UTF8);
        store.Remove(item);
        string restored = Path.Combine(desktop, "Приложение (восстановлено 1).lnk");
        Check(File.ReadAllText(original, Encoding.UTF8) == "new unrelated shortcut", "collision never overwrites desktop content");
        Check(File.Exists(restored) && File.ReadAllBytes(restored).SequenceEqual(originalBytes), "removed managed shortcut restored with free name and exact bytes");
        Check(!File.Exists(item.Path) && store.Settings.Items.Count == 0, "owned copy cleaned only after removal saved");
        Check(new Store(data, desktop).Settings.Items.Count == 0, "saved removal survived reload");

        store = NewStore("copy", out desktop, out data);
        store.Settings.MoveDesktopShortcuts = false;
        original = Shortcut(desktop, "Website.url");
        item = store.Add(original);
        Check(item.ManagedShortcut && File.Exists(original) && File.Exists(item.Path), "copy-only option keeps original");
        store.Remove(item);
        Check(File.Exists(original) && Directory.GetFiles(desktop).Length == 1 && !File.Exists(item.Path), "copy-only removal does not create duplicate desktop shortcut");

        store = NewStore("ordinary", out desktop, out data);
        original = Path.Combine(desktop, "notes.txt");
        File.WriteAllText(original, "keep");
        item = store.Add(original);
        Check(!item.ManagedShortcut && File.Exists(original) && item.Path == original, "ordinary desktop file stays in place");
        store.Remove(item);
        Check(File.Exists(original), "removing ordinary file reference never deletes file");
        string folder = Path.Combine(desktop, "Folder");
        Directory.CreateDirectory(folder);
        item = store.Add(folder);
        Check(!item.ManagedShortcut && Directory.Exists(folder), "folder stays in place");
        store.Remove(item);
        string nested = Path.Combine(desktop, "Subfolder");
        Directory.CreateDirectory(nested);
        original = Shortcut(nested, "nested.lnk");
        item = store.Add(original);
        Check(!item.ManagedShortcut && File.Exists(original), "nested desktop shortcut is not moved");

        store = NewStore("limit", out desktop, out data);
        Check(store.Add("shell:MyComputerFolder").Name == "Этот компьютер", "My Computer virtual item supported");
        Check(store.Add("shell:RecycleBinFolder").Name == "Корзина", "Recycle Bin virtual item supported");
        Check(store.Add("shell:recyclebinfolder") == store.Settings.Items[1], "shell duplicates case insensitive");
        for (int index = 0; index < 28; index++)
        {
            string file = Path.Combine(desktop, index.ToString() + ".txt");
            File.WriteAllText(file, index.ToString());
            store.Add(file);
        }
        bool rejected = false;
        string extra = Path.Combine(desktop, "extra.txt");
        File.WriteAllText(extra, "extra");
        try { store.Add(extra); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && store.Settings.Items.Count == 30 && File.Exists(extra), "thirty item limit enforced before file changes");

        store = NewStore("bounds", out desktop, out data);
        store.Settings.Opacity = Double.NaN;
        store.Settings.IconSize = Double.PositiveInfinity;
        store.Settings.Zoom = -100;
        store.Settings.Width = 999999;
        store.Settings.Left = Double.PositiveInfinity;
        store.Settings.Top = Double.NaN;
        store.Save();
        Check(store.Settings.Opacity == 0.72 && store.Settings.IconSize == 52 && store.Settings.Zoom == 1 && store.Settings.Width == 2400,
            "invalid and nonfinite numeric settings sanitized");
        string json = File.ReadAllText(Path.Combine(data, "settings.json"));
        Check(!json.Contains("NaN") && !json.Contains("Infinity") && json.Contains(Environment.NewLine), "configuration is formatted finite JSON");
        store = new Store(data, desktop);
        Check(Double.IsNaN(store.Settings.Left) && Double.IsNaN(store.Settings.Top), "unset coordinates survive JSON reload");

        store = NewStore("rows", out desktop, out data);
        for (int rows = 1; rows <= 4; rows++)
        {
            store.Settings.Rows = rows;
            store.Save();
            store = new Store(data, desktop);
            Check(store.Settings.Rows == rows, rows.ToString() + " row preference survives reload");
        }
        store.Settings.Rows = 0;
        store.Save();
        Check(new Store(data, desktop).Settings.Rows == 1, "row count below range clamps to one");
        store.Settings.Rows = 99;
        store.Save();
        Check(new Store(data, desktop).Settings.Rows == 4, "row count above range clamps to four");
        File.WriteAllText(Path.Combine(data, "settings.json"), "{\"IconSize\":38,\"Items\":[]}");
        store = new Store(data, desktop);
        Check(store.Settings.Rows == 1 && store.Settings.IconSize == 38,
            "old settings without row count keep single row and existing preferences");

        store = NewStore("corruption", out desktop, out data);
        store.Add("shell:MyComputerFolder");
        store.Save();
        File.WriteAllText(Path.Combine(data, "settings.json"), "{broken JSON");
        store = new Store(data, desktop);
        Check(store.Settings.Items.Count == 1 && store.Settings.Items[0].Path == "shell:MyComputerFolder", "valid backup restores corrupted main JSON");
        Check(Directory.GetFiles(data, "*.unreadable-*.json").Length == 1 && store.LastNotice.Length > 0, "unreadable source preserved and user notice supplied");
        store = new Store(data, desktop);
        Check(store.Settings.Items.Count == 1, "recovered primary configuration is readable");

        store = NewStore("interrupted-copy", out desktop, out data);
        store.Settings.MoveDesktopShortcuts = false;
        original = Shortcut(desktop, "pending.lnk");
        item = store.Add(original);
        item.PendingSourceDelete = item.DesktopSourceMoved = true;
        store.Save();
        store = new Store(data, desktop);
        Check(!store.Settings.Items[0].PendingSourceDelete && !store.Settings.Items[0].DesktopSourceMoved && File.Exists(original),
            "interrupted pre-delete import keeps original and resolves safely");
        store.Remove(store.Settings.Items[0]);
        Check(Directory.GetFiles(desktop).Length == 1, "recovered copy removal does not duplicate source");

        store = NewStore("interrupted-move", out desktop, out data);
        original = Shortcut(desktop, "pending.lnk");
        item = store.Add(original);
        item.PendingSourceDelete = true;
        store.Save();
        store = new Store(data, desktop);
        Check(store.Settings.Items[0].DesktopSourceMoved && !store.Settings.Items[0].PendingSourceDelete,
            "interrupted post-delete import remains restorable");
        store.Remove(store.Settings.Items[0]);
        Check(File.Exists(original), "recovered moved shortcut restored");

        store = NewStore("interrupted-replaced-source", out desktop, out data);
        original = Shortcut(desktop, "pending.lnk");
        item = store.Add(original);
        item.PendingSourceDelete = true;
        store.Save();
        File.WriteAllText(original, "different replacement", Encoding.UTF8);
        store = new Store(data, desktop);
        Check(store.Settings.Items[0].DesktopSourceMoved, "interrupted move with different replacement remains restorable");
        store.Remove(store.Settings.Items[0]);
        Check(File.ReadAllText(original, Encoding.UTF8) == "different replacement" &&
            File.Exists(Path.Combine(desktop, "pending (восстановлено 1).lnk")), "recovery preserves both a replaced source and imported shortcut");

        store = NewStore("save-rollback", out desktop, out data);
        store.Save();
        original = Shortcut(desktop, "rollback.lnk");
        bool failed = false;
        using (FileStream locked = new FileStream(Path.Combine(data, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { store.Add(original); } catch (IOException) { failed = true; }
        }
        Check(failed && File.Exists(original) && store.Settings.Items.Count == 0 && Directory.GetFiles(Path.Combine(data, "Library")).Length == 0,
            "failed durable save rolls back imported copy without deleting desktop source");

        store = NewStore("delete-fallback", out desktop, out data);
        original = Shortcut(desktop, "readonly.lnk");
        File.SetAttributes(original, FileAttributes.ReadOnly);
        item = store.Add(original);
        Check(File.Exists(original) && File.Exists(item.Path) && !item.DesktopSourceMoved && store.LastNotice.Length > 0,
            "desktop deletion denied safely falls back to a managed copy");
        File.SetAttributes(original, FileAttributes.Normal);
        store.Remove(item);
        Check(File.Exists(original) && Directory.GetFiles(desktop).Length == 1 && !File.Exists(item.Path),
            "fallback copy removal preserves single original and cleans owned read-only copy");

        store = NewStore("untrusted-managed-path", out desktop, out data);
        original = Shortcut(desktop, "untouched.lnk");
        store.Settings.Items.Add(new DockItem { Id = Guid.NewGuid().ToString("N"), Name = "Invalid owned path",
            Path = original, OriginalPath = original, ManagedShortcut = true, DesktopSourceMoved = true });
        store.Save();
        store = new Store(data, desktop);
        Check(!store.Settings.Items[0].ManagedShortcut, "managed flag does not make arbitrary path owned");
        store.Remove(store.Settings.Items[0]);
        Check(File.Exists(original), "malformed ownership metadata cannot delete original file");

        store = NewStore("all-corrupt", out desktop, out data);
        store.Save();
        store.Save();
        File.WriteAllText(Path.Combine(data, "settings.json"), "broken main");
        File.WriteAllText(Path.Combine(data, "settings.json.bak"), "broken backup");
        store = new Store(data, desktop);
        Check(!store.IsFirstRun && store.Settings.Items.Count == 0 &&
            Directory.GetFiles(data, "*.unreadable-*.json").Length == 2, "both corrupt configurations preserved without seeding new user items");

        store = NewStore("public-copy", out desktop, out data);
        string publicDesktop = Path.Combine(root, "public-copy", "PublicDesktop");
        Directory.CreateDirectory(publicDesktop);
        store = new Store(data, desktop, publicDesktop);
        original = Shortcut(publicDesktop, "shared.lnk");
        originalBytes = File.ReadAllBytes(original);
        item = store.Add(original);
        Check(item.ManagedShortcut && item.PendingPublicMove && File.Exists(original) &&
            File.ReadAllBytes(item.Path).SequenceEqual(originalBytes) && store.IsPublicShortcut(item),
            "public shortcut imported as owned exact copy without deleting original");
        Check(!store.NeedsPublicRestore(item) && store.LastNotice.Contains("администратора"),
            "public copy explains required action but does not request restoration yet");
        store = new Store(data, desktop, publicDesktop);
        item = store.Settings.Items[0];
        Check(item.PendingPublicMove && item.ManagedShortcut && File.Exists(original), "pending public move persisted without startup deletion");
        Check(store.CompletePublicMove(item) && !File.Exists(original) && !item.PendingPublicMove &&
            store.NeedsPublicRestore(item), "explicit public move persists restoration state before deleting source");
        string importedPath = item.Path;
        bool restoreRequired = false;
        try { store.Remove(item); } catch (IOException ex) { restoreRequired = ex.Message.Contains("администратора"); }
        Check(restoreRequired && store.Settings.Items.Count == 1 && File.Exists(importedPath),
            "public moved item cannot be removed before administrator restoration");
        File.WriteAllText(original, "replacement shared shortcut", Encoding.UTF8);
        store.RestorePublic(item);
        string restoredPublic = Path.Combine(publicDesktop, "shared (восстановлено 1).lnk");
        Check(File.ReadAllText(original, Encoding.UTF8) == "replacement shared shortcut" &&
            File.ReadAllBytes(restoredPublic).SequenceEqual(originalBytes) && File.Exists(importedPath) &&
            !item.PendingPublicMove && !store.NeedsPublicRestore(item),
            "public restoration preserves collision, original bytes, and owned library for subsequent removal");
        store.Remove(item);
        Check(store.Settings.Items.Count == 0 && !File.Exists(importedPath) && File.Exists(restoredPublic),
            "remove after successful public restoration cleans only owned copy");

        store = NewStore("public-denied", out desktop, out data);
        publicDesktop = Path.Combine(root, "public-denied", "PublicDesktop");
        Directory.CreateDirectory(publicDesktop);
        store = new Store(data, desktop, publicDesktop);
        original = Shortcut(publicDesktop, "shared-readonly.url");
        File.SetAttributes(original, FileAttributes.ReadOnly);
        item = store.Add(original);
        Check(!store.CompletePublicMove(item) && File.Exists(original) && item.PendingPublicMove &&
            !store.NeedsPublicRestore(item), "failed shared source deletion reverts moved state and keeps pending action");
        store = new Store(data, desktop, publicDesktop);
        item = store.Settings.Items[0];
        Check(item.PendingPublicMove && !store.NeedsPublicRestore(item), "failed public move rollback survived reload");
        File.SetAttributes(original, FileAttributes.Normal);
        store.Remove(item);
        Check(File.Exists(original) && store.Settings.Items.Count == 0,
            "removing pending public copy preserves shared desktop source");

        store = NewStore("public-modified", out desktop, out data);
        publicDesktop = Path.Combine(root, "public-modified", "PublicDesktop");
        Directory.CreateDirectory(publicDesktop);
        store = new Store(data, desktop, publicDesktop);
        original = Shortcut(publicDesktop, "modified.lnk");
        item = store.Add(original);
        File.WriteAllText(original, "modified after import", Encoding.UTF8);
        Check(!store.CompletePublicMove(item) && File.ReadAllText(original, Encoding.UTF8) == "modified after import" &&
            item.PendingPublicMove && !store.NeedsPublicRestore(item), "modified shared source is never deleted");

        store = NewStore("public-save-rollback", out desktop, out data);
        publicDesktop = Path.Combine(root, "public-save-rollback", "PublicDesktop");
        Directory.CreateDirectory(publicDesktop);
        store = new Store(data, desktop, publicDesktop);
        original = Shortcut(publicDesktop, "save-rollback.lnk");
        item = store.Add(original);
        using (FileStream locked = new FileStream(Path.Combine(data, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(!store.CompletePublicMove(item) && File.Exists(original) && item.PendingPublicMove &&
                !store.NeedsPublicRestore(item), "public save failure cannot delete shared source");
        }

        store = NewStore("public-interrupted", out desktop, out data);
        publicDesktop = Path.Combine(root, "public-interrupted", "PublicDesktop");
        Directory.CreateDirectory(publicDesktop);
        store = new Store(data, desktop, publicDesktop);
        original = Shortcut(publicDesktop, "public-pending.lnk");
        item = store.Add(original);
        item.PendingSourceDelete = item.DesktopSourceMoved = true;
        store.Save();
        store = new Store(data, desktop, publicDesktop);
        item = store.Settings.Items[0];
        Check(item.PendingPublicMove && !item.DesktopSourceMoved && !item.PendingSourceDelete && File.Exists(original),
            "interrupted public move before deletion recovers pending status without deleting source");
        item.PendingSourceDelete = item.DesktopSourceMoved = true;
        store.Save();
        File.Delete(original);
        store = new Store(data, desktop, publicDesktop);
        item = store.Settings.Items[0];
        Check(!item.PendingPublicMove && item.DesktopSourceMoved && !item.PendingSourceDelete && store.NeedsPublicRestore(item),
            "interrupted public move after deletion recovers restorable status");
        store.RestorePublic(item);
        store.Remove(item);
        Check(File.Exists(original), "interrupted public move can be restored safely");

        store = NewStore("public-upgrade", out desktop, out data);
        publicDesktop = Path.Combine(root, "public-upgrade", "PublicDesktop");
        Directory.CreateDirectory(publicDesktop);
        store = new Store(data, desktop, publicDesktop);
        original = Shortcut(publicDesktop, "old-reference.lnk");
        string legacyId = Guid.NewGuid().ToString("N");
        store.Settings.Items.Add(new DockItem { Id = legacyId, Name = "Legacy shared label",
            Path = original, OriginalPath = original, ManagedShortcut = false });
        store.Settings.Items.Add(new DockItem { Id = Guid.NewGuid().ToString("N"), Name = "Second",
            Path = "shell:MyComputerFolder", OriginalPath = "shell:MyComputerFolder" });
        store.Save();
        Check(store.PreparePublicCopies() == 1 && store.Settings.Items[0].Id == legacyId &&
            store.Settings.Items[0].Name == "Legacy shared label" && store.Settings.Items[0].ManagedShortcut &&
            store.Settings.Items[0].PendingPublicMove && File.Exists(original), "legacy public upgrade keeps ID, name, order and source while preparing copy");
        store = new Store(data, desktop, publicDesktop);
        Check(store.Settings.Items[0].Id == legacyId && store.Settings.Items[0].ManagedShortcut &&
            store.Settings.Items[1].Path == "shell:MyComputerFolder" && store.PreparePublicCopies() == 0,
            "legacy public upgrade persisted and is idempotent");

        store = NewStore("public-upgrade-rollback", out desktop, out data);
        publicDesktop = Path.Combine(root, "public-upgrade-rollback", "PublicDesktop");
        Directory.CreateDirectory(publicDesktop);
        store = new Store(data, desktop, publicDesktop);
        original = Shortcut(publicDesktop, "legacy-rollback.url");
        store.Settings.Items.Add(new DockItem { Id = Guid.NewGuid().ToString("N"), Name = "Legacy rollback",
            Path = original, OriginalPath = original, ManagedShortcut = false });
        store.Save();
        using (FileStream locked = new FileStream(Path.Combine(data, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(store.PreparePublicCopies() == 0 && !store.Settings.Items[0].ManagedShortcut &&
                store.Settings.Items[0].Path == original && File.Exists(original) &&
                Directory.GetFiles(Path.Combine(data, "Library")).Length == 0,
                "legacy public upgrade save failure rolls back copy and reference without source deletion");
        }

        store = NewStore("public-hardlink-source", out desktop, out data);
        publicDesktop = Path.Combine(root, "public-hardlink-source", "PublicDesktop");
        Directory.CreateDirectory(publicDesktop);
        store = new Store(data, desktop, publicDesktop);
        original = Shortcut(publicDesktop, "hardlinked-source.lnk");
        string linked = Path.Combine(root, "public-hardlink-source", "second-name.lnk");
        if (CreateHardLink(linked, original, IntPtr.Zero))
        {
            item = store.Add(original);
            Check(!store.CompletePublicMove(item) && File.Exists(original) && File.Exists(linked) && item.PendingPublicMove,
                "public move rejects hardlinked source without deletion");
            bool hardlinkReadRejected = false;
            try { Store.ReadVerifiedShortcut(original); } catch (IOException) { hardlinkReadRejected = true; }
            Check(hardlinkReadRejected, "verified reader rejects files with multiple hardlinks");
        }
        else Console.WriteLine("SKIP: native hardlink source fixture unavailable (Win32 " + Marshal.GetLastWin32Error() + ").");

        store = NewStore("public-hardlink-library", out desktop, out data);
        publicDesktop = Path.Combine(root, "public-hardlink-library", "PublicDesktop");
        Directory.CreateDirectory(publicDesktop);
        store = new Store(data, desktop, publicDesktop);
        original = Shortcut(publicDesktop, "library-hardlink.lnk");
        item = store.Add(original);
        Check(store.CompletePublicMove(item), "public move setup for malicious library fixture");
        File.Delete(item.Path);
        string secretFixture = Path.Combine(root, "public-hardlink-library", "must-not-be-copied.txt");
        File.WriteAllText(secretFixture, "isolated protected-content stand-in", Encoding.UTF8);
        if (CreateHardLink(item.Path, secretFixture, IntPtr.Zero))
        {
            bool hardlinkRestoreRejected = false;
            try { store.RestorePublic(item); } catch (IOException) { hardlinkRestoreRejected = true; }
            Check(hardlinkRestoreRejected && !File.Exists(original) && File.Exists(secretFixture) && File.Exists(item.Path),
                "public restore never copies hardlinked library target");
        }
        else Console.WriteLine("SKIP: native hardlink library fixture unavailable (Win32 " + Marshal.GetLastWin32Error() + ").");

        string verifiedNormal = Shortcut(desktop, "verified-normal.lnk");
        Check(Store.ReadVerifiedShortcut(verifiedNormal).SequenceEqual(File.ReadAllBytes(verifiedNormal)),
            "verified reader returns exact bytes for single-link ordinary file");
        string oversized = Path.Combine(desktop, "oversized.lnk");
        using (FileStream stream = new FileStream(oversized, FileMode.CreateNew, FileAccess.Write))
            stream.SetLength(4 * 1024 * 1024 + 1);
        bool oversizedRejected = false;
        try { Store.ReadVerifiedShortcut(oversized); } catch (IOException) { oversizedRejected = true; }
        Check(oversizedRejected, "verified reader enforces four megabyte size bound");

        store = NewStore("strict-valid-settings", out desktop, out data);
        store.Add("shell:MyComputerFolder");
        store = new Store(data, desktop, null, true);
        Check(store.Settings.Items.Count == 1, "strict constructor loads verified single-link valid configuration");
        byte[] validBackupBytes = File.ReadAllBytes(Path.Combine(data, "settings.json"));
        File.WriteAllBytes(Path.Combine(data, "settings.json.bak"), validBackupBytes);
        File.WriteAllText(Path.Combine(data, "settings.json"), "{invalid existing privileged config");
        byte[] corruptBytes = File.ReadAllBytes(Path.Combine(data, "settings.json"));
        bool strictCorruptionRejected = false;
        try { new Store(data, desktop, null, true); } catch (IOException) { strictCorruptionRejected = true; }
        Check(strictCorruptionRejected && corruptBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(data, "settings.json"))) &&
            validBackupBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(data, "settings.json.bak"))) &&
            Directory.GetFiles(data, "*.unreadable-*.json").Length == 0,
            "strict constructor aborts invalid config without recovery, preservation copies, or default overwrite");

        store = NewStore("strict-hardlink-settings", out desktop, out data);
        string configTarget = Path.Combine(root, "strict-hardlink-settings", "actual-config.json");
        File.WriteAllText(configTarget, "{\"Items\":[{\"Id\":\"0123456789abcdef0123456789abcdef\",\"Name\":\"Keep\",\"Path\":\"shell:MyComputerFolder\"}]}", new UTF8Encoding(false));
        string configLink = Path.Combine(data, "settings.json");
        if (CreateHardLink(configLink, configTarget, IntPtr.Zero))
        {
            byte[] beforeConfig = File.ReadAllBytes(configTarget);
            bool strictConfigLinkRejected = false;
            try { new Store(data, desktop, null, true); } catch (IOException) { strictConfigLinkRejected = true; }
            Check(strictConfigLinkRejected && beforeConfig.SequenceEqual(File.ReadAllBytes(configTarget)) &&
                beforeConfig.SequenceEqual(File.ReadAllBytes(configLink)), "strict reader rejects hardlinked config without altering either name");
            Check(new Store(data, desktop).Settings.Items.Count == 1, "normal application storage can read valid hardlinked config without resetting it");
        }
        else Console.WriteLine("SKIP: native hardlink configuration fixture unavailable (Win32 " + Marshal.GetLastWin32Error() + ").");

        LocalizationStorage();
        Console.WriteLine("PASS: " + assertions.ToString() + " storage and localization assertions. Test data: " + root);
    }
    private static void LocalizationStorage()
    {
        var originalCulture=System.Threading.Thread.CurrentThread.CurrentUICulture;
        try {
            System.Threading.Thread.CurrentThread.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
            Check(new DockSettings().Language=="en", "English OS culture selects English for a fresh profile");
            System.Threading.Thread.CurrentThread.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            Check(new DockSettings().Language=="ru", "Russian OS culture selects Russian for a fresh profile");
            System.Threading.Thread.CurrentThread.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Check(new DockSettings().Language=="en" && L10n.Normalize("invalid")=="en", "unsupported language safely falls back to English");
            string desktop,data;
            Store store=NewStore("localization",out desktop,out data);
            var computer=store.Add("shell:MyComputerFolder");
            var trash=store.Add("shell:RecycleBinFolder");
            string original=Shortcut(desktop,"My game.lnk");
            var managed=store.Add(original);
            byte[] managedBytes=File.ReadAllBytes(managed.Path);
            string id=managed.Id, savedPath=managed.Path;
            store.Settings.Rows=3; store.Settings.Opacity=.44; store.Settings.Language="ru"; store.Save();
            store=new Store(data,desktop);
            Check(store.Settings.Language=="ru" && L10n.Language=="ru", "selected Russian overrides non-Russian OS on reload");
            computer=store.Settings.Items[0]; trash=store.Settings.Items[1];
            Check(L10n.DisplayName(computer)=="Этот компьютер" && L10n.DisplayName(trash)=="Корзина", "built-in labels follow selected Russian");
            string originalComputerName=computer.Name;
            store.Settings.Language="en"; L10n.SetLanguage("en");
            Check(L10n.DisplayName(computer)=="This PC" && computer.Name==originalComputerName, "changing language translates display without rewriting stored title");
            computer.Name="Игровой ПК"; computer.UserNamed=true;
            trash.Name="Recycle Bin"; trash.UserNamed=true;
            store.Save(); store=new Store(data,desktop);
            Check(store.Settings.Language=="en" && L10n.Language=="en", "selected English persists");
            L10n.SetLanguage("ru");
            Check(L10n.DisplayName(store.Settings.Items[0])=="Игровой ПК" && L10n.DisplayName(store.Settings.Items[1])=="Recycle Bin", "explicit custom names survive language changes, including default-looking names");
            managed=store.Settings.Items[2];
            Check(managed.Id==id && managed.Path==savedPath && managed.ManagedShortcut && !File.Exists(original) && managedBytes.SequenceEqual(File.ReadAllBytes(savedPath)), "language changes preserve moved shortcut identity and exact bytes");
            Check(store.Settings.Rows==3 && Math.Abs(store.Settings.Opacity-.44)<.0001, "language changes preserve existing visual settings");
            File.WriteAllText(Path.Combine(data,"settings.json"),"{\"Rows\":2,\"Items\":[{\"Id\":\"legacy\",\"Name\":\"My custom PC\",\"Path\":\"shell:MyComputerFolder\"}]}",new UTF8Encoding(false));
            store=new Store(data,desktop);
            Check(store.Settings.Language=="en" && store.Settings.Rows==2 && L10n.DisplayName(store.Settings.Items[0])=="My custom PC", "legacy profile gains OS language while preserving custom shell titles and layout");
            System.Threading.Thread.CurrentThread.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            store=NewStore("localized-backup-recovery",out desktop,out data);
            store.Settings.Language="en"; store.Save(); store.Save();
            File.WriteAllText(Path.Combine(data,"settings.json"),"{invalid-main");
            L10n.SetLanguage("ru");
            store=new Store(data,desktop);
            Check(store.Settings.Language=="en" && store.LastNotice.StartsWith("Settings were restored from a backup.",StringComparison.Ordinal), "backup recovery notice uses saved English language on Russian OS before UI startup");
            foreach(string code in new[]{"ru","en"}) {
                L10n.SetLanguage(code);
                foreach(string key in L10n.Keys) Check(!String.IsNullOrWhiteSpace(L10n.T(key,1,2)), "complete and format-valid "+code+" translation: "+key);
            }
            bool unknownRejected=false;
            try { L10n.T("missing-test-key"); } catch(ArgumentException) { unknownRejected=true; }
            Check(unknownRejected,"missing translation keys fail clearly during tests");
        } finally {
            System.Threading.Thread.CurrentThread.CurrentUICulture=originalCulture;
            L10n.SetLanguage("ru");
        }
    }
}
