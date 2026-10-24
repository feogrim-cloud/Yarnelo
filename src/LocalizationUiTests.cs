using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Polka {
    // This runner never calls Show/Activate, starts a tray icon or desktop host,
    // writes a real profile, or changes the user's startup setting.
    internal static class LocalizationUiTests {
        static int assertions;
        static readonly BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool value,string message) { if(!value) throw new Exception(message); assertions++; }
        static T Field<T>(object value,string name) { return (T)value.GetType().GetField(name,PrivateInstance).GetValue(value); }
        static IEnumerable<DependencyObject> Visuals(DependencyObject root) {
            yield return root;
            for(int index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
                foreach(var child in Visuals(VisualTreeHelper.GetChild(root,index))) yield return child;
        }
        static T Tagged<T>(SettingsWindow settings,string tag) where T:FrameworkElement {
            return Visuals((DependencyObject)settings.Content).OfType<T>().Single(element=>Object.Equals(element.Tag,tag));
        }
        static void CheckSettingsSheet(SettingsWindow settings,string directory,string language) {
            foreach(var area in new[]{new Size(1920,1040),new Size(1280,680),new Size(800,576),new Size(640,480)}) {
                settings.FitToWorkingArea(new Rect(new Point(),area));
                Check(settings.Width<=area.Width-23 && settings.Height<=area.Height-23,"settings sheet fits working area including high-DPI equivalent sizes");
                string name="settings-"+language+"-"+(int)area.Width+"x"+(int)area.Height+".png";
                Render(settings,Path.Combine(directory,name),new Size(settings.Width,settings.Height));
                var sheet=(FrameworkElement)settings.Content;
                Check(!Visuals(sheet).OfType<ScrollViewer>().Any(),"settings has no vertical or horizontal scrolling surface");
                var columns=Tagged<Grid>(settings,"settings-columns");
                foreach(var tag in new[]{"settings-appearance","settings-behavior"}) {
                    var column=Tagged<StackPanel>(settings,tag);
                    Check(column.DesiredSize.Height<=columns.ActualHeight+.1,"all settings fit their two-column layout: "+language+" "+tag+" desired="+column.DesiredSize.Height+" available="+columns.ActualHeight);
                }
                foreach(var control in Visuals(sheet).OfType<FrameworkElement>().Where(element=>element is Button || element is Slider || element is CheckBox || element is TextBlock)) {
                    if(control.ActualWidth==0 || control.ActualHeight==0) continue;
                    var bounds=control.TransformToAncestor(sheet).TransformBounds(new Rect(control.RenderSize));
                    Check(bounds.Left>=-.5 && bounds.Top>=-.5 && bounds.Right<=sheet.ActualWidth+.5 && bounds.Bottom<=sheet.ActualHeight+.5,
                        "all settings controls and text remain visible: "+language+" "+control.GetType().Name+" "+control.Tag);
                }
                Check(Visuals(sheet).OfType<Button>().Count(button=>System.Windows.Automation.AutomationProperties.GetName(button).StartsWith("Placement: ",StringComparison.Ordinal))==5,
                    "settings exposes all four centered edge positions and free placement");
            }
            settings.FitToWorkingArea(new Rect(0,0,1920,1040));
        }
        static void Render(FrameworkElement element,string path,Size size) {
            var window=element as Window;
            var content=window==null ? element : (FrameworkElement)window.Content;
            if(window!=null) {
                System.Windows.Documents.TextElement.SetForeground(content,window.Foreground);
                System.Windows.Documents.TextElement.SetFontFamily(content,window.FontFamily);
                window.Content=null;
            }
            try {
                content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);
                bitmap.Render(content); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                byte[] pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                int painted=0;
                for(int index=3;index<pixels.Length;index+=4) if(pixels[index]!=0) painted++;
                Check(painted>1000,"own visual render has painted pixels: "+Path.GetFileName(path));
                using(var stream=File.Create(path)) encoder.Save(stream);
            } finally { if(window!=null) window.Content=content; }
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
                store.Settings.Language="ru"; store.Settings.ShowLabels=true;
                store.Add("shell:MyComputerFolder"); store.Add("shell:RecycleBinFolder");
                var dock=new DockWindow(store); dock.ApplySettings();
                var settings=new SettingsWindow(dock);
                // A failed Windows startup update must never leave a success
                // checkmark behind. Inject the failing action, not a real task.
                byte[] beforeStartupFailure=File.ReadAllBytes(Path.Combine(store.DataDirectory,"settings.json"));
                var checkPanel=new StackPanel();
                var addCheck=typeof(SettingsWindow).GetMethod("AddCheck",PrivateInstance);
                var failedStartup=(CheckBox)addCheck.Invoke(settings,new object[]{checkPanel,"Startup",true,new Action<bool>(delegate { throw new IOException("Isolated startup write failure"); })});
                failedStartup.IsChecked=false;
                Check(failedStartup.IsChecked==true,"failed startup update restores the previous checkbox state");
                Check(beforeStartupFailure.SequenceEqual(File.ReadAllBytes(Path.Combine(store.DataDirectory,"settings.json"))),"failed startup update preserves the persisted profile");
                typeof(DockWindow).GetField("settingsWindow",PrivateInstance).SetValue(dock,settings);
                Check(dock.Title=="Yarnelo — панель ярлыков" && settings.Title=="Настройки — Yarnelo","Russian dock and settings strings");
                var language=dock.ContextMenu.Items.OfType<MenuItem>().Single(item=>Object.Equals(item.Header,"Язык"));
                Check(language.Items.Count==2,"dock exposes both language choices");
                var english=(MenuItem)language.Items[0]; var russian=(MenuItem)language.Items[1];
                Check(!english.IsChecked && russian.IsChecked,"current Russian choice is checked");
                // Apply the production control template without opening any popup window.
                dock.ContextMenu.ApplyTemplate(); language.ApplyTemplate();
                var popup=language.Template.FindName("PART_Popup",language) as Popup;
                Check(popup!=null && popup.Child!=null,"production submenu template creates its real popup surface");
                Render((FrameworkElement)popup.Child,Path.Combine(directory,"language-submenu-ru.png"),new Size(180,80));
                english.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(store.Settings.Language=="en" && L10n.Language=="en","language menu selection updates active model");
                Check(dock.Title=="Yarnelo — shortcut dock" && settings.Title=="Settings — Yarnelo","existing dock and open settings translate immediately");
                language=dock.ContextMenu.Items.OfType<MenuItem>().Single(item=>Object.Equals(item.Header,"Language"));
                Check(((MenuItem)language.Items[0]).IsChecked && !((MenuItem)language.Items[1]).IsChecked,"rebuilt submenu checks English");
                ((MenuItem)language.Items[0]).IsChecked=false;
                ((MenuItem)language.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                language=dock.ContextMenu.Items.OfType<MenuItem>().Single(item=>Object.Equals(item.Header,"Language"));
                Check(((MenuItem)language.Items[0]).IsChecked,"selecting the current language cannot leave it unchecked");
                Check(new Store(store.DataDirectory,desktop,desktop).Settings.Language=="en","menu selection persists to isolated profile");
                Check(store.Settings.Items.Select(L10n.DisplayName).SequenceEqual(new[]{"This PC","Recycle Bin"}),"both built-in dock labels translate");
                var views=Field<List<DockWindow.IconView>>(dock,"views");
                Check(((ToolTip)views[0].Button.ToolTip).Content.ToString()=="This PC","existing icon tooltip translates immediately");
                string namesPath=Path.Combine(store.DataDirectory,"settings.json");
                var autoAdd=Tagged<CheckBox>(settings,"setting-AutoAddDesktop");
                Check(autoAdd.IsChecked==false,"desktop auto-add remains opt-in for an existing profile");
                autoAdd.IsChecked=true;
                Check(store.Settings.AutoAddDesktopShortcuts && new Store(store.DataDirectory,desktop,desktop).Settings.AutoAddDesktopShortcuts,"desktop auto-add checkbox persists its selected state");
                byte[] beforeAutoFailure=File.ReadAllBytes(namesPath);
                using(var locked=new FileStream(namesPath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    autoAdd.IsChecked=false;
                    Check(autoAdd.IsChecked==true && store.Settings.AutoAddDesktopShortcuts,"failed auto-add save restores model and checkmark");
                }
                Check(beforeAutoFailure.SequenceEqual(File.ReadAllBytes(namesPath)),"failed auto-add save preserves exact profile bytes");
                autoAdd.IsChecked=false;
                Check(!store.Settings.AutoAddDesktopShortcuts,"manual desktop adding remains available");
                string settingsPath=Path.Combine(store.DataDirectory,"settings.json");
                byte[] beforeFailure=File.ReadAllBytes(settingsPath);
                using(var locked=new FileStream(settingsPath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    var failedChoice=(MenuItem)language.Items[1]; failedChoice.IsChecked=true;
                    failedChoice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(store.Settings.Language=="en" && L10n.Language=="en" && settings.Title=="Settings — Yarnelo", "failed language save rolls back model and existing settings interface");
                }
                Check(beforeFailure.SequenceEqual(File.ReadAllBytes(settingsPath)),"failed language save preserves exact persisted profile bytes");
                language=dock.ContextMenu.Items.OfType<MenuItem>().Single(item=>Object.Equals(item.Header,"Language"));
                Check(((MenuItem)language.Items[0]).IsChecked && !((MenuItem)language.Items[1]).IsChecked,"failed language save restores the previous menu checkmarks");
                Check(Field<TextBlock>(dock,"notice").Text.StartsWith("Could not save settings: ",StringComparison.Ordinal),"failed language save reports the error in the retained interface language");
                Field<TextBlock>(dock,"notice").Visibility=Visibility.Collapsed;
                Render(dock,Path.Combine(directory,"dock-en.png"),new Size(dock.Width,dock.Height));
                Render(settings,Path.Combine(directory,"settings-en.png"),new Size(settings.Width,settings.Height));
                CheckSettingsSheet(settings,directory,"en");
                var square=Tagged<Button>(settings,"corner-preset-0");
                square.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(store.Settings.CornerRadius==0 && new Store(store.DataDirectory,desktop,desktop).Settings.CornerRadius==0,"square corner preset updates and persists production preference");
                Tagged<Button>(settings,"corner-preset-100").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(store.Settings.CornerRadius==100 && new Store(store.DataDirectory,desktop,desktop).Settings.CornerRadius==100,"fully rounded corner preset updates and persists production preference");
                var leftPosition=Visuals((DependencyObject)settings.Content).OfType<Button>().Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="Placement: left");
                leftPosition.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(store.Settings.Placement=="left" && new Store(store.DataDirectory,desktop,desktop).Settings.Placement=="left","side placement button persists its chosen anchor");
                Check(Visuals((DependencyObject)settings.Content).OfType<TextBlock>().Any(block=>block.Text=="Number of columns"),"side placement updates the row selector to upright icon columns");
                var freePosition=Visuals((DependencyObject)settings.Content).OfType<Button>().Single(button=>System.Windows.Automation.AutomationProperties.GetName(button)=="Placement: free");
                freePosition.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(store.Settings.Placement=="free","free placement remains available after choosing an edge anchor");
                dock.SetLanguage("ru");
                Check(dock.Title=="Yarnelo — панель ярлыков" && settings.Title=="Настройки — Yarnelo","switching back updates both existing windows");
                Render(settings,Path.Combine(directory,"settings-ru.png"),new Size(settings.Width,settings.Height));
                CheckSettingsSheet(settings,directory,"ru");
                Check(Field<object>(dock,"host")==null && Field<object>(dock,"tray")==null && !dock.IsVisible && !settings.IsVisible,"localization tests never start a visible window, tray or desktop host");
                Console.WriteLine("PASS: "+assertions+" localization UI assertions; own-window renders: "+directory);
                return 0;
            } catch(Exception ex) { Console.WriteLine(ex); return 1; }
            finally { app.Shutdown(); }
        }
    }
}
