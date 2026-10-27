using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Polka {
    // All folders, bytes and intents are synthetic; no real profile, desktop,
    // application, elevated token, UAC prompt or installed task is used.
    internal static class SecureSaveTests {
        static int assertions; static string root;
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
        [DllImport("kernel32.dll",SetLastError=true)]
        static extern bool DeviceIoControl(SafeFileHandle file,uint code,IntPtr input,uint inputLength,IntPtr output,uint outputLength,out uint returned,IntPtr overlapped);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool CreateHardLink(string link,string existing,IntPtr security);
        static void Check(bool value,string message) { assertions++;if(!value)throw new Exception(message); }
        static void Refused(Action action,string message) {
            bool refused=false;try {action();}catch(IOException){refused=true;}catch(UnauthorizedAccessException){refused=true;}
            Check(refused,message);
        }
        static string Scenario(string name) {
            string path=Path.Combine(root,name);Check(!Directory.Exists(path)&&!File.Exists(path),"Fresh scenario: "+name);
            Directory.CreateDirectory(path);return path;
        }
        static Store Setup(string name,out string data,out string shared) {
            string scenario=Scenario(name);data=Path.Combine(scenario,"Data");shared=Path.Combine(scenario,"Public");
            string desktop=Path.Combine(scenario,"Desktop");Directory.CreateDirectory(desktop);Directory.CreateDirectory(shared);
            var store=new Store(data,desktop,shared);
            File.WriteAllText(Path.Combine(shared,"Game.url"),"[InternetShortcut]\nURL=https://example.invalid/game\n");
            store.Add(Path.Combine(shared,"Game.url"));
            Check(store.Settings.Items.Count==1&&store.Settings.Items[0].PendingPublicMove,"Shared import is pending and keeps original");
            return store;
        }
        static Store Strict(string data,string shared) {return new Store(data,Path.Combine(Path.GetDirectoryName(data),"Desktop"),shared,true);}
        static Store Normal(string data,string shared) {return new Store(data,Path.Combine(Path.GetDirectoryName(data),"Desktop"),shared);}
        static byte[] MainBytes(string data){return File.ReadAllBytes(Path.Combine(data,"settings.json"));}
        static string[] Fingerprint(string data){return Directory.GetFiles(data,"*",SearchOption.AllDirectories).OrderBy(value=>value).Select(value=>Path.GetFileName(value)+":"+Convert.ToBase64String(File.ReadAllBytes(value))).ToArray();}
        static void Unchanged(string[] before,string data,string message){Check(before.SequenceEqual(Fingerprint(data)),message);}
        static void Junction(string path,string target) {
            Directory.CreateDirectory(path);byte[] substitute=Encoding.Unicode.GetBytes(@"\??\"+target),printable=Encoding.Unicode.GetBytes(target);
            int length=8+substitute.Length+2+printable.Length+2;IntPtr buffer=Marshal.AllocHGlobal(8+length);
            try {
                for(int index=0;index<8+length;index++)Marshal.WriteByte(buffer,index,0);
                Marshal.WriteInt32(buffer,0,unchecked((int)0xa0000003));Marshal.WriteInt16(buffer,4,(short)length);
                Marshal.WriteInt16(buffer,10,(short)substitute.Length);Marshal.WriteInt16(buffer,12,(short)(substitute.Length+2));Marshal.WriteInt16(buffer,14,(short)printable.Length);
                Marshal.Copy(substitute,0,IntPtr.Add(buffer,16),substitute.Length);Marshal.Copy(printable,0,IntPtr.Add(buffer,18+substitute.Length),printable.Length);
                using(var file=CreateFile(path,0x40000000,7,IntPtr.Zero,3,0x02200000,IntPtr.Zero)){uint returned;
                    Check(!file.IsInvalid&&DeviceIoControl(file,0x900a4,buffer,(uint)(8+length),IntPtr.Zero,0,out returned,IntPtr.Zero),"Create owned junction fixture, error="+Marshal.GetLastWin32Error());}
            } finally {Marshal.FreeHGlobal(buffer);}
        }
        static void ReadOnlyContract() {
            string data,shared;var normal=Setup("readonly",out data,out shared);string[] before=Fingerprint(data);
            var helper=Strict(data,shared);Unchanged(before,data,"Strict construction never rewrites profile or backup");
            Refused(helper.Save,"Strict store cannot save a profile");Unchanged(before,data,"Rejected strict save changes no bytes");
            Refused(delegate{helper.Add(Path.Combine(shared,"Game.url"));},"Strict store cannot import into the profile library");
            Refused(delegate{helper.PreparePublicCopies();},"Strict store cannot prepare profile-library copies");
            Refused(delegate{helper.Remove(helper.Settings.Items[0]);},"Strict store cannot remove profile-library items");
            Unchanged(before,data,"Every profile mutation entry point refuses strict context before touching files");
            Check(!helper.CompletePublicMove(helper.Settings.Items[0]),"Move without a prior normal-user journal is refused");
            Refused(delegate{helper.RestorePublic(helper.Settings.Items[0]);},"Restore without prepared intent is refused");
            Check(File.Exists(normal.Settings.Items[0].OriginalPath),"Unprepared helper never deletes shared original");Unchanged(before,data,"Unprepared operations leave profile unchanged");
            string missing=Path.Combine(Scenario("missing"),"Data");
            Refused(delegate{new Store(missing,null,null,true);},"Strict constructor refuses missing profile");Check(!Directory.Exists(missing),"Strict constructor creates no missing directory");
            File.WriteAllText(Path.Combine(data,"settings.json"),"{broken");before=Fingerprint(data);
            Refused(delegate{Strict(data,shared);},"Strict constructor rejects corrupt profile");Unchanged(before,data,"Strict corruption rejection creates no preserve/repair files");
        }
        static Store Move(string name,out string data,out string shared) {
            var normal=Setup(name,out data,out shared);normal.PreparePublicOperation("--public-move",null);
            Check(normal.Settings.Items[0].PendingSourceDelete&&normal.Settings.Items[0].DesktopSourceMoved,"Normal dock durably journals deletion before helper");
            string[] before=Fingerprint(data);var helper=Strict(data,shared);
            Check(helper.CompletePublicMove(helper.Settings.Items[0]),"Read-only helper performs verified shared deletion");
            Unchanged(before,data,"Successful helper move changes no profile/library bytes");
            Check(!File.Exists(helper.Settings.Items[0].OriginalPath),"Prepared shared original was moved");
            var reloaded=Normal(data,shared);
            Check(reloaded.Settings.Items[0].DesktopSourceMoved&&!reloaded.Settings.Items[0].PendingSourceDelete&&!reloaded.Settings.Items[0].PendingPublicMove,"Normal reload reconciles completed move");
            return reloaded;
        }
        static void RestoreAndRestart() {
            string data,shared;var normal=Move("restore-restart",out data,out shared);var item=normal.Settings.Items[0];
            normal.PreparePublicOperation("--public-restore",item.Id);string target=item.LastRestoredPath;string[] before=Fingerprint(data);
            Check(item.PendingPublicRestore&&normal.IsPendingPublicRestorePath(target),"Exact restore target is persisted before elevation and excluded from auto-add");
            var helper=Strict(data,shared);var restarted=Normal(data,shared);
            Check(restarted.Settings.Items[0].PendingPublicRestore&&restarted.IsPendingPublicRestorePath(target),"Parent restart preserves intent before delayed helper writes");
            helper.RestorePublic(helper.Settings.Items[0]);Unchanged(before,data,"Restore helper creates no profile/result/temporary data file");
            Check(File.Exists(target)&&File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(item.Path)),"Helper restores exact journaled path and bytes");
            string[] reconciled=restarted.ReconcilePublicRestoreIntents();
            Check(reconciled.Length==1&&reconciled[0]==target,"Active restarted dock reconciles a late helper's exact path");
            Check(!restarted.Settings.Items[0].PendingPublicRestore&&!restarted.Settings.Items[0].DesktopSourceMoved,"Late completion clears restored intent in ordinary context");
            Check(restarted.Settings.Items[0].LastRestoredPath==target,"Exact restored path remains available for watcher acknowledgment");
            Check(restarted.ReconcilePublicRestoreIntents().Length==0,"Repeated reconciliation is idempotent");
            restarted.Remove(restarted.Settings.Items[0]);Check(restarted.Settings.Items.Count==0&&File.Exists(target),"Removal after restore keeps the public copy");

            normal=Move("restore-before-restart",out data,out shared);item=normal.Settings.Items[0];normal.PreparePublicOperation("--public-restore",item.Id);target=item.LastRestoredPath;
            helper=Strict(data,shared);helper.RestorePublic(helper.Settings.Items[0]);restarted=Normal(data,shared);
            Check(!restarted.Settings.Items[0].PendingPublicRestore&&!restarted.Settings.Items[0].DesktopSourceMoved,"Startup reconciles a helper that already finished");
            Check(restarted.Settings.Items[0].LastRestoredPath==target,"Startup retains exact restore path");
        }
        static void CancellationAndCollisions() {
            string data,shared;var normal=Move("cancel",out data,out shared);var item=normal.Settings.Items[0];normal.PreparePublicOperation("--public-restore",item.Id);
            normal.CancelPublicRestoreIntent(item.Id);
            Check(!item.PendingPublicRestore&&item.LastRestoredPath==null&&item.DesktopSourceMoved,"Known UAC cancellation clears only unfinished restore intent");
            Check(File.Exists(item.Path),"Cancelled operation keeps managed bytes");

            normal=Move("collision",out data,out shared);item=normal.Settings.Items[0];File.WriteAllText(item.OriginalPath,"A different original");
            normal.PreparePublicOperation("--public-restore",item.Id);string target=item.LastRestoredPath;
            Check(target!=item.OriginalPath,"Ordinary dock preselects a collision-free numbered target");
            File.WriteAllText(target,"A later collision");string[] before=Fingerprint(data);var helper=Strict(data,shared);
            Refused(delegate{helper.RestorePublic(helper.Settings.Items[0]);},"New collision after journaling refuses restore");
            Check(File.ReadAllText(target)=="A later collision"&&File.ReadAllText(item.OriginalPath)=="A different original","Neither existing shortcut is overwritten");
            Check(Directory.GetFiles(shared).Length==2,"Helper never selects an unjournaled alternate name");Unchanged(before,data,"Collision failure changes no profile bytes");
            normal=Normal(data,shared);normal.CancelPublicRestoreIntent(item.Id);normal.PreparePublicOperation("--public-restore",item.Id);
            target=normal.Settings.Items[0].LastRestoredPath;helper=Strict(data,shared);helper.RestorePublic(helper.Settings.Items[0]);
            Check(File.Exists(target)&&Directory.GetFiles(shared).Length==3,"Retry journals and restores a new exact free name");

            normal=Move("failed-journal",out data,out shared);item=normal.Settings.Items[0];byte[] bytes=MainBytes(data);
            using(var locked=new FileStream(Path.Combine(data,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.Read))
                Refused(delegate{normal.PreparePublicOperation("--public-restore",item.Id);},"Failed intent persistence is reported before any helper starts");
            Check(!item.PendingPublicRestore&&item.LastRestoredPath==null&&bytes.SequenceEqual(MainBytes(data)),"Failed journal restores in-memory flags and disk bytes");
        }
        static void SubstitutionAndLinks() {
            foreach(bool ancestor in new[]{false,true}) {
                string data,shared;var normal=Setup(ancestor?"ancestor-swap":"data-swap",out data,out shared);normal.PreparePublicOperation("--public-move",null);var helper=Strict(data,shared);
                string swapped=ancestor?Path.GetDirectoryName(data):data;string victim=Path.Combine(root,ancestor?"ancestor-victim":"data-victim");Directory.CreateDirectory(victim);
                string settings=Path.Combine(victim,"settings.json");File.WriteAllText(settings,"{\"Victim\":true}");Directory.Move(swapped,swapped+"-original");Junction(swapped,victim);
                Refused(helper.Save,"Readonly save rejects substitution after constructor");Check(!helper.CompletePublicMove(helper.Settings.Items[0]),"Shared action refuses replaced data ancestry");
                Check(File.ReadAllText(settings)=="{\"Victim\":true}"&&!File.Exists(settings+".bak"),"No victim settings/temp/backup write through substituted directory");Directory.Delete(swapped);
            }
            foreach(bool backup in new[]{false,true}) {
                string data,shared;Setup(backup?"backup-hardlink":"main-hardlink",out data,out shared);string path=Path.Combine(data,"settings.json")+(backup?".bak":"");string victim=Path.Combine(Path.GetDirectoryName(data),"Victim.json");
                File.Copy(Path.Combine(data,"settings.json"),victim);if(File.Exists(path))File.Delete(path);Check(CreateHardLink(path,victim,IntPtr.Zero),"Create isolated profile hardlink");byte[] original=File.ReadAllBytes(victim);
                if(backup){var helper=Strict(data,shared);Refused(helper.Save,"Readonly helper never writes linked backup");}else Refused(delegate{Strict(data,shared);},"Strict constructor rejects hardlinked main");
                Check(original.SequenceEqual(File.ReadAllBytes(victim)),"Hardlinked victim remains unchanged");
            }
            string d,s;var moved=Move("malicious-target",out d,out s);var item=moved.Settings.Items[0];moved.PreparePublicOperation("--public-restore",item.Id);
            string outside=Path.Combine(Path.GetDirectoryName(d),"Outside.url");item.LastRestoredPath=outside;moved.Save();var strict=Strict(d,s);
            Refused(delegate{strict.RestorePublic(strict.Settings.Items[0]);},"Helper refuses a journal target outside the shared desktop");Check(!File.Exists(outside),"Outside journal target is never created");

            moved=Move("restore-hardlink",out d,out s);item=moved.Settings.Items[0];moved.PreparePublicOperation("--public-restore",item.Id);string target=item.LastRestoredPath;
            string hardVictim=Path.Combine(Path.GetDirectoryName(d),"Victim.url");File.Copy(item.Path,hardVictim);Check(CreateHardLink(target,hardVictim,IntPtr.Zero),"Create exact-target hardlink fixture");byte[] prior=File.ReadAllBytes(hardVictim);strict=Strict(d,s);
            Refused(delegate{strict.RestorePublic(strict.Settings.Items[0]);},"Identical bytes do not permit a hardlinked restore target");Check(prior.SequenceEqual(File.ReadAllBytes(hardVictim)),"Restore hardlink target is never written");
        }
        static void PartialWriteCleanup() {
            string scenario=Scenario("partial-write"),path=Path.Combine(scenario,"New.url");
            var method=typeof(Store).GetMethod("WriteNewPublicShortcut",BindingFlags.Static|BindingFlags.NonPublic);bool failed=false;
            try {method.Invoke(null,new object[]{path,null});}catch(TargetInvocationException){failed=true;}
            Check(failed&&!File.Exists(path),"A failure after exclusive creation deletes only its own incomplete file by handle");

            // Call the creation primitive after a prepared directory was
            // replaced, modelling a race AFTER a caller's ancestry check.
            // The final handle check must reject it before writing bytes.
            scenario=Scenario("post-create-final-path");string victim=Path.Combine(scenario,"Victim"),alias=Path.Combine(scenario,"Prepared");Directory.CreateDirectory(victim);Junction(alias,victim);
            failed=false;
            try {method.Invoke(null,new object[]{Path.Combine(alias,"New.url"),Encoding.UTF8.GetBytes("must not reach the victim")});}
            catch(TargetInvocationException error){failed=error.InnerException is IOException;}
            Check(failed,"Handle-path verification rejects a post-check ancestor redirection before content is written");
            Check(Directory.GetFileSystemEntries(victim).Length==0,"Redirected newly created file is removed by its own handle");
            Directory.Delete(alias);
        }
        static void InterruptedAndPartialFlows() {
            string data,shared;var normal=Setup("move-cancel",out data,out shared);normal.PreparePublicOperation("--public-move",null);
            var reloaded=Normal(data,shared);var item=reloaded.Settings.Items[0];
            Check(!item.DesktopSourceMoved&&!item.PendingSourceDelete&&item.PendingPublicMove&&File.Exists(item.OriginalPath),"Cancelled move without a child reconciles original-present intent safely");

            normal=Setup("partial-move",out data,out shared);
            string secondSource=Path.Combine(shared,"Second.url");File.WriteAllText(secondSource,"[InternetShortcut]\nURL=https://example.invalid/second\n");
            normal.Add(secondSource);normal.PreparePublicOperation("--public-move",null);
            string victim=Path.Combine(Path.GetDirectoryName(data),"Second-victim.url");File.Copy(secondSource,victim);File.Delete(secondSource);
            Check(CreateHardLink(secondSource,victim,IntPtr.Zero),"Create partial-batch source hardlink");byte[] victimBytes=File.ReadAllBytes(victim);
            string[] before=Fingerprint(data);var helper=Strict(data,shared);
            Check(helper.CompletePublicMove(helper.Settings.Items[0]),"First prepared batch item completes");
            Check(!helper.CompletePublicMove(helper.Settings.Items[1]),"Unsafe later batch item fails closed");
            Unchanged(before,data,"Partially completed helper batch still makes zero profile writes");
            reloaded=Normal(data,shared);
            Check(reloaded.Settings.Items[0].DesktopSourceMoved&&!reloaded.Settings.Items[0].PendingPublicMove,"Completed batch item recovers as moved");
            Check(!reloaded.Settings.Items[1].DesktopSourceMoved&&reloaded.Settings.Items[1].PendingPublicMove,"Failed batch item recovers as pending with source kept");
            Check(victimBytes.SequenceEqual(File.ReadAllBytes(victim))&&File.Exists(secondSource)&&reloaded.Settings.Items.All(value=>File.Exists(value.Path)),"Partial failure keeps all library copies and unsafe source bytes");

            normal=Move("reconcile-save-failure",out data,out shared);item=normal.Settings.Items[0];normal.PreparePublicOperation("--public-restore",item.Id);
            string target=item.LastRestoredPath;helper=Strict(data,shared);var restarted=Normal(data,shared);helper.RestorePublic(helper.Settings.Items[0]);byte[] journal=MainBytes(data);
            using(var locked=new FileStream(Path.Combine(data,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.Read)) {
                Check(restarted.ReconcilePublicRestoreIntents().Length==0,"Failed reconciliation save reports no completion to the watcher");
                Check(restarted.Settings.Items[0].PendingPublicRestore&&restarted.IsPendingPublicRestorePath(target),"Failed reconciliation persistence retains exact future-path suppression");
                Check(journal.SequenceEqual(MainBytes(data)),"Failed reconciliation keeps the durable prepared journal unchanged");
            }
            Check(restarted.ReconcilePublicRestoreIntents().SequenceEqual(new[]{target}),"Reconciliation can safely retry after transient profile lock");
        }
        static void ScopedMoveBatches() {
            string data,shared;var normal=Setup("scoped-move",out data,out shared);string oldId=normal.Settings.Items[0].Id;
            normal.PreparePublicOperation("--public-move",oldId);normal=Normal(data,shared); // previous UAC request cancelled
            Check(normal.Settings.Items[0].PendingPublicMove&&!normal.Settings.Items[0].PendingSourceDelete,"Cancelled old item is still pending but no longer prepared");
            string newSource=Path.Combine(shared,"New.url");File.WriteAllText(newSource,"[InternetShortcut]\nURL=https://example.invalid/new\n");
            DockItem added=normal.Add(newSource);normal.PreparePublicOperation("--public-move",added.Id);
            Check(!normal.Settings.Items[0].PendingSourceDelete&&added.PendingSourceDelete,"Scoped journal prepares only the newly requested item");
            string[] before=Fingerprint(data);var helper=Strict(data,shared);DockItem[] selected=helper.GetPreparedPublicMoveItems(added.Id.ToUpperInvariant());
            Check(selected.Length==1&&selected[0].Id==added.Id,"Child selection accepts a unique canonical GUID without expanding its scope");
            foreach(var item in selected)Check(helper.CompletePublicMove(item),"Scoped helper completes requested new item");
            Check(File.Exists(normal.Settings.Items[0].OriginalPath)&&!File.Exists(newSource),"Old cancelled source stays on desktop while new source moves");
            Unchanged(before,data,"Scoped helper batch performs zero profile or library writes");
            var reloaded=Normal(data,shared);
            Check(reloaded.Settings.Items[0].PendingPublicMove&&!reloaded.Settings.Items[0].DesktopSourceMoved,"Old declined item stays pending after new batch reconciliation");
            Check(reloaded.Settings.Items[1].DesktopSourceMoved&&!reloaded.Settings.Items[1].PendingPublicMove,"New scoped item recovers as moved");

            normal=Setup("invalid-move-batches",out data,out shared);
            string second=Path.Combine(shared,"Second.url");File.WriteAllText(second,"[InternetShortcut]\nURL=https://example.invalid/second\n");normal.Add(second);
            string firstId=normal.Settings.Items[0].Id,secondId=normal.Settings.Items[1].Id,unknown=Guid.NewGuid().ToString("N");
            before=Fingerprint(data);
            foreach(string value in new[]{"",unknown,firstId+","+unknown,firstId+","+firstId,firstId+",",Guid.NewGuid().ToString(),"not-guid",new string('a',1000)}) {
                string invalid=value;
                Refused(delegate{normal.PreparePublicOperation("--public-move",invalid);},"Invalid/unknown/duplicate batch is rejected before parent journaling");
                Unchanged(before,data,"Rejected batch changes no profile bytes");
                Check(normal.Settings.Items.All(item=>!item.PendingSourceDelete&&File.Exists(item.OriginalPath)),"Rejected batch prepares or deletes no item");
            }
            normal.PreparePublicOperation("--public-move",firstId);helper=Strict(data,shared);before=Fingerprint(data);
            Refused(delegate{helper.GetPreparedPublicMoveItems(firstId+","+secondId);},"Child refuses an entire mixed prepared/unprepared batch");
            Refused(delegate{helper.GetPreparedPublicMoveItems(firstId+","+unknown);},"Child refuses a missing requested ID before any action");
            Refused(delegate{helper.GetPreparedPublicMoveItems(firstId+","+firstId);},"Child refuses duplicate requested IDs");
            Refused(delegate{helper.GetPreparedPublicMoveItems(null);},"Manual child selection rejects an unprepared pending item instead of partially executing");
            Check(normal.Settings.Items.All(item=>File.Exists(item.OriginalPath)),"Failed child batch selection deletes no earlier valid source");
            Unchanged(before,data,"Rejected child batches make no profile writes");
            normal.PreparePublicOperation("--public-move",null);helper=Strict(data,shared);selected=helper.GetPreparedPublicMoveItems(null);
            Check(selected.Length==2,"Null batch retains explicit manual-menu all-pending behavior after all are journaled");
        }
        public static int Main(string[] args) {
            try {
                if(args.Length!=2||!Path.IsPathRooted(args[0]))throw new Exception("A fresh absolute fixture path is required");root=Path.GetFullPath(args[0]);
                if(Directory.Exists(root)||File.Exists(root))throw new Exception("Fixture root already exists");Directory.CreateDirectory(root);
                Check(IntPtr.Size==(args[1]=="x86"?4:8),"Actual fixture bitness matches the selected platform");ReadOnlyContract();RestoreAndRestart();CancellationAndCollisions();SubstitutionAndLinks();PartialWriteCleanup();InterruptedAndPartialFlows();ScopedMoveBatches();
                Console.WriteLine("PASS: "+assertions+" read-only helper assertions; ordinary-user journaling, no elevated profile writes, restart/cancel/collision recovery and safe restoration verified; pointer size="+IntPtr.Size+".");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
