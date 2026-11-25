using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Polka {
    internal static class DesktopShortcutValidationTests {
        static int checks;
        static void Check(bool value,string message) { if(!value) throw new Exception(message); checks++; }
        static string Write(string root,string name,string contents,Encoding encoding=null) {
            string path=Path.Combine(root,name); File.WriteAllText(path,contents,encoding??Encoding.UTF8); return path;
        }
        static void Ready(string path,bool expected,string message) {
            byte[] before=File.ReadAllBytes(path);
            Check(Native.IsDesktopShortcutReady(path)==expected,message);
            Check(File.ReadAllBytes(path).SequenceEqual(before),message+": source remains unchanged");
        }
        [STAThread] public static int Main(string[] args) {
            try {
                if(args.Length!=1 || !Path.IsPathRooted(args[0])) throw new ArgumentException("Pass one new absolute fixture directory.");
                string root=Path.GetFullPath(args[0]);
                if(Directory.Exists(root)||File.Exists(root)) throw new IOException("Fixture directory must not exist.");
                for(string parent=root;!String.IsNullOrEmpty(parent);parent=Path.GetDirectoryName(parent))
                    if((Directory.Exists(parent)||File.Exists(parent)) && (File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)
                        throw new IOException("Fixture path cannot contain a reparse point.");
                Directory.CreateDirectory(root);
                foreach(string value in new[]{"[InternetShortcut]\n", "[InternetShortcut]\nURL=\n", "[InternetShortcut]\nURL=https://\n", "[Other]\nURL=https://example.invalid\n", "[InternetShortcut]\nURL=relative/game\n", "[InternetShortcut]\nURL=https://example.invalid\nURL=\n"})
                    Ready(Write(root,"incomplete.url",value),false,"incomplete or ambiguous Internet shortcuts wait for completion");
                Ready(Write(root,"valid.url","[InternetShortcut]\nURL=https://example.invalid/game?id=123\n"),true,"complete Internet shortcut is ready");
                Ready(Write(root,"steam.url","[InternetShortcut]\nURL=steam://rungameid/730\n"),true,"custom game URI remains supported");
                Ready(Write(root,"unicode.url","[InternetShortcut]\nURL=https://example.invalid/игра\n",Encoding.Unicode),true,"Unicode Internet shortcut is supported");

                string link=Path.Combine(root,"Valid game.lnk");
                CreateLink(link,Path.Combine(root,"target-does-not-exist.exe"),IntPtr.Zero);
                Ready(link,true,"COM-created link does not require its target to exist");
                byte[] complete=File.ReadAllBytes(link);
                foreach(int length in new[]{0,4,75,76,complete.Length/2,complete.Length-4,complete.Length-1}) {
                    string truncated=Path.Combine(root,"Truncated.lnk");
                    File.WriteAllBytes(truncated,complete.Take(length).ToArray());
                    Ready(truncated,false,"truncated Shell Link waits for the full file and terminal block ("+length+")");
                }
                byte[] damaged=(byte[])complete.Clone(); damaged[4]^=255;
                string badClass=Path.Combine(root,"Wrong class.lnk"); File.WriteAllBytes(badClass,damaged);
                Ready(badClass,false,"a wrong Shell Link class identifier is rejected");
                byte[] empty=complete.Take(76).Concat(new byte[4]).ToArray();
                Array.Clear(empty,20,4);
                string emptyLink=Path.Combine(root,"Empty target.lnk"); File.WriteAllBytes(emptyLink,empty);
                Ready(emptyLink,false,"a header without any target does not become an automatic shortcut");
                string trailing=Path.Combine(root,"Trailing data.lnk"); File.WriteAllBytes(trailing,complete.Concat(new byte[]{42}).ToArray());
                Ready(trailing,false,"incomplete trailing data does not pass COM's permissive loader");
                Ready(Write(root,"Not a shortcut.txt","[InternetShortcut]\nURL=https://example.invalid\n"),false,"ordinary files are not auto-added");

                IntPtr pidl=IntPtr.Zero;
                try {
                    uint attributes;
                    Marshal.ThrowExceptionForHR(SHParseDisplayName("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",IntPtr.Zero,out pidl,0,out attributes));
                    string namespaceLink=Path.Combine(root,"Computer namespace.lnk");
                    CreateLink(namespaceLink,null,pidl);
                    Ready(namespaceLink,true,"IDList-only namespace targets remain supported without path resolution");
                } finally { if(pidl!=IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
                string locked=Path.Combine(root,"Writing.url");
                using(var writer=new FileStream(locked,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    writer.WriteByte(42); writer.Flush();
                    Check(!Native.IsDesktopShortcutReady(locked),"an active writer prevents automatic import");
                }
                string oversized=Path.Combine(root,"Oversized.lnk");
                using(var writer=new FileStream(oversized,FileMode.CreateNew,FileAccess.Write)) writer.SetLength(4*1024*1024+1);
                Check(!Native.IsDesktopShortcutReady(oversized),"shortcut validation is bounded");
                CheckApplicationTargets(root);
                CheckSteamLibraries(root);
                Console.WriteLine("PASS: "+checks+" automatic shortcut readiness and target checks; no targets launched or resolved; fixtures: "+root);
                return 0;
            } catch(Exception error) { Console.WriteLine(error); return 1; }
        }
        static void CheckApplicationTargets(string root) {
            string programDirectory=Path.Combine(root,"Installed Game");
            Directory.CreateDirectory(programDirectory);
            string program=Write(programDirectory,"Game.exe","fake executable, never launched");
            string link=Path.Combine(root,"Installed Game.lnk");
            CreateLink(link,program,IntPtr.Zero);
            Check(Native.FindMissingApplication(program)==null && Native.FindMissingApplication(link)==null,
                "existing executable and real Windows shortcut are preserved");
            byte[] original=File.ReadAllBytes(link);
            File.Delete(program);
            Directory.Delete(programDirectory);
            string direct=Native.FindMissingApplication(program), imported=Native.FindMissingApplication(link);
            Check(direct!=null && imported!=null && direct!=imported,
                "a missing executable under its original fixed volume is detectable directly and through a real shortcut");
            Check(imported==Native.FindMissingApplication(link) && original.SequenceEqual(File.ReadAllBytes(link)),
                "missing evidence is stable and remains read-only");

            byte[] replacedVolume=(byte[])original.Clone();
            uint flags=BitConverter.ToUInt32(original,20);
            int info=76+((flags&1)!=0?2+BitConverter.ToUInt16(original,76):0);
            int serial=info+(int)BitConverter.ToUInt32(original,info+12)+8;
            replacedVolume[serial]^=1;
            File.WriteAllBytes(link,replacedVolume);
            Check(Native.FindMissingApplication(link)==null,"a replaced drive letter cannot establish absence");
            File.WriteAllBytes(link,original);
            Directory.CreateDirectory(programDirectory);
            Write(programDirectory,"Game.exe","fake program restored before confirmation");
            Check(Native.FindMissingApplication(link)==null,"a reinstalled executable clears missing evidence");
            Check(Native.FindMissingApplication(Path.Combine(root,"valid.url"))==null &&
                Native.FindMissingApplication(Path.Combine(root,"Computer namespace.lnk"))==null &&
                Native.FindMissingApplication(programDirectory)==null &&
                Native.FindMissingApplication("shell:RecycleBinFolder")==null &&
                Native.FindMissingApplication(@"\\unreachable.invalid\share\Game.exe")==null &&
                Native.FindMissingApplication(@"\\?\C:\NoSuch-Yarnelo.exe")==null &&
                Native.FindMissingApplication(Path.Combine(programDirectory,"Game.exe:alternate"))==null,
                "URLs, folders, namespaces, network, extended and alternate stream paths are never inferred as uninstallations");
            using(var writer=new FileStream(link,FileMode.Open,FileAccess.Write,FileShare.None))
                Check(Native.FindMissingApplication(link)==null,"a locked shortcut is not considered broken or missing");
        }
        static void CheckSteamLibraries(string root) {
            string steam=Path.Combine(root,"Steam fixture");
            string other=Path.Combine(root,"Steam secondary");
            Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
            Directory.CreateDirectory(Path.Combine(other,"steamapps"));
            Write(steam,"steam.exe","fake Steam executable, never launched");
            string config=Path.Combine(steam,"steamapps","libraryfolders.vdf");
            string escapedSteam=steam.Replace("\\","\\\\"), escapedOther=other.Replace("\\","\\\\");
            string complete="\"libraryfolders\" { \"0\" { \"path\" \""+escapedSteam+"\" \"apps\" { } } "+
                "\"1\" { \"path\" \""+escapedOther+"\" \"apps\" { } } }";
            File.WriteAllText(config,complete);
            string absent=Native.ConfirmSteamLibraries(steam,"730");
            Check(absent!=null && absent==Native.ConfirmSteamLibraries(steam,"730"),
                "two readable Steam libraries corroborate a genuinely absent manifest consistently");
            string manifest=Write(Path.Combine(other,"steamapps"),"appmanifest_730.acf","fixture");
            Check(Native.ConfirmSteamLibraries(steam,"730")==null,"a manifest in a secondary Steam library preserves its shortcut");
            File.Delete(manifest);
            File.WriteAllText(config,complete.Replace("\"apps\" { }","\"apps\" { \"730\" \"123456\" }"));
            Check(Native.ConfirmSteamLibraries(steam,"730")==null,"an indexed Steam app stays even while its manifest is temporarily absent");
            File.WriteAllText(config,complete.Substring(0,complete.Length-1));
            Check(Native.ConfirmSteamLibraries(steam,"730")==null,"an incomplete library map cannot establish absence");
            File.WriteAllText(config,complete);
            Directory.Delete(Path.Combine(other,"steamapps"));
            Check(Native.ConfirmSteamLibraries(steam,"730")==null,"an unavailable secondary library preserves Steam shortcuts");
        }
        static void CreateLink(string path,string target,IntPtr pidl) {
            object value=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"),true));
            try {
                var link=(IShellLinkFixture)value;
                if(pidl!=IntPtr.Zero) link.SetIDList(pidl); else link.SetPath(target);
                link.SetDescription("Yarnelo test 🎮");
                ((IPersistFile)value).Save(path,true);
            } finally { Marshal.FinalReleaseComObject(value); }
        }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
        static extern int SHParseDisplayName(string name,IntPtr context,out IntPtr pidl,uint mask,out uint attributes);

        [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkFixture {
            void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int size,IntPtr data,uint flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string value);
            void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string value);
            void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string value);
            void GetHotkey(out short value);
            void SetHotkey(short value);
            void GetShowCmd(out int value);
            void SetShowCmd(int value);
            void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int size,out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string value,int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string value,uint reserved);
            void Resolve(IntPtr window,uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)]string value);
        }
    }
}
