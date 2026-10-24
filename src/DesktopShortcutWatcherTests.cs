using System;
using System.Collections.Generic;
using System.IO;

namespace Polka {
    // All input is in a caller-provided new fixture directory. Nothing reads
    // the account's real desktop, shell shortcuts, profile or registry.
    internal static class DesktopShortcutWatcherTests {
        static int checks;
        static void Check(bool value,string message) { if(!value) throw new Exception(message); checks++; }
        static bool Has(IList<string> paths,string value) {
            foreach(string path in paths) if(String.Equals(path,value,StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        static string Write(string directory,string name,string value) {
            string path=Path.Combine(directory,name); File.WriteAllText(path,value); return path;
        }
        public static int Main(string[] args) {
            try {
                if(args.Length!=1 || !Path.IsPathRooted(args[0])) throw new ArgumentException("Pass one new absolute fixture directory.");
                string fixture=Path.GetFullPath(args[0]);
                if(Directory.Exists(fixture) || File.Exists(fixture)) throw new IOException("The fixture directory must not already exist.");
                RejectReparseParents(fixture);
                string user=Path.Combine(fixture,"User desktop"),common=Path.Combine(fixture,"Common desktop");
                Directory.CreateDirectory(user); Directory.CreateDirectory(common);
                string existing=Write(user,"Existing.lnk","original");
                Write(common,"Existing public.url","[InternetShortcut]\nURL=https://example.invalid\n");
                DateTime now=new DateTime(2026,10,24,0,0,0,DateTimeKind.Utc);
                var errors=new List<Exception>();
                using(var watcher=new DesktopShortcutWatcher(new[]{user,common,user},errors.Add,delegate { return now; })) {
                    Check(watcher.Scan().Count==0,"enabling baselines existing user/common shortcuts");
                    File.AppendAllText(existing," edited");
                    Check(watcher.Scan().Count==0,"editing an old shortcut does not import it");
                    string added=Write(user,"New game.LNK","partial");
                    Check(watcher.Scan().Count==0,"new shortcut waits for a second observation");
                    File.AppendAllText(added," completed");
                    Check(watcher.Scan().Count==0,"size change restarts stabilization");
                    Check(Has(watcher.Scan(),added),"unchanged size and timestamp become ready");
                    Check(Has(watcher.Scan(),added),"failed imports remain ready without acknowledgement");
                    watcher.Acknowledge(added);
                    Check(!Has(watcher.Scan(),added),"successful duplicate/full/saved result can be acknowledged once");
                    File.AppendAllText(added," later edit");
                    Check(watcher.Scan().Count==0,"an acknowledged item is not duplicated after an edit");

                    string ignored=Write(user,"Not shortcut.txt","text");
                    Write(user,"Temporary.lnk.tmp","copy in progress");
                    Directory.CreateDirectory(Path.Combine(user,"Folder.lnk"));
                    string nested=Path.Combine(user,"Nested"); Directory.CreateDirectory(nested); Write(nested,"Nested.lnk","not a desktop icon");
                    Check(watcher.Scan().Count==0 && watcher.Scan().Count==0,"ordinary/temp files, folders and nested files are ignored");
                    string renamed=Path.Combine(user,"Renamed.url"); File.Move(ignored,renamed);
                    Check(watcher.Scan().Count==0 && Has(watcher.Scan(),renamed),"renaming a file to a supported shortcut is a new event");
                    watcher.Acknowledge(renamed);

                    string zero=Write(common,"New public.url",String.Empty);
                    Check(watcher.Scan().Count==0 && watcher.Scan().Count==0,"empty shortcuts wait without being imported");
                    File.AppendAllText(zero,"[InternetShortcut]\nURL=https://example.invalid\n");
                    Check(watcher.Scan().Count==0 && Has(watcher.Scan(),zero),"common desktop shortcut is ready after completing");
                    watcher.Acknowledge(zero);

                    string locked=Path.Combine(user,"Locked.lnk");
                    using(var stream=new FileStream(locked,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                        stream.WriteByte(42); stream.Flush();
                        Check(watcher.Scan().Count==0 && watcher.Scan().Count==0,"an actively written locked file is not ready");
                    }
                    Check(Has(watcher.Scan(),locked),"unlocking a stable file permits retry");
                    watcher.Acknowledge(locked);

                    string gone=Write(user,"Removed.lnk","temporary");
                    Check(watcher.Scan().Count==0,"a newly observed file waits");
                    File.Delete(gone); Check(watcher.Scan().Count==0,"a deleted candidate is removed without importing");
                    Write(user,"Removed.lnk","recreated");
                    Check(watcher.Scan().Count==0 && Has(watcher.Scan(),gone),"recreating after deletion is a new shortcut");
                    watcher.Acknowledge(gone);

                    string pending=Write(user,"Other new shortcut.lnk","pending");
                    Check(watcher.Scan().Count==0,"another new shortcut begins stabilization");
                    string restored=Write(user,"Restored by dock.lnk","restored");
                    watcher.Acknowledge(restored);
                    IList<string> afterRestore=watcher.Scan();
                    Check(!Has(afterRestore,restored) && Has(afterRestore,pending),"acknowledging a restored shortcut does not rebaseline other pending items");
                    watcher.Acknowledge(pending);
                    Check(watcher.Scan().Count==0,"a restored shortcut stays excluded from automatic import");
                    File.Delete(restored); watcher.Scan();
                    Write(user,"Restored by dock.lnk","new shortcut after deletion");
                    Check(watcher.Scan().Count==0 && Has(watcher.Scan(),restored),"a later deleted/recreated restored path is a new event");
                    watcher.Acknowledge(restored);

                    string failed=Write(user,"Unimportable.lnk","retry");
                    Check(watcher.Scan().Count==0 && Has(watcher.Scan(),failed),"retry candidate starts normally");
                    now=now.AddSeconds(31);
                    Check(!Has(watcher.Scan(),failed),"unacknowledged failures expire after the bounded retry period");
                    File.AppendAllText(failed," changed");
                    Check(watcher.Scan().Count==0 && watcher.Scan().Count==0,"expired failures do not form an endless import queue");
                    Check(File.ReadAllText(failed)=="retry changed","observation never moves or changes original files");

                    watcher.Dispose();
                    string stopped=Write(user,"While disabled.lnk","manual");
                    Check(watcher.Scan().Count==0,"disposing stops observation");
                    watcher.Acknowledge(stopped);
                    using(var enabledAgain=new DesktopShortcutWatcher(new[]{user,common},errors.Add,delegate { return now; }))
                        Check(enabledAgain.Scan().Count==0,"re-enabling baselines shortcuts created while disabled");
                }
                Check(errors.Count==0,"normal file operations need no error callback");
                Console.WriteLine("PASS: "+checks+" desktop observer checks; isolated fixtures: "+fixture); return 0;
            } catch(Exception error) { Console.WriteLine(error); return 1; }
        }
        static void RejectReparseParents(string path) {
            for(string current=path;!String.IsNullOrEmpty(current);current=Path.GetDirectoryName(current)) {
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Fixture path cannot contain a reparse point.");
            }
        }
    }
}
