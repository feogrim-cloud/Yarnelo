// Render the actual Windows controls without showing a window or reading a user profile.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Polka {
    [DataContract] internal sealed class DemoEntry {
        [DataMember] public string Name {get;set;}
        [DataMember] public string Path {get;set;}
    }
    internal static class ScreenshotRenderer {
        static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Field(object owner,string name) { return owner.GetType().GetField(name,Hidden).GetValue(owner); }
        static void Layout(FrameworkElement control,double width,double height) {
            control.Measure(new Size(width,height)); control.Arrange(new Rect(0,0,width,height)); control.UpdateLayout();
        }
        static DrawingVisual Backdrop(int width,int height,string title,string subtitle) {
            var visual=new DrawingVisual();
            using(var draw=visual.RenderOpen()) {
                var background=new LinearGradientBrush(Color.FromRgb(16,25,48),Color.FromRgb(58,47,89),35);
                draw.DrawRectangle(background,null,new Rect(0,0,width,height));
                var glow=new RadialGradientBrush();
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(100,69,134,194),0));
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(0,69,134,194),1));
                draw.DrawEllipse(glow,null,new Point(width*.25,height*.85),width*.65,height*.8);
                var violet=new RadialGradientBrush();
                violet.GradientStops.Add(new GradientStop(Color.FromArgb(75,154,112,224),0));
                violet.GradientStops.Add(new GradientStop(Color.FromArgb(0,154,112,224),1));
                draw.DrawEllipse(violet,null,new Point(width*.95,height*.1),width*.55,height*.9);
                Text(draw,"YARNELO",42,31,15,Color.FromRgb(177,196,226));
                Text(draw,title,42,62,30,Color.FromRgb(245,247,255));
                Text(draw,subtitle,43,109,14,Color.FromRgb(174,188,213));
                Text(draw,"Windows interface · demonstration shortcuts",42,height-34,12,Color.FromRgb(150,166,193));
            }
            return visual;
        }
        static void Text(DrawingContext draw,string value,double x,double y,double size,Color color) {
            var text=new FormattedText(value,System.Globalization.CultureInfo.GetCultureInfo("en-US"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,new SolidColorBrush(color),1.0);
            draw.DrawText(text,new Point(x,y));
        }
        static void Save(string file,int width,int height,string title,string subtitle,FrameworkElement panel,Rect bounds,FrameworkElement menu,Rect menuBounds,FrameworkElement submenu=null,Rect submenuBounds=default(Rect)) {
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(Backdrop(width,height,title,subtitle));
            var layer=new DrawingVisual();
            using(var draw=layer.RenderOpen()) {
                var brush=new VisualBrush(panel) { Stretch=Stretch.Fill };
                draw.DrawRectangle(brush,null,bounds);
                if(menu!=null) draw.DrawRectangle(new VisualBrush(menu) { Stretch=Stretch.Fill },null,menuBounds);
                if(submenu!=null) draw.DrawRectangle(new VisualBrush(submenu) { Stretch=Stretch.Fill },null,submenuBounds);
            }
            bitmap.Render(layer);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var stream=File.Create(file)) encoder.Save(stream);
            Console.WriteLine(Path.GetFileName(file)+" "+width+"x"+height);
        }
        static FrameworkElement Dock(DockWindow dock) {
            dock.ApplySettings();
            foreach(object view in (IList)Field(dock,"views")) {
                var item=(DockItem)view.GetType().GetField("Item").GetValue(view);
                var image=(Image)view.GetType().GetField("Image").GetValue(view);
                image.Source=Native.GetIcon(item.Path);
                if(image.Source==null) throw new InvalidOperationException("No icon available for demo: "+item.Name);
            }
            var panel=(FrameworkElement)dock.Content;
            Layout(panel,dock.Width,dock.Height);
            return panel;
        }
        [STAThread] static int Main(string[] args) {
            if(args.Length!=3) throw new ArgumentException("Usage: ScreenshotRenderer MANIFEST OUTPUT NEW-SCRATCH-DIRECTORY");
            var entries=(List<DemoEntry>)new DataContractJsonSerializer(typeof(List<DemoEntry>)).ReadObject(File.OpenRead(args[0]));
            if(entries.Count<12 || entries.Count>30) throw new ArgumentException("Supply 12 to 30 demonstration icon references.");
            string output=Path.GetFullPath(args[1]), scratch=Path.GetFullPath(args[2]);
            if(Directory.Exists(scratch)) throw new IOException("Screenshot scratch directory must be new.");
            Directory.CreateDirectory(scratch); Directory.CreateDirectory(output);
            Program.TestMode=true; Program.DataDir=Path.Combine(scratch,"profile");
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            var store=new Store(Program.DataDir,scratch,scratch);
            store.Settings.Language="en"; L10n.SetLanguage("en");
            store.Settings.MoveDesktopShortcuts=false; store.Settings.LaunchAtLogin=false;
            store.Settings.IconSize=64; store.Settings.Zoom=1.5; store.Settings.Opacity=.60;
            store.Settings.Rows=1; store.Settings.ShowLabels=false;
            int number=0;
            foreach(var entry in entries.GetRange(0,10)) store.Settings.Items.Add(new DockItem { Id="demo"+(number++),Name=entry.Name,Path=entry.Path,OriginalPath=entry.Path });
            var dock=new DockWindow(store);
            var panel=Dock(dock);
            var views=(IList)Field(dock,"views");
            var hover=(DockWindow.IconView)views[2];
            typeof(DockWindow).GetMethod("Animate",Hidden).Invoke(dock,new object[]{hover,1.5,-6.0,150});
            for(int i=0;i<160;i++) dock.AdvanceAnimations(1.0/120);
            Layout(panel,dock.Width,dock.Height);
            Save(Path.Combine(output,"yarnelo-single-row.png"),1440,440,"Your shortcuts. In one place.","A translucent dock with smooth hover magnification.",panel,new Rect((1440-dock.Width)/2,211,dock.Width,dock.Height),null,Rect.Empty);
            store.Settings.Rows=4; store.Settings.ShowLabels=true; store.Settings.Items.Clear();
            foreach(var entry in entries) store.Settings.Items.Add(new DockItem { Id="demo"+(number++),Name=entry.Name,Path=entry.Path,OriginalPath=entry.Path });
            panel=Dock(dock);
            Save(Path.Combine(output,"yarnelo-multi-row.png"),1440,810,"Make room for what you use.","Up to 30 shortcuts · 1, 2, 3 or 4 rows · adjustable opacity and icon size.",panel,new Rect((1440-dock.Width)/2,180,dock.Width,dock.Height),null,Rect.Empty);
            store.Settings.Rows=1; store.Settings.ShowLabels=false;
            store.Settings.Items.RemoveRange(7,store.Settings.Items.Count-7);
            panel=Dock(dock);
            var menu=dock.ContextMenu;
            menu.ApplyTemplate(); menu.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            menu.Arrange(new Rect(new Point(0,0),menu.DesiredSize)); menu.UpdateLayout();
            double menuWidth=Math.Max(280,menu.ActualWidth),menuHeight=menu.ActualHeight;
            Layout(menu,menuWidth,menuHeight);
            var language=menu.Items.OfType<MenuItem>().Single(x=>x.Items.Count==2);
            language.ApplyTemplate();
            var popup=(System.Windows.Controls.Primitives.Popup)language.Template.FindName("PART_Popup",language);
            var submenu=(FrameworkElement)popup.Child;
            submenu.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            Layout(submenu,Math.Max(150,submenu.DesiredSize.Width),submenu.DesiredSize.Height);
            Save(Path.Combine(output,"yarnelo-language-menu.png"),1440,670,"English or Russian. Your choice.","Change the interface language from the dock menu. Your choice is saved.",panel,new Rect(110,440,dock.Width,dock.Height),menu,new Rect(900,177,menuWidth,menuHeight),submenu,new Rect(900+menuWidth+4,330,submenu.ActualWidth,submenu.ActualHeight));
            if(PresentationSource.FromVisual(dock)!=null) throw new InvalidOperationException("Renderer must never show a native dock window.");
            Console.WriteLine("Rendered actual controls without showing windows, starting applications, moving shortcuts or reading the installed profile.");
            return 0;
        }
    }
}
