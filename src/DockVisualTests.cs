using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
                Console.WriteLine("PASS: "+assertions+" dock visual assertions; own-control renders: "+directory);
                return 0;
            } catch(Exception ex) { Console.WriteLine(ex); return 1; }
            finally { app.Shutdown(); }
        }
    }
}
