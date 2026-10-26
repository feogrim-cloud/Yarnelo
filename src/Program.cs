using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Yarnelo")]
[assembly: AssemblyDescription("Yarnelo — shortcut dock for Windows")]
[assembly: AssemblyCompany("Yarnelo")]
[assembly: AssemblyProduct("Yarnelo")]
[assembly: AssemblyVersion("1.9.1.0")]
[assembly: AssemblyFileVersion("1.9.1.0")]

namespace Polka {
    internal static class Program {
        internal static bool TestMode;
        internal static string DataDir;
        internal static string DefaultDataDirectory {
            get {
                return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"portable.mode"))
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data")
                    // Keep the legacy profile location so a renamed build opens
                    // existing shortcuts rather than starting a second library.
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PolkaDock");
            }
        }
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        // Stable private identities also make an old shortcut activate Yarnelo.
        internal static readonly uint ShowMessage = RegisterWindowMessage("Polka.Show.v1");

        [STAThread] public static int Main(string[] args) {
            if(File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"portable.mode"))) DataDir=DefaultDataDirectory;
            string helper=null, helperId=null;
            bool startupLaunch=false;
            for (int i=0;i<args.Length;i++) {
                if(args[i]=="--data-dir" && i+1<args.Length) { DataDir=Path.GetFullPath(args[++i]); TestMode=true; }
                else if(args[i]=="--public-move") helper=args[i];
                else if(args[i]=="--public-restore" && i+1<args.Length) { helper=args[i]; helperId=args[++i]; }
                else if(args[i]=="--startup") startupLaunch=true;
            }
            if(helper!=null) {
                try {
                    if(DataDir==null) return 2;
                    string allowed=DefaultDataDirectory;
                    if(!String.Equals(Path.GetFullPath(DataDir).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(allowed).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)) return 2;
                    for(string check=DataDir; !String.IsNullOrEmpty(check); check=Path.GetDirectoryName(check)) {
                        if(Directory.Exists(check) && (File.GetAttributes(check) & FileAttributes.ReparsePoint)!=0) return 2;
                    }
                    var saved=new Store(DataDir,null,null,true);
                    if(helper=="--public-move") {
                        foreach(var item in saved.Settings.Items.ToArray()) if(item.PendingPublicMove && !saved.CompletePublicMove(item)) return 2;
                    } else {
                        var item=saved.Settings.Items.Find(x=>x.Id==helperId);
                        if(item==null) return 2;
                        saved.RestorePublic(item);
                    }
                    return 0;
                } catch(Exception ex) {
                    MessageBox.Show(L10n.T("ActionFinishFailed")+ex.Message,"Yarnelo",MessageBoxButton.OK,MessageBoxImage.Warning); return 2;
                }
            }
            StartupTrace("launch source="+(startupLaunch?"sign-in":"manual"));
            bool first;
            using(var mutex=new Mutex(true, TestMode ? "Local\\Polka.Test."+DataDir.GetHashCode() : "Local\\Polka.DesktopDock.v1",out first)) {
                if(!first) {
                    StartupTrace("already-running");
                    if(!startupLaunch) PostMessage(new IntPtr(0xffff), ShowMessage, IntPtr.Zero,IntPtr.Zero);
                    return 0;
                }
                var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
                    try { File.AppendAllText(Path.Combine(DataDir ?? DefaultDataDirectory,"error.log"), DateTime.Now.ToString("s")+" "+e.Exception+Environment.NewLine); } catch {}
                    MessageBox.Show(L10n.T("ActionFailed")+e.Exception.Message,"Yarnelo",MessageBoxButton.OK,MessageBoxImage.Warning);
                    e.Handled=true;
                };
                try {
                    var store=new Store(DataDir);
                    string recoveryNotice=store.LastNotice;
                    string startupNotice=null;
                    if(!TestMode) {
                        try {
                            var registration=new StartupRegistration(Native.ExePath);
                            if(registration.MigrateEnabledLegacy()) StartupTrace("enabled legacy startup migrated");
                            bool enabled=registration.Enabled;
                            if(store.Settings.LaunchAtLogin!=enabled) { store.Settings.LaunchAtLogin=enabled; store.Save(); }
                        } catch(Exception ex) {
                            startupNotice=L10n.T("StartupMigrationFailed")+Native.StartupError(ex);
                            StartupTrace("startup-registration-error: "+ex.Message);
                        }
                    }
                    store.PreparePublicCopies();
                    if(store.IsFirstRun) { store.Add("shell:MyComputerFolder"); store.Add("shell:RecycleBinFolder"); }
                    var dock=new DockWindow(store); app.MainWindow=dock;
                    dock.Loaded+=delegate { StartupTrace("ready"); };
                    dock.Show();
                    if(!String.IsNullOrEmpty(recoveryNotice)) dock.Toast(recoveryNotice);
                    if(!String.IsNullOrEmpty(startupNotice)) dock.Toast(startupNotice);
                    app.Run(); return 0;
                } catch(Exception ex) {
                    StartupTrace("failed: "+ex.Message);
                    MessageBox.Show(L10n.T("StartFailed")+ex.Message,"Yarnelo",MessageBoxButton.OK,MessageBoxImage.Error); return 1;
                }
            }
        }
        static void StartupTrace(string message) {
            if(TestMode) return;
            try {
                string directory=DataDir ?? DefaultDataDirectory;
                Directory.CreateDirectory(directory);
                string path=Path.Combine(directory,"startup.log");
                if(File.Exists(path) && new FileInfo(path).Length>65536) File.WriteAllText(path,String.Empty);
                File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" pid="+System.Diagnostics.Process.GetCurrentProcess().Id+" "+message+Environment.NewLine);
            } catch { /* Diagnostics must never prevent the dock from starting. */ }
        }
    }
}
