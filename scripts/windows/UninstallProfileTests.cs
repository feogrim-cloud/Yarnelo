using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// This executable calls only injected synthetic paths. It cannot launch Yarnelo,
// uninstall an application, inspect a real profile, or change startup settings.
internal static class UninstallProfileTests
{
    private static string root;
    private static int passed;
    private sealed class Fixture
    {
        internal string Root, Profile, Library, Desktop, Public;
    }
    private static Fixture Make(string name)
    {
        var f = new Fixture();
        f.Root = Path.Combine(root, name);
        f.Profile = Path.Combine(f.Root, "Profile"); f.Library = Path.Combine(f.Profile, "Library");
        f.Desktop = Path.Combine(f.Root, "Desktop"); f.Public = Path.Combine(f.Root, "PublicDesktop");
        Directory.CreateDirectory(f.Library); Directory.CreateDirectory(f.Desktop); Directory.CreateDirectory(f.Public);
        return f;
    }
    private static string Quote(string value) { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
    private static string Item(string id, string source, string original, string flags)
    {
        return "{\"Id\":" + Quote(id) + ",\"Path\":" + Quote(source) + ",\"OriginalPath\":" + Quote(original) + ",\"ManagedShortcut\":true," + flags + "}";
    }
    private static void Profile(Fixture f, params string[] items)
    {
        File.WriteAllText(Path.Combine(f.Profile, "settings.json"), "{\n \"unknownPreference\": \"preserve byte for byte\",\"Items\":[" + String.Join(",", items) + "]}\n", new UTF8Encoding(false));
    }
    private static string Source(Fixture f, string id, string extension, byte[] bytes)
    {
        string path = Path.Combine(f.Library, id + extension); File.WriteAllBytes(path, bytes); return path;
    }
    private static byte[] Bytes(string text) { return Encoding.UTF8.GetBytes(text); }
    private static bool Equal(byte[] left, byte[] right)
    {
        if(left.Length != right.Length) return false;
        for(int i=0; i<left.Length; i++) if(left[i] != right[i]) return false;
        return true;
    }
    private static void Assert(bool condition, string text) { if(!condition) throw new Exception(text); }
    private static int Restore(Fixture f) { return UninstallProfile.Restore(f.Profile, f.Desktop, f.Public); }
    private static void Refused(Fixture f)
    {
        bool refused = false; try { Restore(f); } catch(IOException) { refused = true; }
        Assert(refused, "Unsafe or incomplete fixture must prevent uninstall preparation.");
        Assert(Directory.GetFiles(f.Desktop).Length == 0 && Directory.GetFiles(f.Public).Length == 0, "Failed preflight must not create desktop copies.");
    }
    private static void Pass(string name) { passed++; Console.WriteLine("PASS: " + name); }

    private static void CopiesAndPreservation()
    {
        Fixture f=Make("copies-preservation"); string a=Guid.NewGuid().ToString("N"), b=Guid.NewGuid().ToString("N");
        byte[] first = new byte[] {0, 1, 255, 18, 0, 10}; byte[] second=Bytes("[InternetShortcut]\r\nURL=https://example.invalid/\r\n");
        string source1=Source(f,a,".lnk",first), source2=Source(f,b,".url",second);
        string target1=Path.Combine(f.Desktop,"App.lnk"), target2=Path.Combine(f.Public,"Site.url");
        Profile(f,Item(a,source1,target1,"\"DesktopSourceMoved\":true"),Item(b,source2,target2,"\"PendingPublicMove\":true"));
        string settings=Path.Combine(f.Profile,"settings.json"), backup=settings+".bak";
        byte[] settingsBytes=File.ReadAllBytes(settings); File.WriteAllText(backup,"unrelated backup bytes"); byte[] backupBytes=File.ReadAllBytes(backup);
        Assert(Restore(f)==2,"Both user and shared desktop items must be restored.");
        Assert(Equal(File.ReadAllBytes(target1),first) && Equal(File.ReadAllBytes(target2),second),"Shortcut bytes changed.");
        Assert(Equal(File.ReadAllBytes(settings),settingsBytes) && Equal(File.ReadAllBytes(backup),backupBytes),"Settings or backup was modified.");
        Assert(Equal(File.ReadAllBytes(source1),first) && Equal(File.ReadAllBytes(source2),second),"Library was modified.");
        Restore(f); Assert(Directory.GetFiles(f.Desktop).Length==1 && Directory.GetFiles(f.Public).Length==1,"Retry created duplicate restored shortcuts.");
        Pass("user/shared/pending restore preserves profile, backup, library and exact bytes; retry is idempotent");
    }
    private static void Collision()
    {
        Fixture f=Make("collision"); string id=Guid.NewGuid().ToString("N"); byte[] saved=Bytes("saved shortcut");
        string source=Source(f,id,".lnk",saved), target=Path.Combine(f.Desktop,"App.lnk");
        Profile(f,Item(id,source,target,"\"DesktopSourceMoved\":true"));
        File.WriteAllText(target,"existing user file"); File.WriteAllText(Path.Combine(f.Desktop,"App (2).lnk"),"another user file");
        Restore(f); Assert(File.ReadAllText(target)=="existing user file","Original was overwritten.");
        Assert(File.ReadAllText(Path.Combine(f.Desktop,"App (2).lnk"))=="another user file","Numbered user file was overwritten.");
        Assert(Equal(File.ReadAllBytes(Path.Combine(f.Desktop,"App (3).lnk")),saved),"A free numbered name was not used.");
        Restore(f); Assert(Directory.GetFiles(f.Desktop).Length==3,"Cancelled/retried restoration created a duplicate.");
        Pass("existing desktop names are preserved and a numbered restoration is reused");
    }
    private static void BackupRecovery()
    {
        Fixture f=Make("backup"); string id=Guid.NewGuid().ToString("N"), target=Path.Combine(f.Desktop,"Saved.url");
        Profile(f,Item(id,Source(f,id,".url",Bytes("saved")),target,"\"PendingSourceDelete\":true"));
        string settings=Path.Combine(f.Profile,"settings.json"); File.Copy(settings,settings+".bak"); File.WriteAllText(settings,"{broken");
        Restore(f); Assert(File.ReadAllText(target)=="saved" && File.ReadAllText(settings)=="{broken","Backup recovery must restore copies without rewriting damaged settings.");
        Pass("valid backup restores shortcuts while damaged settings remain unchanged");
    }
    private static void Failures()
    {
        Fixture missing=Make("missing-copy"); string a=Guid.NewGuid().ToString("N"), b=Guid.NewGuid().ToString("N");
        Profile(missing,Item(a,Source(missing,a,".lnk",Bytes("first")),Path.Combine(missing.Desktop,"First.lnk"),"\"DesktopSourceMoved\":true"),
            Item(b,Path.Combine(missing.Library,b+".url"),Path.Combine(missing.Public,"Missing.url"),"\"PendingPublicMove\":true")); Refused(missing);
        Fixture corrupt=Make("corrupt"); File.WriteAllText(Path.Combine(corrupt.Profile,"settings.json"),"invalid"); File.WriteAllText(Path.Combine(corrupt.Profile,"settings.json.bak"),"{}"); Refused(corrupt);
        Fixture orphan=Make("orphan"); File.WriteAllText(Path.Combine(orphan.Library,"orphan.lnk"),"keep"); Refused(orphan);
        Fixture malformed=Make("bad-identity"); Profile(malformed,Item("wrong",Source(malformed,"wrong",".lnk",Bytes("keep")),Path.Combine(malformed.Desktop,"Bad.lnk"),"\"DesktopSourceMoved\":true")); Refused(malformed);
        Fixture outside=Make("outside-library"); string id=Guid.NewGuid().ToString("N"), path=Path.Combine(outside.Root,id+".lnk"); File.WriteAllText(path,"keep");
        Profile(outside,Item(id,path,Path.Combine(outside.Desktop,"Bad.lnk"),"\"DesktopSourceMoved\":true")); Refused(outside);
        Fixture original=Make("outside-desktop"); id=Guid.NewGuid().ToString("N"); Profile(original,Item(id,Source(original,id,".lnk",Bytes("keep")),Path.Combine(original.Root,"Bad.lnk"),"\"DesktopSourceMoved\":true")); Refused(original);
        Fixture empty=Make("empty"); Assert(Restore(empty)==0,"Absent profile and empty library should be a safe no-op.");
        Pass("missing files, unreadable profiles, orphan libraries, bad ownership and non-desktop paths stop preparation before writing");
    }
    private static void HardLinks()
    {
        Fixture f=Make("hard-link"); string id=Guid.NewGuid().ToString("N"), owner=Path.Combine(f.Root,"outside-file.lnk"), source=Path.Combine(f.Library,id+".lnk"); File.WriteAllText(owner,"untrusted hard link");
        Assert(CreateHardLink(source,owner,IntPtr.Zero),"Could not create isolated hard-link fixture. Windows error: "+Marshal.GetLastWin32Error());
        Profile(f,Item(id,source,Path.Combine(f.Desktop,"Bad.lnk"),"\"DesktopSourceMoved\":true")); Refused(f);
        Pass("hard-linked shortcut sources are refused");
    }
    private static void Reparse()
    {
        Fixture f=Make("reparse-library"); string id=Guid.NewGuid().ToString("N"), external=Path.Combine(f.Root,"other-library"); Directory.CreateDirectory(external);
        // This exact, newly created fixture directory is empty. No recursive
        // deletion or existing working/profile path is ever used by tests.
        Directory.Delete(f.Library,false); Junction(f.Library,external);
        File.WriteAllText(Path.Combine(external,id+".lnk"),"untrusted junction source");
        Profile(f,Item(id,Path.Combine(f.Library,id+".lnk"),Path.Combine(f.Desktop,"Bad.lnk"),"\"DesktopSourceMoved\":true")); Refused(f);
        Pass("junction traversal into managed library is refused");

        Fixture desktop=Make("reparse-desktop"); id=Guid.NewGuid().ToString("N"); external=Path.Combine(desktop.Root,"other-desktop"); Directory.CreateDirectory(external);
        Directory.Delete(desktop.Desktop,false); Junction(desktop.Desktop,external);
        Profile(desktop,Item(id,Source(desktop,id,".lnk",Bytes("preserved")),Path.Combine(desktop.Desktop,"Bad.lnk"),"\"DesktopSourceMoved\":true")); Refused(desktop);
        Pass("junction traversal into desktop destination is refused");
    }
    private static void ProfileSelection()
    {
        Fixture f=Make("profile-selection"); string application=Path.Combine(f.Root,"Application"); Directory.CreateDirectory(application);
        string executable=Path.Combine(application,"Yarnelo.exe"), local=Path.Combine(f.Root,"LocalAppData"), marker=Path.Combine(application,"portable.mode");
        Assert(UninstallProfile.ProfileDirectoryForExecutable(executable,local)==Path.Combine(local,"PolkaDock"),"Ordinary installation must select native user profile.");
        File.WriteAllText(marker,""); Assert(UninstallProfile.ProfileDirectoryForExecutable(executable,local)==Path.Combine(application,"Data"),"Existing portable marker must select adjacent Data.");
        File.Delete(marker); Directory.CreateDirectory(marker); bool refused=false;
        try { UninstallProfile.ProfileDirectoryForExecutable(executable,local); } catch(IOException) { refused=true; }
        Assert(refused,"A directory named portable.mode must not select a different profile.");
        Pass("native and portable profile selection agrees with application; directory marker fails closed");
    }
    private static void Junction(string junction, string target)
    {
        Directory.CreateDirectory(junction);
        byte[] substitute=Encoding.Unicode.GetBytes("\\??\\"+target), print=Encoding.Unicode.GetBytes(target);
        byte[] buffer;
        using(var memory=new MemoryStream()) using(var writer=new BinaryWriter(memory))
        {
            writer.Write(0xa0000003u); writer.Write((ushort)(8+substitute.Length+2+print.Length+2)); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)substitute.Length); writer.Write((ushort)(substitute.Length+2)); writer.Write((ushort)print.Length);
            writer.Write(substitute); writer.Write((ushort)0); writer.Write(print); writer.Write((ushort)0); buffer=memory.ToArray();
        }
        using(SafeFileHandle handle=CreateFile(junction,0x40000000u,3,IntPtr.Zero,3,0x02200000u,IntPtr.Zero))
        {
            uint returned; Assert(!handle.IsInvalid && DeviceIoControl(handle,0x000900a4u,buffer,(uint)buffer.Length,IntPtr.Zero,0,out returned,IntPtr.Zero),"Could not create isolated junction fixture.");
        }
    }
    private static int Main(string[] args)
    {
        try
        {
            Assert(args.Length==1 && Path.IsPathRooted(args[0]),"A new absolute synthetic fixture directory is required."); root=Path.GetFullPath(args[0]);
            Assert(!Directory.Exists(root) && !File.Exists(root),"Refusing to reuse an existing directory."); Directory.CreateDirectory(root);
            CopiesAndPreservation(); Collision(); BackupRecovery(); Failures(); HardLinks(); Reparse(); ProfileSelection();
            Console.WriteLine("Uninstall profile tests passed: "+passed+" groups. Synthetic fixtures: "+root); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateHardLink(string link,string existing,IntPtr security);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,uint inputSize,IntPtr output,uint outputSize,out uint returned,IntPtr overlapped);
}
