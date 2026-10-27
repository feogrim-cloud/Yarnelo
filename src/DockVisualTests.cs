using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Polka {
    // Exercise the real production controls without creating an HWND, desktop
    // host, tray icon or any window which could take focus from the user.
    internal static class DockVisualTests {
        static int assertions;
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string message) { if(!value) throw new Exception(message); assertions++; }
        static T Field<T>(object value,string name) { return (T)value.GetType().GetField(name,Private).GetValue(value); }
        static void Near(double value,double expected,string message) { Check(Math.Abs(value-expected)<1.1,message+": "+value+" vs "+expected); }
        static void Arrange(FrameworkElement content,DockWindow window) {
            var size=new Size(window.Width,window.Height);
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
        }
        static Rect Bounds(FrameworkElement element,Visual ancestor) {
            return element.TransformToAncestor(ancestor).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
        }
        static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject {
            for(int index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++) {
                var child=VisualTreeHelper.GetChild(parent,index);
                if(child is T) yield return (T)child;
                foreach(var item in Descendants<T>(child)) yield return item;
            }
        }
        static void WithinWindow(FrameworkElement element,FrameworkElement content,DockWindow dock,string message) {
            Rect bounds=Bounds(element,content); bounds.Offset(content.Margin.Left,content.Margin.Top);
            Check(bounds.Left>=-1 && bounds.Top>=-1 && bounds.Right<=dock.Width+1 && bounds.Bottom<=dock.Height+1,message+": "+bounds);
        }
        static void PumpUntil(Func<bool> condition,string message) {
            var watch=Stopwatch.StartNew();
            while(!condition() && watch.Elapsed.TotalSeconds<15) {
                var frame=new DispatcherFrame();
                var timer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(5)};
                timer.Tick+=delegate {timer.Stop();frame.Continue=false;};
                timer.Start(); Dispatcher.PushFrame(frame);
            }
            Check(condition(),message);
        }
        static bool IconsIdle(DockWindow dock) {
            return Field<object>(dock,"activeIconLoad")==null && Field<IDictionary>(dock,"pendingIcons").Count==0;
        }
        static ImageSource IconDrawing(Color color) {
            var image=new DrawingImage(new GeometryDrawing(new SolidColorBrush(color),null,new RectangleGeometry(new Rect(0,0,48,48))));
            image.Freeze(); return image;
        }
        static void FixtureItems(Store store,string prefix,int count) {
            store.Settings.Items.Clear();
            for(int index=0;index<count;index++) store.Settings.Items.Add(new DockItem {Id=prefix+index,Name="Fixture",Path=prefix+index});
        }
        static void CheckIconCacheLifetime(string directory) {
            string desktop=Path.Combine(directory,"CacheDesktop"); Directory.CreateDirectory(desktop);
            var store=new Store(Path.Combine(directory,"CacheData"),desktop,desktop);
            store.Settings.Items.Clear();
            var dock=new DockWindow(store);
            var cache=Field<Dictionary<string,ImageSource>>(dock,"iconCache");
            int extractions=0;
            dock.IconLoader=path=> {Interlocked.Increment(ref extractions);return Native.GetIcon(path);};
            // The audit's actual native loader scenario: 180 unique local
            // fixtures across six panels, never more than 30 live shortcuts.
            for(int batch=0;batch<6;batch++) {
                store.Settings.Items.Clear();
                for(int index=0;index<30;index++) {
                    string path=Path.Combine(desktop,"cache-"+(batch*30+index)+".txt");
                    File.WriteAllText(path,"Isolated icon fixture; never launched.");
                    store.Settings.Items.Add(new DockItem {Id="cache-"+(batch*30+index),Name="Fixture",Path=path});
                }
                dock.ApplySettings(new Rect(0,0,1920,1040));
                PumpUntil(()=>IconsIdle(dock),"native cache batch "+batch+" completes without a visible window");
                Check(cache.Count==30 && cache.Keys.All(path=>store.Settings.Items.Any(item=>String.Equals(item.Path,path,StringComparison.OrdinalIgnoreCase))),"cache contains only the 30 current paths after batch "+batch);
            }
            Check(extractions==180,"each unique current native fixture is extracted once");
            var replacement=IconDrawing(Colors.LimeGreen);
            dock.IconLoader=path=>replacement;
            var first=Field<List<DockWindow.IconView>>(dock,"views")[0];
            typeof(DockWindow).GetMethod("LoadIcon",Private).Invoke(dock,new object[]{first,true});
            PumpUntil(()=>IconsIdle(dock),"refresh completes with a full icon cache");
            Check(cache.Count==30 && Object.ReferenceEquals(first.Image.Source,replacement),"refresh replaces an existing cache entry even when all 30 slots are used");
            var retired=Field<List<DockWindow.IconView>>(dock,"views").ToArray();
            store.Settings.Items.Clear(); dock.ApplySettings(new Rect(0,0,1920,1040));
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(cache.Count==0 && IconsIdle(dock) && retired.All(view=>view.Image.Source==null),"clearing all items releases cached images and retired view sources after the 180-icon audit scenario");

            var blue=IconDrawing(Colors.CornflowerBlue);
            using(var entered=new ManualResetEventSlim()) using(var release=new ManualResetEventSlim()) {
                extractions=0;
                dock.IconLoader=path=> {Interlocked.Increment(ref extractions);entered.Set();if(!release.Wait(15000)) throw new TimeoutException("Fixture icon gate");return blue;};
                try {
                    FixtureItems(store,"fixture:shared",1);
                    store.Settings.Items.Add(new DockItem {Id="shared-second",Name="Fixture",Path="FIXTURE:SHARED0"});
                    dock.ApplySettings(new Rect(0,0,1920,1040));
                    PumpUntil(()=>entered.IsSet,"controlled icon extraction starts");
                    var oldViews=Field<List<DockWindow.IconView>>(dock,"views").ToArray();
                    for(int repeat=0;repeat<8;repeat++) dock.ApplySettings(new Rect(0,0,1920,1040));
                    Check(extractions==1 && Field<IDictionary>(dock,"pendingIcons").Count==1,"same-path rebuilds and case-insensitive duplicate views share one pending extraction");
                    release.Set(); PumpUntil(()=>IconsIdle(dock),"shared extraction reaches current rebuilt views");
                    Check(cache.Count==1 && Field<List<DockWindow.IconView>>(dock,"views").All(view=>Object.ReferenceEquals(view.Image.Source,blue)),"one shared result is applied to both current views");
                    Check(oldViews.All(view=>view.Image.Source==null),"the shared completion never mutates retired views");
                } finally {release.Set();}
            }
            store.Settings.Items.Clear(); dock.ApplySettings(new Rect(0,0,1920,1040));
            using(var entered=new ManualResetEventSlim()) using(var release=new ManualResetEventSlim())
            using(var secondEntered=new ManualResetEventSlim()) using(var secondRelease=new ManualResetEventSlim()) {
                extractions=0;
                dock.IconLoader=path=> {
                    if(Interlocked.Increment(ref extractions)==1) {entered.Set();if(!release.Wait(15000)) throw new TimeoutException("Retired fixture gate");return blue;}
                    secondEntered.Set();if(!secondRelease.Wait(15000)) throw new TimeoutException("Current fixture gate");return replacement;
                };
                try {
                    FixtureItems(store,"fixture:retired",1); dock.ApplySettings(new Rect(0,0,1920,1040));
                    PumpUntil(()=>entered.IsSet,"removable extraction starts");
                    var oldView=Field<List<DockWindow.IconView>>(dock,"views")[0];
                    for(int batch=0;batch<8;batch++) {
                        FixtureItems(store,"fixture:discarded-"+batch+"-",30); dock.ApplySettings(new Rect(0,0,1920,1040));
                        Check(Field<IDictionary>(dock,"pendingIcons").Count<=30 && extractions==1,"rapid rebuild keeps only current pending paths and one native extraction: "+batch);
                    }
                    FixtureItems(store,"fixture:retired",1); dock.ApplySettings(new Rect(0,0,1920,1040));
                    var currentView=Field<List<DockWindow.IconView>>(dock,"views")[0];
                    release.Set(); PumpUntil(()=>secondEntered.IsSet,"the re-added path gets its own fresh extraction");
                    Check(cache.Count==0 && oldView.Image.Source==null && currentView.Image.Source==null,"retired result cannot reinsert cache or paint a removed/re-added view");
                    secondRelease.Set(); PumpUntil(()=>IconsIdle(dock),"fresh result finishes after retired result is discarded");
                    Check(extractions==2 && cache.Count==1 && Object.ReferenceEquals(currentView.Image.Source,replacement),"only the fresh generation populates a removed and re-added path");
                } finally {release.Set();secondRelease.Set();}
            }
            store.Settings.Items.Clear(); dock.ApplySettings(new Rect(0,0,1920,1040));
            FixtureItems(store,"fixture:error",1);
            dock.IconLoader=path=> {throw new IOException("Synthetic icon extraction failure");};
            dock.ApplySettings(new Rect(0,0,1920,1040)); PumpUntil(()=>IconsIdle(dock),"failed extraction leaves no pending request");
            dock.IconLoader=path=>replacement;
            first=Field<List<DockWindow.IconView>>(dock,"views")[0];
            typeof(DockWindow).GetMethod("LoadIcon",Private).Invoke(dock,new object[]{first,true});
            PumpUntil(()=>IconsIdle(dock),"a failed icon can be refreshed successfully");
            Check(Object.ReferenceEquals(first.Image.Source,replacement),"icon pump recovers after extraction failure");

            store.Settings.Items.Clear(); dock.ApplySettings(new Rect(0,0,1920,1040));
            using(var entered=new ManualResetEventSlim()) using(var release=new ManualResetEventSlim()) using(var returned=new ManualResetEventSlim()) {
                dock.IconLoader=path=> {entered.Set();if(!release.Wait(15000)) throw new TimeoutException("Closed fixture gate");returned.Set();return blue;};
                try {
                    FixtureItems(store,"fixture:closed",1); dock.ApplySettings(new Rect(0,0,1920,1040));
                    PumpUntil(()=>entered.IsSet,"close fixture starts a native extraction");
                    first=Field<List<DockWindow.IconView>>(dock,"views")[0];
                    dock.DisposeIconLoads(); release.Set();
                    PumpUntil(()=>returned.IsSet,"already-started close fixture returns without blocking the UI");
                    Check(IconsIdle(dock) && cache.Count==0 && first.Image.Source==null,"closing clears pending/cache/current images and ignores late completion");
                    typeof(DockWindow).GetMethod("LoadIcon",Private).Invoke(dock,new object[]{first,false});
                    Check(IconsIdle(dock),"disposed icon loader rejects later requests");
                } finally {release.Set();}
            }
            Check(new WindowInteropHelper(dock).Handle==IntPtr.Zero && !dock.IsVisible && Field<object>(dock,"host")==null && Field<object>(dock,"tray")==null,"icon lifecycle regression never creates a native dock, tray or visible window");
        }
        static void CheckPersonalDesktopTransferRetries(string directory) {
            string desktop=Path.Combine(directory,"RetryDesktop"),shared=Path.Combine(directory,"RetryPublic");
            Directory.CreateDirectory(desktop); Directory.CreateDirectory(shared);
            var store=new Store(Path.Combine(directory,"RetryData"),desktop,shared);
            store.Settings.Items.Clear(); store.Settings.AutoAddDesktopShortcuts=true; store.Settings.MoveDesktopShortcuts=true;
            var dock=new DockWindow(store);
            DateTime now=new DateTime(2026,10,27,0,0,0,DateTimeKind.Utc);
            dock.InjectDesktopWatcher(new DesktopShortcutWatcher(new[]{desktop,shared},null,()=>now));
            const string bytes="[InternetShortcut]\nURL=https://example.invalid/installer\n";
            string path=Path.Combine(desktop,"Installer.url"); File.WriteAllText(path,bytes);
            DockItem copied;
            using(var reader=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
                copied=store.Settings.Items.Single();
                Check(copied.ManagedShortcut && File.Exists(copied.Path) && File.Exists(path) &&
                    !copied.DesktopSourceMoved && copied.PendingSourceDelete,"installer read lock keeps a durable managed copy and a retryable original");
                Check(Field<TextBlock>(dock,"notice").Text.Contains(L10n.T("ImportRemoveFailed")),"a deferred private transfer has a visible explanation");
                now=now.AddSeconds(45); dock.PollDesktopShortcuts();
                Check(store.LastPersonalTransferResult==Store.PersonalTransferResult.Retry &&
                    Field<Dictionary<string,string>>(dock,"desktopImportNotices").Count==1,"managed-copy retry survives paused polling without repeating an identical notice");
            }
            string id=copied.Id;
            dock.PollDesktopShortcuts();
            Check(!File.Exists(path) && store.Settings.Items.Count==1 && copied.Id==id &&
                copied.DesktopSourceMoved && !copied.PendingSourceDelete,"releasing the installer lock transfers the same shortcut without a second panel entry");
            var disk=new Store(store.DataDirectory,desktop,shared);
            Check(disk.Settings.Items.Single().Id==id && disk.Settings.Items[0].DesktopSourceMoved,"successful retry is persisted before later removal/restoration");
            File.WriteAllText(path,bytes); // no scan of a missing source name
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(!File.Exists(path) && store.Settings.Items.Count==1 && store.Settings.Items[0].Id==id,"an installer recreation before a missing-path scan transfers the existing managed item");

            File.WriteAllText(path,bytes);
            using(var profileLock=new FileStream(Path.Combine(store.DataDirectory,"settings.json"),FileMode.Open,FileAccess.Read,FileShare.Read)) {
                dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
                Check(File.Exists(path) && store.LastPersonalTransferResult==Store.PersonalTransferResult.Retry,
                    "a failed durable transfer journal never deletes the desktop source");
            }
            dock.PollDesktopShortcuts();
            Check(!File.Exists(path),"the source moves after its durable journal can be saved again");

            for(int index=1;index<Store.MaximumItems;index++) store.Settings.Items.Add(new DockItem {Id="capacity-"+index,Name="Fixture",Path="fixture:capacity-"+index});
            File.WriteAllText(path,bytes); dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(store.Settings.Items.Count==Store.MaximumItems && !File.Exists(path),"a full panel can finish an existing item's transfer without admitting another item");
            store.Settings.Items.RemoveAll(item=>item!=copied);
            const string different="[InternetShortcut]\nURL=https://example.invalid/different\n";
            File.WriteAllText(path,different); dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(File.ReadAllText(path)==different && File.ReadAllText(copied.Path)==bytes && store.Settings.Items.Count==1,
                "changed installer bytes preserve both original and managed copy");
            Check(Field<TextBlock>(dock,"notice").Text.Contains(L10n.T("ImportChanged")),"a changed-source conflict produces a visible notice");
            dock.PollDesktopShortcuts();
            Check(Field<Dictionary<string,string>>(dock,"desktopImportNotices").Count==1,"a preserved changed source is acknowledged after its one-time explanation");

            File.Delete(path); dock.PollDesktopShortcuts(); store.Settings.MoveDesktopShortcuts=false;
            File.WriteAllText(path,bytes); dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(File.Exists(path) && store.Settings.Items.Count==1,"disabled transfer preference preserves a recreated personal original");
            store.Settings.MoveDesktopShortcuts=true;
            string cancelled=Path.Combine(desktop,"Removed copy.url"); File.WriteAllText(cancelled,bytes);
            using(var reader=new FileStream(cancelled,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
                var item=store.Settings.Items.Single(value=>value.OriginalPath==cancelled);
                Check(item.PendingSourceDelete && !item.DesktopSourceMoved,"copy-only fixture remains deferred while the installer holds it");
                typeof(DockWindow).GetMethod("RemoveItem",Private).Invoke(dock,new object[]{item});
            }
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(File.Exists(cancelled) && !store.Settings.Items.Any(item=>item.OriginalPath==cancelled),"explicit removal of a deferred copy preserves and suppresses its desktop original");

            string vanished=Path.Combine(desktop,"Installer removed original.url"); File.WriteAllText(vanished,bytes);
            DockItem pending;
            using(var reader=new FileStream(vanished,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
                pending=store.Settings.Items.Single(item=>item.OriginalPath==vanished);
            }
            File.Delete(vanished); // installer cleanup after its copy was imported
            typeof(DockWindow).GetMethod("RemoveItem",Private).Invoke(dock,new object[]{pending});
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(File.ReadAllText(vanished)==bytes && !store.Settings.Items.Any(item=>item.OriginalPath==vanished),
                "removing a deferred item restores its sole copy if the installer removed the source meanwhile");

            string manual=Path.Combine(desktop,"Manual.url"); File.WriteAllText(manual,bytes);
            dock.InjectDesktopWatcher(new DesktopShortcutWatcher(new[]{desktop,shared})); // baseline contains Manual.url
            dock.AddPaths(new[]{manual});
            Check(!File.Exists(manual),"manual import transfers an existing baselined personal shortcut");
            File.WriteAllText(manual,bytes); dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(!File.Exists(manual) && store.Settings.Items.Count(item=>item.OriginalPath==manual)==1,"manual import forgets its deleted source so an immediate installer recreation is handled");

            string ordinary=Path.Combine(desktop,"Untouched.txt"); File.WriteAllText(ordinary,"not a shortcut");
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(File.Exists(ordinary) && !store.Settings.Items.Any(item=>item.OriginalPath==ordinary),"ordinary desktop files remain outside automatic transfer");
            Check(new WindowInteropHelper(dock).Handle==IntPtr.Zero && !dock.IsVisible && Field<object>(dock,"host")==null && Field<object>(dock,"tray")==null,
                "personal transfer retries use only synthetic directories and no native dock or permission prompt");
        }
        static void CheckAutomaticDesktopAdding(string directory) {
            string desktop=Path.Combine(directory,"watch-desktop"), shared=Path.Combine(directory,"watch-shared");
            Directory.CreateDirectory(desktop); Directory.CreateDirectory(shared);
            string existing=Path.Combine(desktop,"Existing.url");
            File.WriteAllText(existing,"[InternetShortcut]\nURL=https://example.com/existing\n");
            var store=new Store(Path.Combine(directory,"WatchData"),desktop,shared);
            store.Settings.Items.Clear(); store.Settings.AutoAddDesktopShortcuts=true; store.Settings.MoveDesktopShortcuts=true;
            var dock=new DockWindow(store);
            dock.InjectDesktopWatcher(new DesktopShortcutWatcher(new[]{desktop,shared}));
            string added=Path.Combine(desktop,"New.url");
            File.WriteAllText(added,"[InternetShortcut]\nURL=\n");
            dock.PollDesktopShortcuts();
            Check(store.Settings.Items.Count==0,"auto-add waits for a stable shortcut before importing");
            dock.PollDesktopShortcuts();
            Check(store.Settings.Items.Count==0 && File.Exists(added),"a stable but unfinished URL remains on the desktop");
            File.WriteAllText(added,"[InternetShortcut]\nURL=https://example.com/new\n");
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(store.Settings.Items.Count==1 && store.Settings.Items[0].ManagedShortcut && !File.Exists(added),"auto-add uses the existing managed shortcut transfer transaction");
            Check(File.Exists(existing) && new Store(store.DataDirectory,desktop,shared).Settings.Items.Count==1,"old shortcuts remain on desktop and new shortcut is persisted");
            var imported=store.Settings.Items[0];
            // Force a restore collision so suppression must use the real new
            // filename, rather than guessing the original source name.
            File.WriteAllText(added,"[InternetShortcut]\nURL=https://example.com/unrelated\n");
            typeof(DockWindow).GetMethod("RemoveItem",Private).Invoke(dock,new object[]{imported});
            Check(store.Settings.Items.Count==0 && File.Exists(store.LastRestoredPath) && store.LastRestoredPath!=added,"manual remove restores into a safe new filename on collision");
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(store.Settings.Items.Count==0 && File.Exists(store.LastRestoredPath),"a deliberately restored shortcut is not automatically imported again");
            string publicFile=Path.Combine(shared,"Shared.url");
            File.WriteAllText(publicFile,"[InternetShortcut]\nURL=https://example.com/shared\n");
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(store.Settings.Items.Count==1 && store.Settings.Items[0].PendingPublicMove && File.Exists(publicFile),"automatic shared shortcut adding preserves its original without requesting elevation");
            store.Settings.MoveDesktopShortcuts=false;
            string linked=Path.Combine(desktop,"Linked.url");
            File.WriteAllText(linked,"[InternetShortcut]\nURL=https://example.com/linked\n");
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(store.Settings.Items.Count==2 && File.Exists(linked) && store.Settings.Items.Any(item=>item.OriginalPath==linked && File.Exists(item.Path) && !item.DesktopSourceMoved),"auto-add respects the preference to preserve desktop originals");
            store.Settings.Items.Clear();
            for(int index=0;index<30;index++) store.Settings.Items.Add(new DockItem { Id="full-"+index, Name="Full "+index, Path="fixture:full-"+index });
            string overflow=Path.Combine(desktop,"Overflow.url");
            File.WriteAllText(overflow,"[InternetShortcut]\nURL=https://example.com/overflow\n");
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(store.Settings.Items.Count==30 && File.Exists(overflow),"a full dock keeps new desktop shortcuts intact");
            store.Settings.AutoAddDesktopShortcuts=false; dock.ApplySettings(new Rect(0,0,1920,1040));
            Check(Field<DesktopShortcutWatcher>(dock,"desktopWatcher")==null && Field<object>(dock,"desktopWatcherTimer")==null,"manual mode disposes observation and has no timer");
            Check(Field<object>(dock,"host")==null && Field<object>(dock,"tray")==null && !dock.IsVisible,"auto-add integration uses only isolated directories and an unshown dock");
        }
        static void CheckAutomaticPublicMoveQueue(string directory) {
            string desktop=Path.Combine(directory,"public-prompt-desktop"), shared=Path.Combine(directory,"public-prompt-shared");
            Directory.CreateDirectory(desktop); Directory.CreateDirectory(shared);
            string data=Path.Combine(directory,"PublicPromptData");
            var store=new Store(data,desktop,shared);
            store.Settings.MoveDesktopShortcuts=true; store.Settings.AutoAddDesktopShortcuts=true;
            string oldPath=Path.Combine(shared,"Previously declined.url");
            File.WriteAllText(oldPath,"[InternetShortcut]\nURL=https://example.invalid/old\n");
            var old=store.Add(oldPath);
            var dock=new DockWindow(store);
            var drawing=new DrawingImage(new GeometryDrawing(Brushes.Blue,null,new EllipseGeometry(new Point(5,5),5,5))); drawing.Freeze();
            dock.IconLoader=path=>drawing;
            bool desktopAvailable=false;
            var batches=new List<string>();
            dock.InjectAutomaticPublicMove(()=>desktopAvailable,ids=> { batches.Add(ids); return System.Threading.Tasks.Task.FromResult(false); });
            dock.InjectDesktopWatcher(new DesktopShortcutWatcher(new[]{desktop,shared}));
            string firstPath=Path.Combine(shared,"New installer.url");
            File.WriteAllText(firstPath,"[InternetShortcut]\nURL=https://example.invalid/new\n");
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            var first=store.Settings.Items.Single(item=>item.OriginalPath==firstPath);
            Check(first.PendingPublicMove && File.Exists(firstPath),"a new shared shortcut remains safe until Windows consent is available");
            Check(Field<HashSet<string>>(dock,"queuedPublicMoves").SetEquals(new[]{first.Id}),"only the newly imported shared shortcut is queued, not an older declined item");
            dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(batches.Count==0,"a foreign application or fullscreen state defers the automatic permission request");
            desktopAvailable=true;
            dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(batches.SequenceEqual(new[]{first.Id}) && File.Exists(firstPath) && File.Exists(oldPath),"cancelling affects only the scoped request and deletes no original");
            dock.QueueAutomaticPublicMove(first); dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(batches.Count==1,"a cancelled ID is not repeatedly prompted by later watcher ticks");

            string secondPath=Path.Combine(shared,"Next installer.url");
            File.WriteAllText(secondPath,"[InternetShortcut]\nURL=https://example.invalid/next\n");
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            var second=store.Settings.Items.Single(item=>item.OriginalPath==secondPath);
            dock.InjectAutomaticPublicMove(()=>true,ids=> {
                batches.Add(ids);
                dock.Store.PreparePublicOperation("--public-move",ids);
                byte[] planned=File.ReadAllBytes(Path.Combine(data,"settings.json"));
                var helper=new Store(data,desktop,shared,true);
                foreach(var item in helper.GetPreparedPublicMoveItems(ids)) Check(helper.CompletePublicMove(item),"the scoped prepared shared source is transferred in the synthetic helper");
                Check(planned.SequenceEqual(File.ReadAllBytes(Path.Combine(data,"settings.json"))),"the shared helper never writes the user's journal");
                dock.Store=new Store(data,desktop,shared);
                return System.Threading.Tasks.Task.FromResult(true);
            });
            dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(batches.Count==2 && batches[1]==second.Id && !File.Exists(secondPath),"the next installation transfers only its requested new shortcut");
            Check(File.Exists(firstPath) && File.Exists(oldPath),"previously cancelled and legacy pending sources remain untouched");
            Check(!Field<HashSet<string>>(dock,"promptedPublicMoves").Contains(second.Id),"completed requests release their session marker for a later genuine recreation");
            File.WriteAllText(secondPath,"[InternetShortcut]\nURL=https://example.invalid/next\n");
            dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(Field<HashSet<string>>(dock,"queuedPublicMoves").Contains(second.Id),"a shared installer recreation is detected even before a missing-file scan");
            dock.Store.Settings.MoveDesktopShortcuts=false;
            dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(Field<HashSet<string>>(dock,"queuedPublicMoves").Count==0 && File.Exists(secondPath),"turning transfer off clears deferred work without touching its original");
            dock.StopAutomaticPublicMoves(); dock.InjectDesktopWatcher(null);
            typeof(DockWindow).GetMethod("DisposeIconLoads",Private).Invoke(dock,new object[0]);
            Check(Field<DispatcherTimer>(dock,"publicMoveTimer")==null && new WindowInteropHelper(dock).Handle==IntPtr.Zero && !dock.IsVisible,
                "queue tests finish without timers, real prompts or a native dock window");
        }

        static void CheckRecreatedPublicShortcutQueue(string directory) {
            string desktop=Path.Combine(directory,"RecreatedDesktop"),shared=Path.Combine(directory,"RecreatedPublic"),data=Path.Combine(directory,"RecreatedPublicData");
            Directory.CreateDirectory(desktop); Directory.CreateDirectory(shared);
            var store=new Store(data,desktop,shared);
            store.Settings.Items.Clear(); store.Settings.AutoAddDesktopShortcuts=true; store.Settings.MoveDesktopShortcuts=true;
            const string bytes="[InternetShortcut]\nURL=https://example.invalid/shared-installer\n";
            string oldPath=Path.Combine(shared,"Previously declined.url"); File.WriteAllText(oldPath,bytes);
            DockItem old=store.Add(oldPath);
            var dock=new DockWindow(store);
            dock.InjectDesktopWatcher(new DesktopShortcutWatcher(new[]{desktop,shared}));
            int permissions=0;
            string selectedPath=Path.Combine(shared,"New installer.url");
            dock.InjectAutomaticPublicMove(()=>true,ids=> {
                permissions++;
                Check(ids!=old.Id && !ids.Split(',').Contains(old.Id),"recreated public transfer excludes an older pending item");
                dock.Store.PreparePublicOperation("--public-move",ids);
                byte[] planned=File.ReadAllBytes(Path.Combine(data,"settings.json"));
                var helper=new Store(data,desktop,shared,true);
                foreach(var item in helper.GetPreparedPublicMoveItems(ids)) Check(helper.CompletePublicMove(item),"a scoped recreated public source can be transferred by the synthetic read-only helper");
                Check(planned.SequenceEqual(File.ReadAllBytes(Path.Combine(data,"settings.json"))),"recreated public helper leaves its profile journal unchanged");
                dock.Store=new Store(data,desktop,shared);
                return System.Threading.Tasks.Task.FromResult(true);
            });
            File.WriteAllText(selectedPath,bytes); dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            string id=dock.Store.Settings.Items.Single(item=>item.OriginalPath==selectedPath).Id;
            Check(File.Exists(selectedPath) && Field<TextBlock>(dock,"notice").Text.Contains(L10n.T("SharedCopyNotice")),"a new shared copy visibly explains its pending permission action");
            dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(permissions==1 && !File.Exists(selectedPath) && File.Exists(oldPath),"first shared request moves only its selected original");
            File.WriteAllText(selectedPath,bytes); dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            Check(File.Exists(selectedPath) && dock.Store.Settings.Items.Count==2 &&
                dock.Store.Settings.Items.Single(item=>item.Id==id).PendingPublicMove,"identical public recreation reuses its item and waits for a fresh scoped permission request");
            dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(permissions==2 && !File.Exists(selectedPath),"a successful prior transfer does not suppress a later genuine recreation");
            for(int index=2;index<Store.MaximumItems;index++) dock.Store.Settings.Items.Add(new DockItem {Id="public-capacity-"+index,Name="Fixture",Path="fixture:public-capacity-"+index});
            File.WriteAllText(selectedPath,bytes); dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(permissions==3 && !File.Exists(selectedPath) && dock.Store.Settings.Items.Count==Store.MaximumItems,"a full panel can scope a recreated public source without adding another item");
            const string changed="[InternetShortcut]\nURL=https://example.invalid/changed-shared\n";
            File.WriteAllText(selectedPath,changed); dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(permissions==3 && File.ReadAllText(selectedPath)==changed &&
                !dock.Store.Settings.Items.Single(item=>item.Id==id).PendingPublicMove,"different recreated shared bytes are preserved without a permission prompt");
            Check(Field<TextBlock>(dock,"notice").Text.Contains(L10n.T("ImportChanged")),"different shared bytes have a visible conflict explanation");
            File.Delete(selectedPath); dock.PollDesktopShortcuts();
            dock.Store.Settings.MoveDesktopShortcuts=false;
            File.WriteAllText(selectedPath,bytes); dock.PollDesktopShortcuts(); dock.PollDesktopShortcuts();
            dock.RunAutomaticPublicMoves().GetAwaiter().GetResult();
            Check(permissions==3 && File.Exists(selectedPath),"disabled transfer preference preserves shared recreation without requesting permission");
            Check(new WindowInteropHelper(dock).Handle==IntPtr.Zero && !dock.IsVisible && Field<object>(dock,"host")==null && Field<object>(dock,"tray")==null,
                "shared recreation tests use no actual permission prompt, shell desktop or native dock");
            dock.StopAutomaticPublicMoves(); dock.DisposeIconLoads();
        }
        static void CheckThemeRendering(string directory) {
            string desktop=Path.Combine(directory,"theme-desktop"); Directory.CreateDirectory(desktop);
            var store=new Store(Path.Combine(directory,"ThemeData"),desktop,desktop);
            store.Settings.IconSize=80; store.Settings.Zoom=1.65; store.Settings.Items.Clear();
            var dock=new DockWindow(store);
            var drawing=new DrawingImage(new GeometryDrawing(Brushes.OrangeRed,null,new EllipseGeometry(new Point(40,40),40,40))); drawing.Freeze();
            var cache=Field<Dictionary<string,ImageSource>>(dock,"iconCache");
            for(int index=0;index<30;index++) { string path="fixture:theme-"+index; cache[path]=drawing; store.Settings.Items.Add(new DockItem { Id="theme-"+index,Name="Long original shortcut "+index,Path=path }); }
            var dusk=ThemeMotion.For("dusk");
            Check(dusk.Response==25 && dusk.HoverLift==6 && dusk.ClickPop==.19 && dusk.ClickHop==12 && dusk.ClickDuration==.43 && dusk.DragResponse==55 && dusk.DragScale==1.10,"Dusk preserves the established pointer, click and drag responses exactly");
            Check(ThemeCatalog.Ids.Select(id=>ThemeMotion.For(id).Response).Distinct().Count()==5,"all five themes have a distinct motion response");
            foreach(string id in ThemeCatalog.Ids) {
                dock.SetTheme(id);
                var palette=ThemePalette.For(id); var motion=ThemeMotion.For(id);
                Check(Object.ReferenceEquals(palette,ThemePalette.For(id)) && Object.ReferenceEquals(motion,ThemeMotion.For(id)),"theme palettes and motion are cached outside pointer/frame loops");
                foreach(string placement in new[]{"bottom","top","left","right"}) foreach(bool labels in new[]{false,true}) {
                    store.Settings.Placement=placement; store.Settings.ShowLabels=labels;
                    dock.ApplySettings(new Rect(0,0,1920,1040));
                    var content=(FrameworkElement)dock.Content; dock.Content=null;
                    System.Windows.Documents.TextElement.SetForeground(content,dock.Foreground);
                    try {
                        Arrange(content,dock);
                        var views=Field<List<DockWindow.IconView>>(dock,"views");
                        var glass=Field<Border>(dock,"glass");
                        string scenario=id+" "+placement+" labels="+labels;
                        Check(views.Count==30,scenario+": every icon remains present");
                        if(id=="dusk") {
                            var gradient=(LinearGradientBrush)glass.Background;
                            Check(gradient.GradientStops[0].Color==Color.FromArgb((byte)(store.Settings.Opacity*255),43,47,60) && gradient.GradientStops[1].Color==Color.FromArgb((byte)(store.Settings.Opacity*255),21,24,34),"Dusk uses the exact existing two-stop dock background");
                        } else {
                            var background=((SolidColorBrush)glass.Background).Color;
                            Check(background.R==palette.Surface.R && background.G==palette.Surface.G && background.B==palette.Surface.B && background.A==(byte)(store.Settings.Opacity*255),scenario+": selected surface and opacity are applied to the real dock");
                        }
                        foreach(var view in views) {
                            Check(Object.ReferenceEquals(view.Image.Source,drawing),scenario+": theme retains the original full-color icon source");
                            Near(view.Button.Width,view.Image.Width+10,scenario+": compact tile width is unchanged");
                            foreach(var label in Descendants<TextBlock>(view.Button)) Check(label.TextWrapping==TextWrapping.NoWrap && label.TextTrimming==TextTrimming.CharacterEllipsis,scenario+": optional labels keep the compact single-line rendering");
                            view.HoverScale=1.85; view.SizeMotion.Reset(1.85); view.HoverLift=-(motion.HoverLift+motion.ClickHop); view.LiftMotion.Reset(view.HoverLift);
                        }
                        dock.AdvanceAnimations(.001);
                        foreach(var view in views) WithinWindow(view.Image,content,dock,scenario+": themed hover plus click remains inside the existing window geometry");
                        if(id=="aurora") {
                            Check(views.All(view=>view.HoverGlow!=null && view.HoverGlow.Opacity>0),"Aurora adds its restrained accent only during icon activity");
                            Check(((SolidColorBrush)glass.BorderBrush).Color.A>95,"Aurora softly brightens the panel edge during the existing animation");
                            typeof(DockWindow).GetField("externalDragFeedback",Private).SetValue(dock,true);
                            var dragFeedback=Brushes.Orange; glass.BorderBrush=dragFeedback; dock.AdvanceAnimations(.001);
                            Check(Object.ReferenceEquals(dragFeedback,glass.BorderBrush),"Aurora animation preserves external file-drag feedback");
                            typeof(DockWindow).GetField("externalDragFeedback",Private).SetValue(dock,false);
                        }
                        typeof(DockWindow).GetMethod("ResetMagnification",Private).Invoke(dock,new object[0]);
                        for(int frame=0;frame<240;frame++) dock.AdvanceAnimations(1.0/120);
                        Check(!Field<bool>(dock,"rendering"),scenario+": animation stops after settling instead of running an idle loop");
                        Check(views.All(view=>Math.Abs(view.Scale.ScaleX-1)<.001 && (view.HoverGlow==null || view.HoverGlow.Opacity==0)),scenario+": all icons return to their exact idle appearance");
                        if(id=="aurora") Check(((SolidColorBrush)glass.BorderBrush).Color.A==95,"Aurora panel edge returns to its resting opacity without an idle timer");
                        if(!labels) {
                            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dock.Width),(int)Math.Ceiling(dock.Height),96,96,PixelFormats.Pbgra32); bitmap.Render(content);
                            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using(var stream=File.Create(Path.Combine(directory,"theme-"+id+"-"+placement+".png"))) encoder.Save(stream);
                        }
                    } finally { dock.Content=content; }
                }
            }
            Check(Field<object>(dock,"host")==null && Field<object>(dock,"tray")==null && !dock.IsVisible,"theme rendering never starts a real desktop host or visible window");
        }
        static void CheckThemeTransitions(string directory) {
            string desktop=Path.Combine(directory,"BlendDesktop"); Directory.CreateDirectory(desktop);
            var store=new Store(Path.Combine(directory,"BlendData"),desktop,desktop);
            store.Settings.Opacity=.43; store.Settings.CornerRadius=7;
            store.Settings.Items.Add(new DockItem { Id="blend-icon",Name="Original full-color shortcut",Path="fixture:blend" }); store.Save();
            var dock=new DockWindow(store);
            var source=new DrawingImage(new GeometryDrawing(Brushes.OrangeRed,null,new RectangleGeometry(new Rect(0,0,48,48)))); source.Freeze();
            Field<Dictionary<string,ImageSource>>(dock,"iconCache")["fixture:blend"]=source;
            dock.ApplySettings(new Rect(0,0,1920,1040));
            Check(!dock.ThemeTransitionsEnabled,"unshown endpoint tests default to immediate theme application");
            var settings=new SettingsWindow(dock); typeof(DockWindow).GetField("settingsWindow",Private).SetValue(dock,settings);
            var settingsTree=settings.Content;
            var originalView=Field<List<DockWindow.IconView>>(dock,"views")[0];
            var originalButton=originalView.Button;
            dock.ThemeTransitionsEnabled=true;
            dock.SetTheme("frost");
            var glass=Field<Border>(dock,"glass");
            Check(dock.ThemeTransitionActive && dock.DisplayedThemeColor("Foreground")==Colors.White,"successful selection starts at the currently displayed colors without a flash");
            Check(Object.ReferenceEquals(settingsTree,settings.Content) && Object.ReferenceEquals(originalButton,Field<List<DockWindow.IconView>>(dock,"views")[0].Button),"theme switching retains the settings and icon trees instead of rebuilding them");
            Check(((LinearGradientBrush)glass.Background).GradientStops[0].Color.A==(byte)(.43*255) && glass.CornerRadius.TopLeft==7,"transition begins at the visible custom background opacity and radius");
            dock.AdvanceAnimations(.10);
            Check(dock.DisplayedThemeColor("Foreground")==Colors.White && dock.DisplayedThemeColor("Surface")!=ThemePalette.For("dusk").Surface,"a dark-to-light transition keeps text readable before its short contrast-crossing interval");
            dock.AdvanceAnimations(.05);
            Check(dock.ThemeTransitionActive && dock.DisplayedThemeColor("Foreground")==Color.FromRgb(144,153,160),"300ms ease-in-out transition reaches the exact halfway foreground");
            Check(((SolidColorBrush)settings.Foreground).Color==dock.DisplayedThemeColor("Foreground"),"settings text shares the currently displayed foreground");
            Check(((LinearGradientBrush)glass.Background).GradientStops[0].Color.A==151 && Math.Abs(glass.CornerRadius.TopLeft-14.5)<.00001,"background alpha and corner radius interpolate at the same midpoint");
            var midMenu=(ContextMenu)typeof(DockWindow).GetMethod("BuildDockMenu",Private).Invoke(dock,new object[0]);
            Check(((SolidColorBrush)midMenu.Background).Color==dock.DisplayedThemeColor("Menu"),"menus created during a transition start with the current displayed palette");
            Check(((SolidColorBrush)((ToolTip)originalButton.ToolTip).Foreground).Color==dock.DisplayedThemeColor("Foreground"),"an existing icon tooltip follows the same interpolated palette");
            Color beforeRetarget=dock.DisplayedThemeColor("Foreground"), beforeTop=((LinearGradientBrush)glass.Background).GradientStops[0].Color;
            double beforeRadius=glass.CornerRadius.TopLeft;
            dock.SetTheme("ember");
            Check(dock.DisplayedThemeColor("Foreground")==beforeRetarget && ((LinearGradientBrush)glass.Background).GradientStops[0].Color==beforeTop && glass.CornerRadius.TopLeft==beforeRadius,"rapid re-selection retargets from the exact displayed frame without a jump");
            var frostMemory=store.Settings.ThemeAppearances.Single(value=>value.Theme=="frost");
            Check(frostMemory.Opacity==.76 && frostMemory.CornerRadius==22,"intermediate visual values never overwrite per-theme background preferences");
            dock.AdvanceAnimations(.15);
            Check(dock.DisplayedThemeColor("Foreground")==Color.FromRgb(192,189,184),"retargeted midpoint starts from the previous displayed color");
            dock.AdvanceAnimations(.15);
            Check(!dock.ThemeTransitionActive && !Field<bool>(dock,"rendering") && dock.DisplayedThemeColor("Foreground")==ThemePalette.For("ember").Foreground,"transition reaches the exact endpoint and stops its rendering subscription");
            Check(glass.Background is SolidColorBrush && ((SolidColorBrush)glass.Background).Color.A==(byte)(.85*255) && glass.CornerRadius.TopLeft==16,"finished transition restores the exact catalog background type and values");
            Check(((SolidColorBrush)midMenu.Background).Color==ThemePalette.For("ember").Menu,"a menu created midway also reaches the final palette");
            Check(Object.ReferenceEquals(source,originalView.Image.Source) && originalButton.Opacity==1 && dock.Opacity==1,"theme animation never fades the window, icon or an old screenshot");
            dock.SetTheme("aurora"); dock.AdvanceAnimations(.10); dock.SetTheme("aurora"); dock.AdvanceAnimations(.20);
            Check(!dock.ThemeTransitionActive && dock.DisplayedThemeColor("Foreground")==ThemePalette.For("aurora").Foreground,"choosing the already selected theme does not restart its transition");
            dock.SetTheme("frost"); dock.AdvanceAnimations(.10);
            string profile=Path.Combine(store.DataDirectory,"settings.json"); byte[] bytes=File.ReadAllBytes(profile);
            Color beforeFailure=dock.DisplayedThemeColor("Foreground"), backgroundBeforeFailure=((LinearGradientBrush)glass.Background).GradientStops[0].Color;
            double radiusBeforeFailure=glass.CornerRadius.TopLeft;
            using(var locked=new FileStream(profile,FileMode.Open,FileAccess.Read,FileShare.Read)) dock.SetTheme("graphite");
            Check(store.Settings.Theme=="frost" && dock.ThemeTransitionActive && dock.DisplayedThemeColor("Foreground")==beforeFailure && ((LinearGradientBrush)glass.Background).GradientStops[0].Color==backgroundBeforeFailure && glass.CornerRadius.TopLeft==radiusBeforeFailure,"failed persistence leaves the active animation and displayed frame unchanged");
            Check(bytes.SequenceEqual(File.ReadAllBytes(profile)),"failed animated selection preserves the exact profile bytes");
            dock.AdvanceAnimations(.20);
            Check(!dock.ThemeTransitionActive && dock.DisplayedThemeColor("Foreground")==ThemePalette.For("frost").Foreground,"an interrupted failed request does not restart or replace the prior transition");
            dock.SetTheme("ember"); dock.AdvanceAnimations(.10);
            Descendants<Slider>((DependencyObject)settings.Content).Single(slider=>Object.Equals(slider.Tag,"setting-Opacity")).Value=29;
            Descendants<Slider>((DependencyObject)settings.Content).Single(slider=>Object.Equals(slider.Tag,"setting-CornerRadius")).Value=3;
            dock.AdvanceAnimations(.5);
            Check(!dock.ThemeTransitionActive && ((SolidColorBrush)glass.Background).Color.A==(byte)(.29*255) && glass.CornerRadius.TopLeft==3,"direct background edits finish cleanly to current settings instead of reviving an obsolete endpoint");
            var reloaded=new Store(store.DataDirectory,desktop,desktop);
            Check(reloaded.Settings.Theme=="ember" && reloaded.Settings.Opacity==.29 && reloaded.Settings.CornerRadius==3,"background changes during a transition persist their actual requested values");
            Check(dock.DisplayedThemeColor("SliderFill")==dock.Palette.Accent && dock.DisplayedThemeColor("CheckFill")==dock.Palette.Accent,"modern slim-slider and rounded-checkbox endpoints are preserved for every theme");
            dock.SetTheme("dusk"); dock.AdvanceAnimations(.30);
            Check(dock.DisplayedThemeColor("SliderFill")==ThemePalette.For("dusk").Accent && dock.DisplayedThemeColor("CheckFill")==ThemePalette.For("dusk").Accent,"Dusk retains its modern 1.8.1 blue controls after animation");
            dock.SetTheme("graphite"); dock.AdvanceAnimations(.10);
            Check(dock.DisplayedThemeColor("Foreground")!=Colors.White && dock.DisplayedThemeColor("Foreground")!=ThemePalette.For("graphite").Foreground,"dark-to-dark themes retain the normal gradual foreground progress");
            dock.CancelThemeTransition();
            Check(!dock.ThemeTransitionActive && !Field<bool>(dock,"rendering"),"closing cancellation releases transition work and the rendering callback");
            Check(ThemePalette.For("frost").Foreground==Color.FromRgb(32,51,64),"animation never mutates the cached endpoint palette");
            Check(!dock.IsVisible && !settings.IsVisible && Field<object>(dock,"host")==null && Field<object>(dock,"tray")==null,"animation tests create no visible window, desktop host or installed state");
        }
        [STAThread] public static int Main(string[] args) {
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            try {
                if(args.Length!=1) throw new ArgumentException("Provide a new isolated fixture directory.");
                string directory=Path.GetFullPath(args[0]);
                if(Directory.Exists(directory)) throw new IOException("Fixture directory must not exist.");
                string desktop=Path.Combine(directory,"Desktop"); Directory.CreateDirectory(desktop);
                Program.TestMode=true;
                var store=new Store(Path.Combine(directory,"Data"),desktop,desktop);
                store.Settings.IconSize=80; store.Settings.Zoom=1.65;
                var dock=new DockWindow(store);
                var cache=Field<Dictionary<string,ImageSource>>(dock,"iconCache");
                var drawing=new DrawingImage(new GeometryDrawing(Brushes.CornflowerBlue,null,new RectangleGeometry(new Rect(0,0,80,80),12,12)));
                drawing.Freeze();
                foreach(int count in new[]{0,1,30}) {
                    store.Settings.Items.Clear();
                    for(int index=0;index<count;index++) {
                        string path="fixture:"+index; cache[path]=drawing;
                        store.Settings.Items.Add(new DockItem { Id=Guid.NewGuid().ToString("N"), Name="Shortcut "+index, Path=path });
                    }
                    foreach(bool labels in new[]{false,true}) foreach(string placement in new[]{"bottom","top","left","right"})
                    foreach(Rect area in new[]{new Rect(0,0,1920,1040),new Rect(-1280,40,640,400)}) {
                        store.Settings.ShowLabels=labels; store.Settings.Placement=placement;
                        dock.ApplySettings(area);
                        var content=(FrameworkElement)dock.Content;
                        System.Windows.Documents.TextElement.SetFontFamily(content,dock.FontFamily);
                        System.Windows.Documents.TextElement.SetForeground(content,dock.Foreground);
                        dock.Content=null;
                        try {
                            Arrange(content,dock);
                            string name=placement+", "+count+", labels="+labels+", "+area.Width+"x"+area.Height;
                            Check(dock.Left>=area.Left-1 && dock.Top>=area.Top-1 && dock.Left+dock.Width<=area.Right+1 && dock.Top+dock.Height<=area.Bottom+1,name+": anchor stays inside selected monitor");
                            if(placement=="left" || placement=="right") Near(dock.Top+dock.Height/2,area.Top+area.Height/2,name+": side dock vertically centered");
                            else Near(dock.Left+dock.Width/2,area.Left+area.Width/2,name+": horizontal dock centered");
                            var glass=Field<Border>(dock,"glass");
                            var strip=Field<StackPanel>(dock,"strip");
                            foreach(FrameworkElement child in strip.Children) {
                                Rect bounds=Bounds(child,glass);
                                Check(bounds.Left>=-1 && bounds.Top>=-1 && bounds.Right<=glass.ActualWidth+1 && bounds.Bottom<=glass.ActualHeight+1,name+": "+child.GetType().Name+" fits glass: "+bounds+" within "+glass.ActualWidth+"x"+glass.ActualHeight);
                            }
                            var plus=strip.Children.OfType<Button>().Last();
                            var glyph=(FrameworkElement)plus.Content;
                            Rect glyphBounds=Bounds(glyph,plus);
                            Near(glyphBounds.Left+glyphBounds.Width/2,plus.ActualWidth/2,name+": plus centered horizontally");
                            Near(glyphBounds.Top+glyphBounds.Height/2,plus.ActualHeight/2,name+": plus centered vertically");
                            var views=Field<List<DockWindow.IconView>>(dock,"views");
                            Check(views.Count==count,name+": every shortcut remains reachable");
                            bool vertical=placement=="left" || placement=="right";
                            var crossLine=views.Where(view=>vertical?Grid.GetRow(view.Button)==0:Grid.GetColumn(view.Button)==0)
                                .OrderBy(view=>vertical?Grid.GetColumn(view.Button):Grid.GetRow(view.Button)).ToArray();
                            for(int index=1;index<crossLine.Length;index++) {
                                Rect previous=Bounds(crossLine[index-1].Image,content), current=Bounds(crossLine[index].Image,content);
                                Near(vertical?current.Left-previous.Left:current.Top-previous.Top,
                                    Field<double>(dock,vertical?"slot":"rowPitch"),name+": cross lines use consistent spacing");
                            }
                            foreach(var view in views) {
                                WithinWindow(view.Image,content,dock,name+": idle icon fits window");
                                view.HoverScale=1.84; view.HoverLift=-18;
                                view.SizeMotion.Reset(1.84); view.LiftMotion.Reset(-18);
                            }
                            dock.AdvanceAnimations(.001);
                            foreach(var view in views) {
                                WithinWindow(view.Image,content,dock,name+": hover and click fit window");
                                foreach(var label in Descendants<TextBlock>(view.Button)) WithinWindow(label,content,dock,name+": hovered label fits window");
                                Check(placement=="left"?view.Translate.X>0:placement=="right"?view.Translate.X<0:
                                    placement=="top"?view.Translate.Y>0:view.Translate.Y<0,name+": hover and click move into desktop");
                                view.HoverScale=1; view.HoverLift=0; view.SizeMotion.Reset(1); view.LiftMotion.Reset(0);
                            }
                            dock.AdvanceAnimations(.001);
                            if(count==30 && area.Width>1000) {
                                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(dock.Width),(int)Math.Ceiling(dock.Height),96,96,PixelFormats.Pbgra32);
                                bitmap.Render(content); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                                using(var stream=File.Create(Path.Combine(directory,"dock-"+placement+(labels?"-labels":"")+".png"))) encoder.Save(stream);
                            }
                        } finally { dock.Content=content; }
                    }
                }
                store.Settings.CornerRadius=0; dock.ApplySettings(new Rect(0,0,1920,1040));
                Check(Field<Border>(dock,"glass").CornerRadius.TopLeft==0,"square background reaches zero radius");
                store.Settings.CornerRadius=23; dock.ApplySettings(new Rect(0,0,1920,1040));
                Check(Field<Border>(dock,"glass").CornerRadius.TopLeft==23,"standard rounding uses the chosen radius");
                store.Settings.CornerRadius=100; dock.ApplySettings(new Rect(0,0,1920,1040));
                Check(Field<Border>(dock,"glass").CornerRadius.TopLeft>=Math.Min(dock.Width,dock.Height)/2-.01,"full rounding reaches capsule geometry");
                dock.SetPlacement("left");
                Check(new Store(store.DataDirectory,desktop,desktop).Settings.Placement=="left","side placement persists to isolated profile");
                dock.SetPlacement("free");
                Check(!Field<bool>(dock,"verticalDock") && store.Settings.Placement=="free","free placement returns the dock to horizontal orientation");
                Check(Field<object>(dock,"host")==null && Field<object>(dock,"tray")==null && !dock.IsVisible,"visual tests never create a desktop host, tray or visible window");
                CheckAutomaticDesktopAdding(directory);
                CheckPersonalDesktopTransferRetries(directory);
                CheckAutomaticPublicMoveQueue(directory);
                CheckRecreatedPublicShortcutQueue(directory);
                CheckThemeRendering(directory);
                CheckThemeTransitions(directory);
                CheckIconCacheLifetime(directory);
                Console.WriteLine("PASS: "+assertions+" dock visual assertions; own-control renders: "+directory);
                return 0;
            } catch(Exception ex) { Console.WriteLine(ex); return 1; }
            finally { app.Shutdown(); }
        }
    }
}
