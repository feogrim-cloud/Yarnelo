using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Polka {
    internal static class UiTests {
        static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static int assertions;
        static readonly List<string> failures = new List<string>();
        static string root;
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr handle,int command);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
        static T Field<T>(object instance,string name) { return (T)instance.GetType().GetField(name, PrivateInstance).GetValue(instance); }
        static object ViewField(object view,string name) { return view.GetType().GetField(name).GetValue(view); }
        static void Check(bool condition,string message) {
            assertions++;
            Console.WriteLine((condition ? "PASS " : "FAIL ")+message);
            if(!condition) failures.Add(message);
        }
        static void Near(double actual,double expected,double tolerance,string message) {
            Check(Math.Abs(actual-expected)<=tolerance,message+" [actual="+actual.ToString("0.###")+", expected="+expected.ToString("0.###")+"]");
        }
        static void Pump(int milliseconds) {
            var frame=new DispatcherFrame();
            var timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue=false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        static void PrepareItems(Store store,int count) {
            string[] paths={"shell:MyComputerFolder","shell:RecycleBinFolder",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"notepad.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"mspaint.exe")};
            string[] names={"Этот компьютер","Корзина","Проводник","Блокнот","Терминал","Рисование"};
            store.Settings.Items.Clear();
            for(int i=0;i<count;i++) store.Settings.Items.Add(new DockItem { Id=Guid.NewGuid().ToString("N"), Name=names[i%names.Length]+(i<names.Length?"":" "+(i+1)), Path=paths[i%paths.Length], OriginalPath=paths[i%paths.Length] });
        }
        static void Render(Window window,string name) {
            window.UpdateLayout();
            int width=(int)Math.Ceiling(window.ActualWidth),height=(int)Math.Ceiling(window.ActualHeight);
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path=Path.Combine(root,name);
            using(var output=File.Create(path)) encoder.Save(output);
            Check(new FileInfo(path).Length>1000,"Own-window render: "+name+" "+width+"x"+height);
        }
        static DockWindow.IconView View(DockWindow dock,int index) { return (DockWindow.IconView)Field<IList>(dock,"views")[index]; }
        static void Call(DockWindow dock,string method,params object[] arguments) { typeof(DockWindow).GetMethod(method,PrivateInstance).Invoke(dock,arguments); }
        static void Steps(DockWindow dock,double seconds) {
            Call(dock,"StopRendering");
            while(seconds>0) { double dt=Math.Min(1.0/120,seconds); dock.AdvanceAnimations(dt); seconds-=dt; }
        }
        static void ShowOwnTestWindow(DockWindow dock) {
            var host=Field<DesktopHost>(dock,"host");
            if(host!=null) host.Dispose();
            ShowWindow(new WindowInteropHelper(dock).Handle,4);
            dock.Activate();
            Pump(50);
            dock.UpdateLayout();
        }
        static Point IconCenter(DockWindow dock,DockWindow.IconView view) {
            double size=Field<double>(dock,"effectiveSize");
            return view.Image.TranslatePoint(new Point(size/2,size/2),Field<Grid>(dock,"root"));
        }
        static Point ButtonPosition(DockWindow dock,DockWindow.IconView view) {
            return view.Button.TranslatePoint(new Point(0,0),Field<Grid>(dock,"root"));
        }
        static string Order(Store store) { return String.Join("|",store.Settings.Items.Select(x=>x.Id)); }
        static void StartDrag(DockWindow dock,DockWindow.IconView view) {
            Point pointer=IconCenter(dock,view);
            typeof(DockWindow).GetField("pressPoint",PrivateInstance).SetValue(dock,pointer);
            dock.BeginInternalDrag(view,pointer);
        }
        static void CheckLanding(DockWindow dock,DockWindow.IconView source,string name) {
            Steps(dock,1.8);
            Check(!Field<bool>(dock,"internalDrag") && !Field<bool>(dock,"dragLanding"),name+": drag and landing finish");
            Check(Field<Image>(dock,"dragGhost")==null && Field<Border>(dock,"dragPlaceholder")==null,name+": ghost and placeholder removed");
            Near(source.Button.Opacity,1,.001,name+": source opacity restored");
            Check(Field<Canvas>(dock,"dragLayer").Children.Count==0,name+": temporary visual layer is empty");
        }
        static IEnumerable<T> Descendants<T>(DependencyObject node) where T:DependencyObject {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) {
                var child=VisualTreeHelper.GetChild(node,i);
                if(child is T) yield return (T)child;
                foreach(T descendant in Descendants<T>(child)) yield return descendant;
            }
        }
        static void Layout(DockWindow dock,int expected,string name) {
            dock.UpdateLayout();
            var strip=Field<StackPanel>(dock,"strip");
            var glass=Field<Border>(dock,"glass");
            var views=Field<IList>(dock,"views");
            Check(views.Count==expected,name+": correct icon count");
            Rect area=Native.WorkingArea(dock);
            Check(dock.Width<=area.Width+1,name+": panel fits screen width "+area.Width);
            Check(dock.Left>=area.Left-1 && dock.Left+dock.Width<=area.Right+1,name+": panel inside screen horizontally");
            Check(dock.Top>=area.Top-1 && dock.Top+dock.Height<=area.Bottom+1,name+": panel inside screen vertically");
            double right=0;
            foreach(FrameworkElement child in strip.Children) {
                var bounds=child.TransformToAncestor(glass).TransformBounds(new Rect(0,0,child.ActualWidth,child.ActualHeight));
                right=Math.Max(right,bounds.Right);
                Check(bounds.Left>=-1 && bounds.Right<=glass.ActualWidth+1,name+": strip child fits glass width ("+child.GetType().Name+")");
            }
            Check(right<=glass.ActualWidth+1,name+": all controls fit glass");
            foreach(object view in views) {
                var image=(Image)ViewField(view,"Image");
                var bounds=image.TransformToAncestor(dock).TransformBounds(new Rect(0,0,image.ActualWidth,image.ActualHeight));
                Check(bounds.Left>=-.5 && bounds.Right<=dock.ActualWidth+.5 && bounds.Top>=-.5 && bounds.Bottom<=dock.ActualHeight+.5,name+": icon is not clipped by window");
            }
        }
        static void Animations(DockWindow dock) {
            var view=View(dock,0);
            Call(dock,"Animate",view,1.55,-5.0,150);
            Steps(dock,.065);
            Check(view.Scale.ScaleX>1.01 && view.Scale.ScaleX<1.55,"Hover scale animates between values");
            Check(view.Translate.Y<-.1 && view.Translate.Y>-5,"Hover lift animates between values");
            double value=view.SizeMotion.Value, velocity=view.SizeMotion.Velocity;
            Call(dock,"Animate",view,1.27,-2.0,150);
            Near(view.SizeMotion.Value,value,0,"Rehover preserves current rendered scale");
            Near(view.SizeMotion.Velocity,velocity,0,"Rehover preserves current scale velocity");
            dock.AdvanceAnimations(1e-8);
            Near(view.SizeMotion.Value,value+velocity*1e-8,1e-10,"Rehover position stays continuous on next frame");
            Near(view.SizeMotion.Velocity,velocity,.0001,"Rehover velocity stays continuous on next frame");
            Steps(dock,.8);
            Near(view.Scale.ScaleX,1.27,.001,"Hover reaches its latest enlarged scale");
            Near(view.Scale.ScaleY,1.27,.001,"Both image axes scale equally");
            Near(view.Translate.Y,-2,.001,"Hover reaches its latest lifted position");
            Call(dock,"ResetMagnification");
            view.Pressing=true; Steps(dock,.6);
            Near(view.Scale.ScaleX,.93,.001,"Press smoothly compresses icon");
            Near(view.Translate.Y,1,.001,"Press smoothly lowers icon");
            Call(dock,"Bounce",view);
            Pump(140); Steps(dock,.06);
            Check(view.Translate.Y<-4,"Click bounce moves icon up");
            Check(view.Scale.ScaleX>1.0 && view.Scale.ScaleY>1.0,"Click animation enlarges icon");
            Pump(400); Steps(dock,.8);
            Near(view.Translate.Y,0,.001,"Click bounce returns to baseline");
            Near(view.Scale.ScaleX,1,.001,"Click resets press compression");
        }
        static void DragAnimations(DockWindow dock,Store store,string desktop) {
            ShowOwnTestWindow(dock);
            Call(dock,"ResetMagnification"); Steps(dock,1);
            store.Save(); string original=Order(store);
            var source=View(dock,0); var neighbor=View(dock,1); var destination=View(dock,5);
            int clicks=0; source.Button.Click+=delegate { clicks++; };
            Point target=IconCenter(dock,destination);
            StartDrag(dock,source);
            Check(Field<bool>(dock,"internalDrag"),"Internal drag captures the pointer in own test window");
            if(!Field<bool>(dock,"internalDrag")) throw new Exception("Own test window could not capture mouse for drag tests");
            Check(Field<Image>(dock,"dragGhost")!=null && Field<Border>(dock,"dragPlaceholder")!=null,"Drag creates a ghost and target placeholder");
            Near(source.Button.Opacity,0,0,"Dragged source is hidden while ghost moves");
            dock.UpdateInternalDrag(target); Steps(dock,.08);
            double slot=Field<double>(dock,"slot");
            Check(neighbor.Shift.X<-1 && neighbor.Shift.X>-slot+1,"Neighbors slide through an intermediate position");
            var ghostX=Field<MotionValue>(dock,"ghostX");
            Check(ghostX.Value>IconCenter(dock,source).X && ghostX.Value<ghostX.Target,"Dragged ghost follows pointer through intermediate positions");
            Check(Order(store)==original,"Preview drag does not mutate in-memory shortcut order");
            Check(Order(new Store(store.DataDirectory,desktop,desktop))==original,"Preview drag does not mutate saved shortcut order");
            Render(dock,"ui-drag-middle.png");
            var positions=new Dictionary<DockWindow.IconView,double>();
            var velocities=new Dictionary<DockWindow.IconView,double>();
            foreach(DockWindow.IconView view in Field<IList>(dock,"views")) {
                positions[view]=view.Button.TranslatePoint(new Point(0,0),Field<Grid>(dock,"root")).X;
                velocities[view]=view.ShiftMotion.Velocity;
            }
            dock.EndInternalDrag(true);
            Check(store.Settings.Items[5].Id==source.Item.Id,"Successful drop commits requested shortcut order");
            Check(Order(new Store(store.DataDirectory,desktop,desktop))==Order(store),"Successful drop persists shortcut order");
            foreach(DockWindow.IconView view in Field<IList>(dock,"views")) if(view!=source) {
                Near(view.Button.TranslatePoint(new Point(0,0),Field<Grid>(dock,"root")).X,positions[view],.01,"Commit preserves neighbor rendered position");
                Near(view.ShiftMotion.Velocity,velocities[view],.000001,"Commit preserves neighbor motion velocity");
            }
            CheckLanding(dock,source,"Committed drop");
            Render(dock,"ui-drag-settled.png");

            original=Order(store); source=View(dock,0);
            StartDrag(dock,source); dock.UpdateInternalDrag(new Point(-1000,-1000)); Steps(dock,.08); dock.EndInternalDrag(true);
            Check(Order(store)==original,"Dropping outside panel cancels shortcut reorder");
            CheckLanding(dock,source,"Outside cancellation");

            source=View(dock,0); StartDrag(dock,source); dock.UpdateInternalDrag(IconCenter(dock,View(dock,5))); Steps(dock,.08);
            var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(dock),Environment.TickCount,Key.Escape) { RoutedEvent=Keyboard.PreviewKeyDownEvent };
            dock.RaiseEvent(escape);
            Check(escape.Handled && !Field<bool>(dock,"internalDrag"),"Escape cancels active internal drag");
            Check(Order(store)==original,"Escape preserves shortcut order");
            CheckLanding(dock,source,"Escape cancellation");

            source=View(dock,0); StartDrag(dock,source); dock.UpdateInternalDrag(IconCenter(dock,View(dock,5))); Steps(dock,.08);
            dock.ReleaseMouseCapture();
            Check(!Field<bool>(dock,"internalDrag"),"Lost pointer capture cancels active drag");
            Check(Order(store)==original,"Lost capture preserves shortcut order");
            CheckLanding(dock,source,"Lost capture cancellation");
            Check(clicks==0 && Field<IList>(dock,"views").Cast<DockWindow.IconView>().All(x=>x.ClickStarted<0),"Drag and cancellation never generate a launch click");
            Check(Directory.GetFiles(desktop,"*",SearchOption.AllDirectories).Length==0,"Drag tests leave isolated desktop files unchanged");
        }
        static void DragCapacity(DockWindow dock,int count) {
            Call(dock,"ResetMagnification"); Steps(dock,1);
            var source=View(dock,0); string order=Order(dock.Store);
            StartDrag(dock,source);
            Check(Field<bool>(dock,"internalDrag"),count+" icons: internal drag starts");
            dock.UpdateInternalDrag(IconCenter(dock,View(dock,count-1))); Steps(dock,.08);
            if(count>1) Check(View(dock,1).Shift.X<-.5,count+" icons: neighbors animate during drag");
            Check(Order(dock.Store)==order,count+" icons: preview preserves source order");
            dock.EndInternalDrag(false); CheckLanding(dock,source,count+" icons cancellation");
        }
        static void RowLayouts(DockWindow dock,Store store) {
            int[] counts={1,2,5,20,30};
            foreach(int requestedRows in new[]{1,2,3,4}) foreach(int count in counts) {
                PrepareItems(store,count);
                store.Settings.Rows=requestedRows;
                store.Settings.IconSize=80;
                store.Settings.Zoom=1.65;
                store.Settings.ShowLabels=true;
                dock.ApplySettings(); dock.CenterBottom(); Pump(40); Steps(dock,1);
                string name=requestedRows+" rows, "+count+" icons";
                Layout(dock,count,name);
                var current=Field<IList>(dock,"views").Cast<DockWindow.IconView>().ToList();
                var rows=current.GroupBy(x=>Math.Round(ButtonPosition(dock,x).Y-x.Shift.Y,2)).OrderBy(x=>x.Key).ToList();
                Check(rows.Count==Math.Min(requestedRows,count),name+": exactly requested nonempty rows");
                Check(rows.Max(x=>x.Count())-rows.Min(x=>x.Count())<=1,name+": shortcuts are balanced between rows");
                Check(rows.SelectMany(x=>x).Select(x=>x.Item.Id).SequenceEqual(store.Settings.Items.Select(x=>x.Id)),name+": visual reading order follows saved shortcut order");
                foreach(var row in rows) {
                    var ordered=row.ToList();
                    for(int i=1;i<ordered.Count;i++) {
                        Near(ButtonPosition(dock,ordered[i]).X-ButtonPosition(dock,ordered[i-1]).X,Field<double>(dock,"slot"),.1,name+": same-row slots have regular spacing");
                    }
                }
                foreach(var view in current) Call(dock,"Animate",view,1.65,-5.0,150);
                Steps(dock,1);
                bool unclipped=true, rowsSeparated=true, labelsVisible=true;
                var imageBounds=new Dictionary<DockWindow.IconView,Rect>();
                foreach(var view in current) {
                    Rect bounds=view.Image.TransformToAncestor(dock).TransformBounds(new Rect(0,0,view.Image.ActualWidth,view.Image.ActualHeight));
                    imageBounds[view]=bounds;
                    unclipped &= bounds.Left>=-.5 && bounds.Right<=dock.ActualWidth+.5 && bounds.Top>=-.5 && bounds.Bottom<=dock.ActualHeight+.5;
                    foreach(var label in Descendants<TextBlock>(view.Button)) {
                        Rect textBounds=label.TransformToAncestor(dock).TransformBounds(new Rect(0,0,label.ActualWidth,label.ActualHeight));
                        labelsVisible &= textBounds.Top>=-.5 && textBounds.Bottom<=dock.ActualHeight+.5;
                    }
                }
                for(int i=1;i<rows.Count;i++) {
                    double upperBottom=rows[i-1].Max(x=>imageBounds[x].Bottom);
                    double lowerTop=rows[i].Min(x=>imageBounds[x].Top);
                    rowsSeparated &= upperBottom<=lowerTop+.5;
                }
                Check(unclipped,name+": maximum hover zoom fits the window in every row");
                Check(rowsSeparated,name+": enlarged icons do not overlap another row");
                Check(labelsVisible,name+": labels remain inside the window");
                if(count==20 && requestedRows>1) Render(dock,"ui-rows-"+requestedRows+".png");
                foreach(var view in current) Call(dock,"Bounce",view);
                Pump(140); Steps(dock,.06);
                bool clickUnclipped=current.All(delegate(DockWindow.IconView view) {
                    Rect bounds=view.Image.TransformToAncestor(dock).TransformBounds(new Rect(0,0,view.Image.ActualWidth,view.Image.ActualHeight));
                    return bounds.Left>=-.5 && bounds.Right<=dock.ActualWidth+.5 && bounds.Top>=-.5 && bounds.Bottom<=dock.ActualHeight+.5;
                });
                Check(clickUnclipped,name+": maximum hover plus click bounce fits the window");
                Pump(400);
                Call(dock,"ResetMagnification"); Steps(dock,1);
            }
            store.Settings.ShowLabels=false;
            store.Settings.IconSize=52;
            store.Settings.Zoom=1.28;
        }
        static void CrossRowDrag(DockWindow dock,Store store,string desktop,int sourceIndex,int targetIndex,string name,bool commit) {
            ShowOwnTestWindow(dock); Call(dock,"ResetMagnification"); Steps(dock,1);
            store.Save(); string original=Order(store);
            var source=View(dock,sourceIndex);
            Point target=IconCenter(dock,View(dock,targetIndex));
            StartDrag(dock,source);
            Check(Field<bool>(dock,"internalDrag"),name+": internal drag starts");
            dock.UpdateInternalDrag(target); Steps(dock,.08);
            var current=Field<IList>(dock,"views").Cast<DockWindow.IconView>().ToList();
            Check(current.Any(x=>Math.Abs(x.Shift.Y)>1),name+": neighbors animate vertically between rows");
            Check(Order(store)==original,name+": preview preserves in-memory shortcut order");
            Check(Order(new Store(store.DataDirectory,desktop,desktop))==original,name+": preview preserves saved shortcut order");
            var positions=current.ToDictionary(x=>x,x=>ButtonPosition(dock,x));
            var velocityX=current.ToDictionary(x=>x,x=>x.ShiftMotion.Velocity);
            var velocityY=current.ToDictionary(x=>x,x=>x.ShiftYMotion.Velocity);
            dock.EndInternalDrag(commit);
            Check(commit ? store.Settings.Items[targetIndex].Id==source.Item.Id : Order(store)==original,name+": drop produces intended order");
            Check(Order(new Store(store.DataDirectory,desktop,desktop))==Order(store),name+": persisted order matches result");
            foreach(var view in current.Where(x=>x!=source)) {
                Point position=ButtonPosition(dock,view);
                Near(position.X,positions[view].X,.01,name+": neighbor X stays continuous on release");
                Near(position.Y,positions[view].Y,.01,name+": neighbor Y stays continuous on release");
                Near(view.ShiftMotion.Velocity,velocityX[view],.000001,name+": neighbor horizontal velocity stays continuous");
                Near(view.ShiftYMotion.Velocity,velocityY[view],.000001,name+": neighbor vertical velocity stays continuous");
            }
            CheckLanding(dock,source,name);
            Check(current.All(x=>Math.Abs(x.Shift.X)<.01 && Math.Abs(x.Shift.Y)<.01),name+": all neighbors settle into their final slots");
        }
        static void RowDrags(DockWindow dock,Store store,string desktop) {
            foreach(int requestedRows in new[]{2,3,4}) {
                PrepareItems(store,20); store.Settings.Rows=requestedRows;
                dock.ApplySettings(); dock.CenterBottom(); Pump(100); Steps(dock,1);
                CrossRowDrag(dock,store,desktop,0,19,requestedRows+" rows forward",true);
                CrossRowDrag(dock,store,desktop,19,0,requestedRows+" rows reverse",true);
                CrossRowDrag(dock,store,desktop,0,19,requestedRows+" rows cancelled",false);
            }
            store.Settings.Rows=1;
            PrepareItems(store,30); dock.ApplySettings(); dock.CenterBottom(); Pump(100); Steps(dock,1);
        }
        static void OpenMenu(DockWindow dock,DesktopHost host,ContextMenu menu,FrameworkElement target,string name) {
            var tracked=Field<HashSet<ContextMenu>>(dock,"openMenus");
            menu.PlacementTarget=target;
            menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Top;
            menu.IsOpen=true;
            Pump(260);
            Check(menu.IsOpen,name+": actual WPF context menu stays open after visibility polling");
            Check(tracked.Contains(menu) && tracked.Count==1,name+": opened menu is tracked exactly once");
            host.Refresh(); Pump(40);
            Check(IsWindowVisible(new WindowInteropHelper(dock).Handle),name+": native panel remains visible while menu is open");
        }
        static void CloseMenu(DockWindow dock,DesktopHost host,ContextMenu menu,string name) {
            menu.IsOpen=false; Pump(260); host.Refresh();
            Check(!menu.IsOpen,name+": context menu closes");
            Check(Field<HashSet<ContextMenu>>(dock,"openMenus").Count==0,name+": closing removes all menu tracking");
            Check(IsWindowVisible(new WindowInteropHelper(dock).Handle),name+": own active panel remains visible after closing menu");
        }
        static void ContextMenus(DockWindow dock) {
            ShowOwnTestWindow(dock);
            var tracked=Field<HashSet<ContextMenu>>(dock,"openMenus");
            var host=new DesktopHost(dock,delegate { return false; },false,
                delegate { return tracked.Any(x=>x.IsOpen); });
            typeof(DockWindow).GetField("host",PrivateInstance).SetValue(dock,host);
            try {
                host.Start(); host.ShowRequested(); dock.Activate(); Pump(60); host.Refresh();
                Console.WriteLine("Menu fixture state: active="+dock.IsActive+", managed="+dock.Visibility+", native="+IsWindowVisible(new WindowInteropHelper(dock).Handle));
                Check(IsWindowVisible(new WindowInteropHelper(dock).Handle),"Menu fixture: own dock starts visible with desktop visibility controller enabled");
                OpenMenu(dock,host,dock.ContextMenu,dock,"Panel right-click menu");
                CloseMenu(dock,host,dock.ContextMenu,"Panel right-click menu");
                var icon=View(dock,0);
                OpenMenu(dock,host,icon.Button.ContextMenu,icon.Button,"Icon right-click menu");
                CloseMenu(dock,host,icon.Button.ContextMenu,"Icon right-click menu");

                var plus=Field<StackPanel>(dock,"strip").Children.OfType<Button>().Last();
                plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(260);
                var addedMenu=tracked.SingleOrDefault();
                Check(addedMenu!=null && addedMenu.IsOpen,"Plus button opens an actual tracked WPF menu");
                Check(addedMenu!=dock.ContextMenu,"Plus button tracks its separately created menu");
                host.Refresh();
                Check(IsWindowVisible(new WindowInteropHelper(dock).Handle),"Plus-button menu keeps the native panel visible after polling");
                if(addedMenu!=null) CloseMenu(dock,host,addedMenu,"Plus-button menu");

                for(int i=0;i<3;i++) {
                    OpenMenu(dock,host,dock.ContextMenu,dock,"Repeated panel menu "+(i+1));
                    CloseMenu(dock,host,dock.ContextMenu,"Repeated panel menu "+(i+1));
                }
                var previous=dock.ContextMenu;
                OpenMenu(dock,host,previous,dock,"Menu before settings rebuild");
                dock.ApplySettings(); Pump(260);
                Check(tracked.All(x=>x.IsOpen),"Settings rebuild retains no closed menu entries");
                Check(IsWindowVisible(new WindowInteropHelper(dock).Handle),"Settings rebuild while menu is open keeps panel visible");
                CloseMenu(dock,host,previous,"Old menu after settings rebuild");
                OpenMenu(dock,host,dock.ContextMenu,dock,"Rebuilt panel right-click menu");
                CloseMenu(dock,host,dock.ContextMenu,"Rebuilt panel right-click menu");
            }
            finally {
                foreach(var menu in tracked.ToArray()) menu.IsOpen=false;
                Pump(40); host.Dispose(); ShowOwnTestWindow(dock);
            }
        }
        [STAThread] public static int Main(string[] args) {
            root=Path.GetFullPath(args.Length>0?args[0]:"work/animation-tests");
            Directory.CreateDirectory(root);
            string run=Path.Combine(root,"data-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(run); Directory.CreateDirectory(Path.Combine(run,"Desktop"));
            Program.TestMode=true; Program.DataDir=run;
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            DockWindow fresh=null, restored=null; SettingsWindow settings=null;
            try {
                var store=new Store(Path.Combine(run,"fresh"),Path.Combine(run,"Desktop"),Path.Combine(run,"Desktop"));
                PrepareItems(store,6);
                fresh=new DockWindow(store) { ShowActivated=false }; app.MainWindow=fresh; fresh.Show(); Pump(1400);
                Field<TextBlock>(fresh,"notice").Visibility=Visibility.Collapsed;
                Rect area=Native.WorkingArea(fresh);
                Near(fresh.Left,area.Left+(area.Width-fresh.Width)/2,1,"Fresh startup horizontally centered");
                Near(fresh.Top,area.Bottom-fresh.Height-10,1,"Fresh startup above taskbar");
                Check(Field<IList>(fresh,"views").Cast<object>().All(delegate(object v) { return ((Image)ViewField(v,"Image")).Source!=null; }),"Native icons loaded asynchronously, including computer and recycle bin");
                foreach(object view in Field<IList>(fresh,"views")) {
                    var item=(DockItem)ViewField(view,"Item");
                    if(item.Path=="shell:MyComputerFolder" || item.Path=="shell:RecycleBinFolder")
                        Check(((Image)ViewField(view,"Image")).Source is BitmapSource,"Native shell bitmap loaded, not fallback: "+item.Path);
                }
                Layout(fresh,6,"Default"); Render(fresh,"ui-dock.png");
                Animations(fresh);
                DragAnimations(fresh,store,Path.Combine(run,"Desktop"));
                PrepareItems(store,1); fresh.ApplySettings(); fresh.CenterBottom(); Pump(150); Layout(fresh,1,"One icon");
                DragCapacity(fresh,1);
                PrepareItems(store,21); fresh.ApplySettings(); fresh.CenterBottom(); Pump(150); Steps(fresh,1); Layout(fresh,21,"Twenty-one icons");
                DragCapacity(fresh,21); Render(fresh,"ui-21.png");
                PrepareItems(store,30); store.Settings.IconSize=80; store.Settings.Zoom=1.65; fresh.ApplySettings(); fresh.CenterBottom(); Pump(700);
                Layout(fresh,30,"Thirty icons at maximum requested size");
                Check(Field<double>(fresh,"effectiveSize")<=80,"Effective icon size adapts to screen");
                Render(fresh,"ui-30.png");
                DragCapacity(fresh,30);
                object first=Field<IList>(fresh,"views")[0];
                object last=Field<IList>(fresh,"views")[29];
                typeof(DockWindow).GetMethod("Animate",PrivateInstance).Invoke(fresh,new object[]{first,1.65,-5.0,1});
                typeof(DockWindow).GetMethod("Animate",PrivateInstance).Invoke(fresh,new object[]{last,1.65,-5.0,1});
                Steps(fresh,1); Layout(fresh,30,"Thirty icons enlarged at both ends");
                Call(fresh,"ResetMagnification"); Steps(fresh,1);
                RowLayouts(fresh,store);
                RowDrags(fresh,store,Path.Combine(run,"Desktop"));
                ContextMenus(fresh);
                settings=new SettingsWindow(fresh) { Owner=fresh, ShowActivated=false }; settings.Show(); Pump(250);
                var sliders=Descendants<Slider>(settings).ToList(); var checks=Descendants<CheckBox>(settings).ToList();
                Check(sliders.Count==3,"Settings exposes opacity, icon size and hover controls");
                Check(checks.Count==4,"Settings exposes labels, topmost, startup, desktop move");
                var rowButtons=Descendants<Button>(settings).Where(x=>System.Windows.Automation.AutomationProperties.GetName(x).StartsWith("Количество рядов: ",StringComparison.Ordinal)).ToList();
                Check(rowButtons.Count==4,"Settings exposes four row-layout choices");
                foreach(int selectedRows in new[]{2,3,4,1}) {
                    var choice=rowButtons.Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)=="Количество рядов: "+selectedRows);
                    choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(40); Steps(fresh,1);
                    Check(store.Settings.Rows==selectedRows,"Row selector updates model to "+selectedRows);
                    Check(new Store(store.DataDirectory,Path.Combine(run,"Desktop"),Path.Combine(run,"Desktop")).Settings.Rows==selectedRows,"Row selector persists choice "+selectedRows);
                    int rowCount=Field<IList>(fresh,"views").Cast<DockWindow.IconView>().Select(x=>Math.Round(ButtonPosition(fresh,x).Y,2)).Distinct().Count();
                    Check(rowCount==selectedRows,"Row selector immediately changes visual layout to "+selectedRows);
                }
                sliders[0].Value=45; Pump(100);
                Near(store.Settings.Opacity,.45,.001,"Opacity control updates model");
                var gradient=(LinearGradientBrush)Field<Border>(fresh,"glass").Background;
                Check(gradient.GradientStops.All(delegate(GradientStop stop) { return Math.Abs(stop.Color.A-114)<=1; }),"Opacity control updates rendered panel alpha");
                Render(settings,"ui-settings.png");
                settings.Close(); settings=null;
                PrepareItems(store,6); store.Settings.Rows=3; store.Settings.IconSize=52; store.Settings.Zoom=1.28; fresh.ApplySettings(); fresh.Left=area.Left+73; fresh.Top=area.Top+81; fresh.SaveQuietly();
                double savedLeft=fresh.Left,savedTop=fresh.Top;
                fresh.Hide();
                var reloaded=new Store(Path.Combine(run,"fresh"),Path.Combine(run,"Desktop"),Path.Combine(run,"Desktop"));
                Near(reloaded.Settings.Left,savedLeft,.001,"Saved left coordinate round-trips");
                Near(reloaded.Settings.Top,savedTop,.001,"Saved top coordinate round-trips");
                Check(reloaded.Settings.Rows==3,"Saved row count round-trips");
                restored=new DockWindow(reloaded) { ShowActivated=false }; restored.Show(); Pump(250);
                Near(restored.Left,savedLeft,1,"Saved startup left restored");
                Near(restored.Top,savedTop,1,"Saved startup top restored");
                Layout(restored,6,"Restored");
                Check(Field<IList>(restored,"views").Cast<DockWindow.IconView>().Select(x=>Math.Round(ButtonPosition(restored,x).Y,2)).Distinct().Count()==3,"Restored window uses saved three-row layout");
                Console.WriteLine("RESULT "+(failures.Count==0?"PASS":"FAIL")+" assertions="+assertions+" failures="+failures.Count);
            } catch(Exception ex) { failures.Add(ex.ToString()); Console.WriteLine("EXCEPTION "+ex); }
            finally { if(settings!=null) settings.Close(); if(restored!=null) restored.Close(); if(fresh!=null) fresh.Close(); }
            return failures.Count==0?0:1;
        }
    }
}
