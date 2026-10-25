using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Forms = System.Windows.Forms;

namespace Polka {
    internal sealed class ThemeColorValue : DependencyObject {
        public static readonly DependencyProperty ColorProperty=DependencyProperty.Register("Color",typeof(Color),typeof(ThemeColorValue));
        public Color Color { get { return (Color)GetValue(ColorProperty); } set { SetValue(ColorProperty,value); } }
    }
    internal sealed class ThemePalette {
        static readonly Dictionary<string,ThemePalette> cache=new Dictionary<string,ThemePalette>();
        internal string Id;
        internal Color Surface, Foreground, Accent, Muted, Stroke, Menu, MenuStroke, Hint, HintStroke, Highlight, Choice, Selected, SelectedBorder, Handle, Empty, Plus, Divider;
        internal bool Original { get { return Id=="dusk"; } }
        internal static Color Hex(string value) { return (Color)ColorConverter.ConvertFromString(value); }
        internal static string Text(Color color) { return "#"+color.A.ToString("X2")+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2"); }
        internal static Color Mix(Color first,Color second,double amount) {
            return Color.FromRgb((byte)Math.Round(first.R+(second.R-first.R)*amount),(byte)Math.Round(first.G+(second.G-first.G)*amount),(byte)Math.Round(first.B+(second.B-first.B)*amount));
        }
        internal static SolidColorBrush Brush(Color color) { return new SolidColorBrush(color); }
        internal Color AccentText { get { return Id=="frost"?Colors.White:Surface; } }
        internal Color Role(string role) {
            switch(role) {
                case "Surface": return Surface; case "Foreground": return Foreground; case "Accent": return Accent; case "Muted": return Muted;
                case "Stroke": return Stroke; case "Menu": return Menu; case "MenuStroke": return MenuStroke; case "Hint": return Hint; case "HintStroke": return HintStroke;
                case "Highlight": return Highlight; case "Choice": return Choice; case "Selected": return Selected; case "SelectedBorder": return SelectedBorder;
                case "Handle": return Handle; case "Empty": return Empty; case "Plus": return Plus; case "Divider": return Divider;
                case "FocusFill": return Original?Hex("#405E9BFF"):Color.FromArgb(64,Accent.R,Accent.G,Accent.B);
                case "HoverBorder": return Original?Hex("#8BAEFF"):Accent; case "FocusBorder": return Original?Hex("#C5D7FF"):Accent;
                case "Notice": return Original?Color.FromArgb(245,30,33,44):Color.FromArgb(245,Menu.R,Menu.G,Menu.B);
                case "SliderFill": case "SliderThumb": return Accent;
                case "SliderTrack": return Stroke;
                case "CheckFill": return Accent; case "CheckMark": return AccentText;
                case "CheckOff": return Choice;
                case "GlassStroke": return Original?Color.FromArgb(75,255,255,255):Color.FromArgb(95,Accent.R,Accent.G,Accent.B);
                case "Dialog": return Original?Color.FromRgb(30,34,45):Surface;
                case "Separator": return Original?Hex("#454F65"):Stroke;
                default: throw new ArgumentException("Unknown theme brush: "+role);
            }
        }
        internal static ThemePalette For(string theme) {
            string id=ThemeCatalog.Normalize(theme);
            lock(cache) { ThemePalette palette; if(!cache.TryGetValue(id,out palette)) { palette=Create(id); cache.Add(id,palette); } return palette; }
        }
        static ThemePalette Create(string theme) {
            string id=ThemeCatalog.Normalize(theme);
            if(id=="dusk") return new ThemePalette { Id=id, Surface=Hex("#191D28"), Foreground=Colors.White, Accent=Hex("#74A1FF"), Muted=Hex("#9EAAC2"), Stroke=Hex("#424B60"), Menu=Hex("#232733"), MenuStroke=Hex("#484F63"), Hint=Hex("#202532"), HintStroke=Hex("#4B566D"), Highlight=Hex("#3B527B"), Choice=Hex("#242B3A"), Selected=Hex("#3A5891"), SelectedBorder=Hex("#74A1FF"), Handle=Hex("#979FB3"), Empty=Hex("#C0C8D9"), Plus=Hex("#B8C6E0"), Divider=Color.FromArgb(45,255,255,255) };
            string background=id=="frost"?"#EEF6FD":id=="graphite"?"#191C20":id=="ember"?"#37241F":"#17182F";
            string foreground=id=="frost"?"#203340":id=="graphite"?"#E6E8EC":id=="ember"?"#F0E1D0":"#ECEAFC";
            string accent=id=="frost"?"#357CBA":id=="graphite"?"#AAD0BD":id=="ember"?"#EAB981":"#C2B0FF";
            var p=new ThemePalette { Id=id, Surface=Hex(background), Foreground=Hex(foreground), Accent=Hex(accent) };
            p.Muted=Mix(p.Surface,p.Foreground,.68); p.Stroke=Mix(p.Surface,p.Foreground,.20);
            p.Menu=p.Hint=p.Surface; p.MenuStroke=p.HintStroke=p.Stroke; p.Highlight=Mix(p.Surface,p.Accent,.21);
            p.Choice=Mix(p.Surface,p.Foreground,.065); p.Selected=Mix(p.Surface,p.Accent,.24); p.SelectedBorder=p.Accent;
            p.Handle=p.Muted; p.Empty=p.Foreground; p.Plus=p.Muted; p.Divider=Color.FromArgb(45,p.Foreground.R,p.Foreground.G,p.Foreground.B);
            return p;
        }
    }

    public class DockWindow : Window {
        internal Store Store;
        Border glass;
        StackPanel strip;
        Grid iconGrid;
        Grid root;
        Canvas dragLayer;
        TextBlock notice;
        DispatcherTimer noticeTimer, recycleTimer;
        readonly List<IconView> views = new List<IconView>();
        readonly HashSet<ContextMenu> openMenus = new HashSet<ContextMenu>();
        readonly Dictionary<string,ImageSource> iconCache = new Dictionary<string,ImageSource>(StringComparer.OrdinalIgnoreCase);
        Forms.NotifyIcon tray;
        DesktopHost host;
        SettingsWindow settingsWindow;
        DesktopShortcutWatcher desktopWatcher;
        DispatcherTimer desktopWatcherTimer;
        bool desktopWatcherInjected, desktopLimitNotice;
        readonly Dictionary<string,SolidColorBrush> themeBrushes=new Dictionary<string,SolidColorBrush>();
        readonly Dictionary<string,ThemeColorValue> themeColors=new Dictionary<string,ThemeColorValue>();
        readonly ResourceDictionary themeResources=new ResourceDictionary();
        readonly ThemeProgress themeProgress=new ThemeProgress();
        readonly Dictionary<string,Color> themeStartColors=new Dictionary<string,Color>(), themeTargetColors=new Dictionary<string,Color>();
        static readonly string[] ThemeRoles={"Surface","Foreground","Accent","Muted","Stroke","Menu","MenuStroke","Hint","HintStroke","Highlight","Choice","Selected","SelectedBorder","Handle","Empty","Plus","Divider","FocusFill","HoverBorder","FocusBorder","Notice","SliderFill","SliderThumb","SliderTrack","CheckFill","CheckMark","CheckOff","GlassStroke","Dialog","Separator"};
        LinearGradientBrush transitionBackground;
        Color startTop,startBottom,targetTop,targetBottom;
        double startRadius,targetRadius,auraWeight,startAura,targetAura;
        bool themePolarityChanges;
        readonly SolidColorBrush displayedGlassStroke=new SolidColorBrush();
        internal bool ThemeTransitionsEnabled { get; set; }
        internal bool ThemeTransitionActive { get { return themeProgress.Active; } }
        internal ResourceDictionary ThemeResources { get { return themeResources; } }
        internal static string ThemeResource(string role) { return "YarneloTheme"+role; }
        internal SolidColorBrush ThemeBrush(string role) { return themeBrushes[role]; }
        internal Color DisplayedThemeColor(string role) { return ThemeBrush(role).Color; }
        void SetDisplayedThemeColor(string role,Color color) { themeColors[role].Color=color; }
        double effectiveSize, slot, rowPitch;
        int layoutRows, layoutColumns;
        DockLayout layout;
        bool verticalDock;
        bool ready, moving, exiting, helperRunning, externalDragFeedback;
        Point pressPoint;
        IconView pressed;
        readonly Stopwatch animationClock=Stopwatch.StartNew();
        bool rendering, internalDrag, dragLanding, endingDrag, dragValid;
        TimeSpan previousFrame;
        IconView dragView;
        int dragSourceIndex, dragTargetIndex;
        Point dragGrabOffset;
        Image dragGhost;
        Border dragPlaceholder;
        ScaleTransform ghostScaleTransform;
        RotateTransform ghostRotation;
        readonly MotionValue ghostX=new MotionValue(0), ghostY=new MotionValue(0), ghostScale=new MotionValue(1), ghostAlpha=new MotionValue(1), placeholderX=new MotionValue(0), placeholderY=new MotionValue(0), ghostAngle=new MotionValue(0);
        const string ItemFormat="Polka.Reorder.Id";
        // Own the full popup surface; the system template paints a separate
        // light icon/checkmark gutter even when its background is changed.
        static readonly Dictionary<string,ResourceDictionary> menuThemes=new Dictionary<string,ResourceDictionary>();
        const string MenuThemeSource=@"
            <ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                <ControlTemplate x:Key='MenuSurface' TargetType='{x:Type ContextMenu}'>
                    <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                            BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' CornerRadius='10' SnapsToDevicePixels='True'>
                        <ScrollViewer Focusable='False' CanContentScroll='True' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'>
                            <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' SnapsToDevicePixels='True'/>
                        </ScrollViewer>
                    </Border>
                </ControlTemplate>
                <Style TargetType='{x:Type MenuItem}'>
                    <Setter Property='Padding' Value='10,5'/>
                    <Setter Property='MinHeight' Value='28'/>
                    <Setter Property='HorizontalContentAlignment' Value='Left'/>
                    <Setter Property='Template'>
                        <Setter.Value>
                            <ControlTemplate TargetType='{x:Type MenuItem}'>
                                <Grid>
                                    <Border x:Name='body' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' CornerRadius='6' SnapsToDevicePixels='True'>
                                        <Grid>
                                            <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
                                            <TextBlock x:Name='check' Text='✓' Visibility='Collapsed' Margin='0,0,8,0' VerticalAlignment='Center'/>
                                            <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
                                            <TextBlock x:Name='arrow' Grid.Column='2' Text='›' Visibility='Collapsed' Margin='18,0,0,0' VerticalAlignment='Center'/>
                                        </Grid>
                                    </Border>
                                    <Popup x:Name='PART_Popup' AllowsTransparency='True' Focusable='False' IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}' PlacementTarget='{Binding ElementName=body}' Placement='Right' HorizontalOffset='2' PopupAnimation='Fade'>
                                        <Border Background='#232733' BorderBrush='#484F63' BorderThickness='1' CornerRadius='10' Padding='5'>
                                            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
                                        </Border>
                                    </Popup>
                                </Grid>
                                <ControlTemplate.Triggers>
                                    <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='body' Property='Background' Value='#3B527B'/></Trigger>
                                    <Trigger Property='HasItems' Value='True'><Setter TargetName='arrow' Property='Visibility' Value='Visible'/></Trigger>
                                    <Trigger Property='IsChecked' Value='True'><Setter TargetName='check' Property='Visibility' Value='Visible'/></Trigger>
                                    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='body' Property='Opacity' Value='.4'/></Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
                <Style TargetType='{x:Type Separator}'>
                    <Setter Property='Template'>
                        <Setter.Value><ControlTemplate TargetType='{x:Type Separator}'><Border Height='1' Background='#454F65' Margin='8,4,8,4'/></ControlTemplate></Setter.Value>
                    </Setter>
                </Style>
            </ResourceDictionary>";

        internal ThemePalette Palette { get { return ThemePalette.For(Store.Settings.Theme); } }
        ThemeMotion Motion { get { return ThemeMotion.For(Store.Settings.Theme); } }
        ResourceDictionary MenuTheme {
            get {
                var palette=Palette; ResourceDictionary result;
                if(!menuThemes.TryGetValue(palette.Id,out result)) {
                    string source=MenuThemeSource.Replace("#232733","{DynamicResource YarneloThemeMenu}").Replace("#484F63","{DynamicResource YarneloThemeMenuStroke}").Replace("#3B527B","{DynamicResource YarneloThemeHighlight}").Replace("#454F65","{DynamicResource YarneloThemeSeparator}");
                    result=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(source); menuThemes.Add(palette.Id,result);
                }
                return result;
            }
        }

        internal class IconView {
            public DockItem Item;
            public Button Button;
            public Image Image;
            public ScaleTransform Scale;
            public TranslateTransform Translate;
            public TranslateTransform Shift;
            public TranslateTransform LabelTranslate;
            public DropShadowEffect HoverGlow;
            public MotionValue SizeMotion=new MotionValue(1), LiftMotion=new MotionValue(0), ShiftMotion=new MotionValue(0), ShiftYMotion=new MotionValue(0), AlphaMotion=new MotionValue(1);
            public double HoverScale=1, HoverLift, ClickStarted=-1;
            public double ScaleResponse=25;
            public bool Pressing;
        }

        public DockWindow(Store store) {
            Store=store;
            ThemeTransitionsEnabled=!Program.TestMode && SystemParameters.ClientAreaAnimation;
            var initialPalette=Palette;
            foreach(string role in ThemeRoles) {
                var color=new ThemeColorValue { Color=initialPalette.Role(role) };
                var brush=new SolidColorBrush();
                // WPF may freeze resource brushes while sealing templates.
                // A bound color source deliberately keeps these per-window
                // shared brushes mutable without touching catalog endpoints.
                BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new Binding("Color") { Source=color, Mode=BindingMode.OneWay });
                themeColors.Add(role,color); themeBrushes.Add(role,brush); themeResources.Add(ThemeResource(role),brush);
            }
            Resources.MergedDictionaries.Add(themeResources);
            auraWeight=initialPalette.Id=="aurora"?1:0;
            displayedGlassStroke.Color=initialPalette.Role("GlassStroke");
            L10n.SetLanguage(Store.Settings.Language);
            Title=L10n.T("DockTitle");
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
            ShowInTaskbar=Program.TestMode; ResizeMode=ResizeMode.NoResize;
            ShowActivated=false;
            FontFamily=new FontFamily("Segoe UI"); Foreground=Brushes.White;
            UseLayoutRounding=true; SnapsToDevicePixels=true;
            root=new Grid { Margin=new Thickness(18,8,18,16), Background=Brushes.Transparent };
            glass=new Border {
                CornerRadius=new CornerRadius(23), BorderThickness=new Thickness(1),
                BorderBrush=new SolidColorBrush(Color.FromArgb(75,255,255,255)),
                VerticalAlignment=VerticalAlignment.Bottom,
                Effect=new DropShadowEffect { Color=Colors.Black, BlurRadius=22, ShadowDepth=5, Opacity=.30 }
            };
            strip=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(9,12,9,12), VerticalAlignment=VerticalAlignment.Bottom };
            glass.Child=strip; root.Children.Add(glass);
            dragLayer=new Canvas { IsHitTestVisible=false, ClipToBounds=false };
            root.Children.Add(dragLayer);
            notice=new TextBlock { HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Top,
                Background=new SolidColorBrush(Color.FromArgb(245,30,33,44)), Foreground=Brushes.White,
                Padding=new Thickness(12,6,12,6), FontSize=11, TextTrimming=TextTrimming.CharacterEllipsis,
                Visibility=Visibility.Collapsed, IsHitTestVisible=false, MaxWidth=680 };
            root.Children.Add(notice); Content=root;
            AllowDrop=true;
            PreviewDragOver += OnDragOver;
            PreviewDragLeave += delegate { externalDragFeedback=false; glass.BorderBrush=NormalStroke(); };
            PreviewDrop += OnDrop;
            root.MouseMove += OnHoverMove;
            root.MouseLeave += delegate { if(!internalDrag && pressed==null) ResetMagnification(); };
            PreviewMouseMove += OnPointerMove;
            PreviewMouseLeftButtonUp += OnPointerReleased;
            LostMouseCapture += delegate { if(internalDrag && !endingDrag && Mouse.Captured!=this) EndInternalDrag(false); };
            Deactivated += delegate { if(internalDrag) EndInternalDrag(false); ResetMagnification(); };
            PreviewKeyDown += delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape && internalDrag) { EndInternalDrag(false); e.Handled=true; } };
            glass.MouseLeftButtonDown += delegate(object sender,MouseButtonEventArgs e) {
                if(e.OriginalSource==glass || e.OriginalSource==strip || e.OriginalSource==iconGrid) MoveDock(e);
            };
            ContextMenu=BuildDockMenu();
            KeyDown += delegate(object sender,KeyEventArgs e) {
                if((Keyboard.Modifiers & ModifierKeys.Control)!=0 && e.Key==Key.OemComma) { ShowSettings(); e.Handled=true; }
            };
            noticeTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(5) };
            noticeTimer.Tick += delegate { notice.Visibility=Visibility.Collapsed; noticeTimer.Stop(); };
            Loaded += delegate {
                double savedLeft=Store.Settings.Left, savedTop=Store.Settings.Top;
                if(!double.IsNaN(savedLeft) && !double.IsNaN(savedTop)) { Left=savedLeft; Top=savedTop; }
                ApplySettings();
                if(Store.Settings.Placement!="free") AnchorPosition(Native.WorkingArea(this));
                else if(double.IsNaN(savedLeft) || double.IsNaN(savedTop)) CenterBottom();
                else ClampPosition();
                ready=true;
                host=new DesktopHost(this,delegate { return Store.Settings.AlwaysOnTop; },!Program.TestMode,
                    delegate { return openMenus.Any(menu=>menu.IsOpen); }); host.Start();
                var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                source.AddHook(delegate(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled) {
                    if((uint)msg==Program.ShowMessage) { Dispatcher.BeginInvoke(new Action(ShowDock)); handled=true; }
                    if(msg==0x007e || msg==0x02e0) Dispatcher.BeginInvoke(new Action(delegate { ApplySettings(); ClampPosition(); }));
                    return IntPtr.Zero;
                });
                InitTray();
                if(Store.IsFirstRun) Toast(L10n.T("Welcome"));
                recycleTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(20) };
                recycleTimer.Tick += delegate {
                    var v=views.FirstOrDefault(x=>x.Item.Path=="shell:RecycleBinFolder");
                    if(v!=null) LoadIcon(v,true);
                }; recycleTimer.Start();
            };
            LocationChanged += delegate { if(ready && !moving && IsVisible) { Store.Settings.Left=Left; Store.Settings.Top=Top; } };
            Closing += delegate(object sender,System.ComponentModel.CancelEventArgs e) {
                if(helperRunning) { e.Cancel=true; return; }
                CancelThemeTransition();
                StopDesktopWatcher();
                if(internalDrag) EndInternalDrag(false);
                if(!exiting) { exiting=true; SaveQuietly(); }
            };
            Closed += delegate { StopDesktopWatcher(); StopRendering(); if(internalDrag) Mouse.Capture(null); if(host!=null) host.Dispose(); if(recycleTimer!=null) recycleTimer.Stop(); noticeTimer.Stop(); if(tray!=null) tray.Dispose(); Application.Current.Shutdown(); };
        }

        SolidColorBrush NormalStroke() { return displayedGlassStroke; }
        internal static Button PlainButton(object content,string help) {
            return PlainButton(content,help,ThemePalette.For("dusk"));
        }
        internal static Button PlainButton(object content,string help,ThemePalette palette,DockWindow owner=null) {
            var button=new Button { Content=content, Background=Brushes.Transparent, BorderThickness=new Thickness(0),
                Foreground=owner==null?ThemePalette.Brush(palette.Foreground):owner.ThemeBrush("Foreground"), Cursor=Cursors.Hand, Padding=new Thickness(0), ToolTip=help,
                FocusVisualStyle=null, HorizontalContentAlignment=HorizontalAlignment.Center, VerticalContentAlignment=VerticalAlignment.Center };
            button.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border x:Name='body' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='body' Property='Background' Value='"+(owner!=null?"{DynamicResource YarneloThemeFocusFill}":palette.Original?"#405E9BFF":ThemePalette.Text(Color.FromArgb(64,palette.Accent.R,palette.Accent.G,palette.Accent.B)))+"'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            ToolTipService.SetInitialShowDelay(button,250); ToolTipService.SetBetweenShowDelay(button,80);
            button.ToolTip=new ToolTip { Content=help, Background=owner==null?ThemePalette.Brush(palette.Hint):owner.ThemeBrush("Hint"), Foreground=owner==null?ThemePalette.Brush(palette.Foreground):owner.ThemeBrush("Foreground"),
                BorderBrush=owner==null?ThemePalette.Brush(palette.HintStroke):owner.ThemeBrush("HintStroke"), Padding=new Thickness(10,6,10,6), FontSize=12 };
            ToolTipService.SetPlacement(button,System.Windows.Controls.Primitives.PlacementMode.Top);
            ToolTipService.SetVerticalOffset(button,-9);
            System.Windows.Automation.AutomationProperties.SetName(button,help);
            return button;
        }
        Button DockButton(object content,string help) { return PlainButton(content,help,Palette,this); }

        static Color Blend(Color start,Color end,double amount) {
            if(amount>=1) return end;
            return Color.FromArgb((byte)Math.Round(start.A+(end.A-start.A)*amount),(byte)Math.Round(start.R+(end.R-start.R)*amount),(byte)Math.Round(start.G+(end.G-start.G)*amount),(byte)Math.Round(start.B+(end.B-start.B)*amount));
        }
        static bool LightSurface(Color color) { return (.2126*color.R+.7152*color.G+.0722*color.B)/255>.5; }
        static bool ForegroundRole(string role) { return role=="Foreground" || role=="Muted" || role=="Empty" || role=="Handle" || role=="Plus"; }
        void BackgroundColors(ThemePalette palette,out Color top,out Color bottom) {
            byte alpha=(byte)(Store.Settings.Opacity*255);
            top=palette.Original?Color.FromArgb(alpha,43,47,60):Color.FromArgb(alpha,palette.Surface.R,palette.Surface.G,palette.Surface.B);
            bottom=palette.Original?Color.FromArgb(alpha,21,24,34):top;
        }
        double BackgroundRadius() { return Store.Settings.CornerRadius>=100?Math.Min(Width,Height)/2:Store.Settings.CornerRadius; }
        void EndpointBackground() {
            var palette=Palette; Color top,bottom; BackgroundColors(palette,out top,out bottom);
            glass.Background=palette.Original?(Brush)new LinearGradientBrush(top,bottom,verticalDock?0:90):new SolidColorBrush(top);
            glass.CornerRadius=new CornerRadius(BackgroundRadius());
        }
        void SyncThemeEndpoint() {
            themeProgress.Cancel(); transitionBackground=null;
            var palette=Palette;
            foreach(string role in ThemeRoles) SetDisplayedThemeColor(role,palette.Role(role));
            auraWeight=palette.Id=="aurora"?1:0;
            displayedGlassStroke.Color=ThemeBrush("GlassStroke").Color;
            foreach(var view in views) {
                if(auraWeight==0 && view.HoverGlow!=null) { view.Image.Effect=null; view.HoverGlow=null; }
            }
        }
        void BeginThemeTransition() {
            themeStartColors.Clear(); themeTargetColors.Clear();
            var palette=Palette;
            foreach(string role in ThemeRoles) { themeStartColors.Add(role,ThemeBrush(role).Color); themeTargetColors.Add(role,palette.Role(role)); }
            themePolarityChanges=LightSurface(themeStartColors["Surface"])!=LightSurface(themeTargetColors["Surface"]);
            var currentGradient=glass.Background as LinearGradientBrush;
            var currentSolid=glass.Background as SolidColorBrush;
            if(currentGradient!=null) { startTop=currentGradient.GradientStops[0].Color; startBottom=currentGradient.GradientStops[currentGradient.GradientStops.Count-1].Color; }
            else { startTop=startBottom=currentSolid==null?themeStartColors["Surface"]:currentSolid.Color; }
            BackgroundColors(palette,out targetTop,out targetBottom);
            startRadius=glass.CornerRadius.TopLeft; targetRadius=BackgroundRadius();
            startAura=auraWeight; targetAura=palette.Id=="aurora"?1:0;
            transitionBackground=new LinearGradientBrush(startTop,startBottom,verticalDock?0:90);
            glass.Background=transitionBackground;
            if(startAura>0 || targetAura>0) foreach(var view in views) if(view.HoverGlow==null) {
                view.HoverGlow=new DropShadowEffect { Color=ThemeBrush("Accent").Color, BlurRadius=15, ShadowDepth=0, Opacity=0 }; view.Image.Effect=view.HoverGlow;
            }
            themeProgress.Start(); StartRendering();
        }
        internal void AdvanceThemeTransition(double dt) {
            if(!themeProgress.Active) return;
            themeProgress.Advance(dt); double amount=themeProgress.Amount;
            double foregroundAmount=ThemeProgress.ForegroundAmount(amount,themePolarityChanges);
            foreach(string role in ThemeRoles) SetDisplayedThemeColor(role,Blend(themeStartColors[role],themeTargetColors[role],ForegroundRole(role)?foregroundAmount:amount));
            if(transitionBackground!=null) { transitionBackground.GradientStops[0].Color=Blend(startTop,targetTop,amount); transitionBackground.GradientStops[1].Color=Blend(startBottom,targetBottom,amount); }
            glass.CornerRadius=new CornerRadius(startRadius+(targetRadius-startRadius)*amount);
            auraWeight=startAura+(targetAura-startAura)*amount;
            if(!themeProgress.Active) { SyncThemeEndpoint(); EndpointBackground(); }
        }
        internal void CancelThemeTransition() { themeProgress.Cancel(); transitionBackground=null; StopRendering(); }

        internal void ApplySettings() {
            ApplySettings(Native.WorkingArea(this));
        }
        internal void ApplySettings(Rect area) {
            // Direct background edits, resize and language changes finish to
            // the current model, never to an obsolete animation endpoint.
            SyncThemeEndpoint();
            var palette=Palette;
            Foreground=ThemeBrush("Foreground");
            glass.BorderBrush=NormalStroke();
            notice.Foreground=ThemeBrush("Foreground");
            notice.Background=ThemeBrush("Notice");
            bool wasVertical=verticalDock;
            verticalDock=Store.Settings.Placement=="left" || Store.Settings.Placement=="right";
            layout=DockLayout.Create(Store.Settings.Items.Count,Store.Settings.Rows,Store.Settings.IconSize,Store.Settings.Zoom,
                Store.Settings.ShowLabels,verticalDock,Math.Max(1,area.Width-20),Math.Max(1,area.Height-20));
            effectiveSize=layout.IconSize; slot=layout.CellWidth; rowPitch=layout.CellHeight;
            layoutRows=layout.Rows; layoutColumns=layout.Columns;
            double center=double.IsNaN(Left) ? area.Left+area.Width/2 : Left+Width/2;
            double centerY=double.IsNaN(Top) ? area.Top+area.Height/2 : Top+Height/2;
            double bottom=Top+Height;
            if(!ready || double.IsNaN(center) || double.IsInfinity(center)) center=area.Left+area.Width/2;
            Width=layout.Width; Height=layout.Height;
            root.Margin=verticalDock?new Thickness(12,18,12,18):new Thickness(18,8,18,16);
            strip.Orientation=verticalDock?Orientation.Vertical:Orientation.Horizontal;
            strip.Margin=verticalDock?new Thickness(12,9,12,9):new Thickness(9,12,9,12);
            strip.VerticalAlignment=verticalDock?VerticalAlignment.Stretch:VerticalAlignment.Bottom;
            strip.HorizontalAlignment=verticalDock?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
            glass.VerticalAlignment=verticalDock?VerticalAlignment.Stretch:Store.Settings.Placement=="top"?VerticalAlignment.Top:VerticalAlignment.Bottom;
            glass.HorizontalAlignment=verticalDock?(Store.Settings.Placement=="right"?HorizontalAlignment.Right:HorizontalAlignment.Left):HorizontalAlignment.Stretch;
            if(ready && Store.Settings.Placement=="free") {
                Left=center-Width/2;
                if(wasVertical && !double.IsNaN(centerY) && !double.IsInfinity(centerY)) Top=centerY-Height/2;
                else if(!double.IsNaN(bottom) && !double.IsInfinity(bottom)) Top=bottom-Height;
            }
            EndpointBackground();
            Topmost=Store.Settings.AlwaysOnTop;
            RenderIcons();
            ContextMenu=BuildDockMenu();
            if(Store.Settings.Placement!="free") AnchorPosition(area);
            else if(ready) ClampPosition(area);
            UpdateDesktopWatcher();
            if(host!=null) host.Refresh();
        }

        void UpdateDesktopWatcher() {
            if(Program.TestMode && !desktopWatcherInjected) return;
            if(!Store.Settings.AutoAddDesktopShortcuts) { StopDesktopWatcher(); return; }
            if(desktopWatcher!=null) return;
            desktopWatcher=new DesktopShortcutWatcher(new[] { Store.DesktopDirectory, Store.PublicDesktopDirectory });
            if(!Program.TestMode) {
                desktopWatcherTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
                desktopWatcherTimer.Tick+=delegate { PollDesktopShortcuts(); };
                desktopWatcherTimer.Start();
            }
        }

        void StopDesktopWatcher() {
            if(desktopWatcherTimer!=null) { desktopWatcherTimer.Stop(); desktopWatcherTimer=null; }
            if(desktopWatcher!=null) { desktopWatcher.Dispose(); desktopWatcher=null; }
            desktopWatcherInjected=false; desktopLimitNotice=false;
        }

        internal void InjectDesktopWatcher(DesktopShortcutWatcher watcher) {
            if(!Program.TestMode) throw new InvalidOperationException("Desktop watcher injection is available only in test mode.");
            StopDesktopWatcher(); desktopWatcher=watcher; desktopWatcherInjected=watcher!=null;
        }

        internal void PollDesktopShortcuts() {
            if(desktopWatcher==null || !Store.Settings.AutoAddDesktopShortcuts || helperRunning || exiting || moving || internalDrag || dragLanding || settingsWindow!=null || openMenus.Any(menu=>menu.IsOpen)) return;
            if(Store.Settings.Items.Count<Polka.Store.MaximumItems) desktopLimitNotice=false;
            bool changed=false;
            foreach(string path in desktopWatcher.Scan()) {
                bool duplicate=Store.Settings.Items.Any(item=>SameDesktopPath(item.Path,path) || SameDesktopPath(item.OriginalPath,path));
                if(duplicate) { desktopWatcher.Acknowledge(path); continue; }
                if(Store.Settings.Items.Count>=Polka.Store.MaximumItems) {
                    desktopWatcher.Acknowledge(path);
                    if(!desktopLimitNotice) { desktopLimitNotice=true; Toast(L10n.T("DockFull")); }
                    continue;
                }
                try {
                    if(!Native.IsDesktopShortcutReady(path)) continue;
                    int before=Store.Settings.Items.Count;
                    Store.Add(path);
                    desktopWatcher.Acknowledge(path);
                    changed|=Store.Settings.Items.Count>before;
                } catch(Exception ex) {
                    // An installer may still be writing or locking its shortcut.
                    // The tracker retries this exact file for a bounded interval.
                    if(!(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is System.Security.SecurityException) && !(ex is InvalidOperationException)) throw;
                }
            }
            if(changed) { ApplySettings(); SaveQuietly(); }
        }

        static bool SameDesktopPath(string first,string second) {
            if(String.IsNullOrWhiteSpace(first) || String.IsNullOrWhiteSpace(second) || first.StartsWith("shell:",StringComparison.OrdinalIgnoreCase)) return false;
            try { return String.Equals(Path.GetFullPath(first),Path.GetFullPath(second),StringComparison.OrdinalIgnoreCase); }
            catch(ArgumentException) { return false; }
            catch(NotSupportedException) { return false; }
            catch(PathTooLongException) { return false; }
        }

        void AcknowledgeRestoredShortcut(string path) {
            if(desktopWatcher!=null && !String.IsNullOrWhiteSpace(path)) desktopWatcher.Acknowledge(path);
        }

        void RenderIcons() {
            if(internalDrag) EndInternalDrag(false);
            FinishLanding();
            var previousIds=new HashSet<string>(views.Select(x=>x.Item.Id));
            strip.Children.Clear(); views.Clear(); pressed=null;
            var palette=Palette;
            var motion=Motion;
            var handle=DockButton(new TextBlock { Text="⠿", FontSize=21, Foreground=ThemeBrush("Handle") },L10n.T("MoveDock"));
            handle.Width=verticalDock?effectiveSize+8:19; handle.Height=verticalDock?19:effectiveSize+8;
            handle.VerticalAlignment=VerticalAlignment.Center; handle.HorizontalAlignment=HorizontalAlignment.Center;
            handle.PreviewMouseLeftButtonDown += delegate(object s,MouseButtonEventArgs e) { MoveDock(e); };
            handle.Click += delegate { };
            strip.Children.Add(handle);
            double iconAreaLength=0;
            if(Store.Settings.Items.Count==0) {
                var emptyText=new TextBlock { Text=L10n.T("DropShortcuts"), FontSize=12, TextAlignment=TextAlignment.Center,
                    TextWrapping=TextWrapping.Wrap, TextTrimming=TextTrimming.CharacterEllipsis,
                    Foreground=ThemeBrush("Empty") };
                var empty=DockButton(emptyText,L10n.T("AddShortcuts"));
                empty.Width=verticalDock?effectiveSize+10:Math.Max(24,Width-122);
                empty.Height=effectiveSize+8;
                emptyText.MaxWidth=empty.Width; emptyText.MaxHeight=empty.Height;
                empty.Click+=delegate { PickFiles(); }; strip.Children.Add(empty);
                iconAreaLength=verticalDock?empty.Height:empty.Width;
            }
            iconGrid=new Grid { Width=layout.GridWidth, Height=layout.GridHeight, VerticalAlignment=VerticalAlignment.Bottom };
            for(int c=0;c<layoutColumns;c++) {
                bool compactColumn=verticalDock && (Store.Settings.Placement=="right"?c==0:c==layoutColumns-1);
                iconGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(compactColumn?effectiveSize+10:slot) });
            }
            for(int r=0;r<layoutRows;r++) iconGrid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(!verticalDock && r==0?effectiveSize+8+(Store.Settings.ShowLabels?21:0):rowPitch) });
            if(Store.Settings.Items.Count>0) { strip.Children.Add(iconGrid); iconAreaLength=verticalDock?iconGrid.Height:iconGrid.Width; }
            foreach(var item in Store.Settings.Items) {
                var v=new IconView { Item=item, Scale=new ScaleTransform(1,1), Translate=new TranslateTransform(0,0), Shift=new TranslateTransform(0,0) };
                var transform=new TransformGroup(); transform.Children.Add(v.Scale); transform.Children.Add(v.Translate);
                v.Image=new Image { Width=effectiveSize, Height=effectiveSize, Stretch=Stretch.Uniform,
                    RenderTransform=transform, RenderTransformOrigin=ScaleOrigin(), IsHitTestVisible=false };
                v.ScaleResponse=motion.Response;
                if(palette.Id=="aurora") { v.HoverGlow=new DropShadowEffect { Color=palette.Accent, BlurRadius=15, ShadowDepth=0, Opacity=0 }; v.Image.Effect=v.HoverGlow; }
                RenderOptions.SetBitmapScalingMode(v.Image,BitmapScalingMode.HighQuality);
                var stack=new StackPanel { Orientation=Orientation.Vertical, HorizontalAlignment=HorizontalAlignment.Center };
                stack.Children.Add(v.Image);
                if(Store.Settings.ShowLabels) {
                    v.LabelTranslate=new TranslateTransform();
                    stack.Children.Add(new TextBlock { Text=L10n.DisplayName(item), FontSize=10, MaxWidth=effectiveSize+8,
                        TextTrimming=TextTrimming.CharacterEllipsis, TextAlignment=TextAlignment.Center, Margin=new Thickness(0,6,0,0),
                        IsHitTestVisible=false, RenderTransform=v.LabelTranslate });
                }
                v.Button=DockButton(stack,L10n.DisplayName(item)); v.Button.Width=effectiveSize+10; v.Button.Height=effectiveSize+8+(Store.Settings.ShowLabels?21:0);
                v.Button.RenderTransform=v.Shift;
                v.Button.VerticalAlignment=verticalDock?VerticalAlignment.Center:VerticalAlignment.Bottom;
                v.Button.HorizontalAlignment=verticalDock?(Store.Settings.Placement=="right"?HorizontalAlignment.Right:HorizontalAlignment.Left):HorizontalAlignment.Center;
                if(Store.Settings.Placement=="left") ToolTipService.SetPlacement(v.Button,System.Windows.Controls.Primitives.PlacementMode.Right);
                else if(Store.Settings.Placement=="right") ToolTipService.SetPlacement(v.Button,System.Windows.Controls.Primitives.PlacementMode.Left);
                else if(Store.Settings.Placement=="top") ToolTipService.SetPlacement(v.Button,System.Windows.Controls.Primitives.PlacementMode.Bottom);
                v.Button.ContextMenu=BuildItemMenu(item);
                v.Button.PreviewMouseLeftButtonDown += delegate(object sender,MouseButtonEventArgs e) {
                    if(helperRunning) return;
                    if(dragLanding) FinishLanding();
                    pressed=v; pressPoint=e.GetPosition(root); v.Pressing=true; v.ScaleResponse=Motion.PressResponse; StartRendering();
                };
                v.Button.PreviewMouseLeftButtonUp += delegate { if(!internalDrag) { v.Pressing=false; pressed=null; StartRendering(); } };
                v.Button.LostMouseCapture += delegate { if(!internalDrag) { v.Pressing=false; if(pressed==v) pressed=null; StartRendering(); } };
                v.Button.Click += delegate { if(internalDrag || endingDrag) return; v.Pressing=false; pressed=null; Bounce(v); try { Native.Launch(v.Item); } catch(Exception ex) { Toast(L10n.T("OpenFailed")+ex.Message); } };
                if(ready && !previousIds.Contains(item.Id)) {
                    v.SizeMotion.Reset(.72); v.AlphaMotion.Reset(0); v.ShiftMotion.Reset(8); v.AlphaMotion.Target=1; v.ShiftMotion.Target=0;
                    v.Scale.ScaleX=v.Scale.ScaleY=.72; v.Button.Opacity=0; v.Shift.X=8;
                    StartRendering();
                }
                SetCell(v.Button,views.Count); iconGrid.Children.Add(v.Button); views.Add(v); LoadIcon(v,false);
            }
            var divider=new Border { Width=verticalDock?effectiveSize*.62:1, Height=verticalDock?1:effectiveSize*.62,
                Margin=verticalDock?new Thickness(0,6,0,10):new Thickness(6,0,10,0),
                Background=ThemeBrush("Divider"), VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Center };
            strip.Children.Add(divider);
            double glyphSize=Math.Max(12,Math.Min(18,effectiveSize*.30));
            var plusGlyph=new System.Windows.Shapes.Path { Data=Geometry.Parse("M 0,6 L 12,6 M 6,0 L 6,12"),
                Width=glyphSize, Height=glyphSize, Stretch=Stretch.Uniform, StrokeThickness=1.7,
                StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round,
                Stroke=ThemeBrush("Plus"), IsHitTestVisible=false };
            var plus=DockButton(plusGlyph,L10n.T("AddAndSettings"));
            double available=verticalDock?Height-root.Margin.Top-root.Margin.Bottom-glass.BorderThickness.Top-glass.BorderThickness.Bottom-strip.Margin.Top-strip.Margin.Bottom:
                Width-root.Margin.Left-root.Margin.Right-glass.BorderThickness.Left-glass.BorderThickness.Right-strip.Margin.Left-strip.Margin.Right;
            double plusLength=Math.Max(26,available-(verticalDock?handle.Height:handle.Width)-iconAreaLength-
                (verticalDock?divider.Height+divider.Margin.Top+divider.Margin.Bottom:divider.Width+divider.Margin.Left+divider.Margin.Right));
            plus.Width=verticalDock?effectiveSize+8:plusLength;
            plus.Height=verticalDock?plusLength:effectiveSize+8; plus.VerticalAlignment=VerticalAlignment.Center; plus.HorizontalAlignment=HorizontalAlignment.Center;
            KeyboardNavigation.SetTabIndex(plus,views.Count+1);
            plus.Click += delegate { var menu=BuildDockMenu(); menu.PlacementTarget=plus;
                menu.Placement=Store.Settings.Placement=="left"?System.Windows.Controls.Primitives.PlacementMode.Right:
                    Store.Settings.Placement=="right"?System.Windows.Controls.Primitives.PlacementMode.Left:
                    Store.Settings.Placement=="top"?System.Windows.Controls.Primitives.PlacementMode.Bottom:System.Windows.Controls.Primitives.PlacementMode.Top;
                menu.IsOpen=true; };
            strip.Children.Add(plus);
        }

        // Reading order follows the dock: across horizontal rows or down vertical columns.
        void SetCell(UIElement element,int index) {
            KeyboardNavigation.SetTabIndex(element,index+1);
            int row,column; layout.Cell(Store.Settings.Items.Count,index,out row,out column);
            Grid.SetRow(element,row); Grid.SetColumn(element,column);
        }

        Point SlotCenter(int index) {
            var v=views[index]; Point center=BaseImageCenter(v);
            return new Point(center.X-v.Shift.X,center.Y-v.Shift.Y);
        }
        int NearestSlot(Point point) {
            int nearest=0; double best=double.MaxValue;
            for(int i=0;i<views.Count;i++) {
                Point center=SlotCenter(i);
                double dx=(point.X-center.X)/slot, dy=(point.Y-center.Y)/rowPitch;
                double distance=dx*dx+dy*dy;
                if(distance<best) { nearest=i; best=distance; }
            }
            return nearest;
        }

        async void LoadIcon(IconView view,bool refresh) {
            ImageSource icon;
            if(!refresh && iconCache.TryGetValue(view.Item.Path,out icon)) { view.Image.Source=icon; return; }
            string path=view.Item.Path;
            try {
                icon=await Task.Run(delegate { return Native.GetIcon(path); });
                if(icon!=null) { iconCache[path]=icon; view.Image.Source=icon; }
            } catch { }
        }

        void OnHoverMove(object sender,MouseEventArgs e) {
            if(internalDrag || dragLanding || e.LeftButton==MouseButtonState.Pressed) return;
            var motion=Motion;
            Point pointer=e.GetPosition(root);
            foreach(var v in views) {
                Point center=BaseImageCenter(v);
                double dx=(pointer.X-center.X)/slot, dy=(pointer.Y-center.Y)/rowPitch;
                double influence=Math.Exp(-(dx*dx+dy*dy)*1.45);
                double scale=1+(Store.Settings.Zoom-1)*influence;
                Panel.SetZIndex(v.Button,(int)(influence*100));
                v.HoverScale=scale; v.HoverLift=-motion.HoverLift*influence; v.ScaleResponse=motion.Response;
            }
            StartRendering();
        }
        void Animate(IconView v,double scale,double lift,int ms) {
            v.HoverScale=scale; v.HoverLift=lift; v.ScaleResponse=Motion.Response; StartRendering();
        }
        void ResetMagnification() { var response=Motion.Response; foreach(var v in views) { v.Pressing=false; v.HoverScale=1; v.HoverLift=0; v.ScaleResponse=response; } StartRendering(); }
        void Bounce(IconView v) {
            v.ClickStarted=animationClock.Elapsed.TotalSeconds; v.Pressing=false; v.ScaleResponse=Motion.ClickResponse; StartRendering();
        }

        void StartRendering() {
            if(rendering || exiting) return;
            rendering=true; previousFrame=TimeSpan.Zero;
            CompositionTarget.Rendering+=OnAnimationFrame;
        }
        void StopRendering() {
            if(!rendering) return;
            CompositionTarget.Rendering-=OnAnimationFrame; rendering=false; previousFrame=TimeSpan.Zero;
        }
        void OnAnimationFrame(object sender,EventArgs e) {
            var frame=e as RenderingEventArgs;
            if(frame==null) return;
            if(previousFrame==frame.RenderingTime) return;
            double dt=previousFrame==TimeSpan.Zero?1.0/60:(frame.RenderingTime-previousFrame).TotalSeconds;
            previousFrame=frame.RenderingTime;
            AdvanceAnimations(Math.Max(.001,Math.Min(.05,dt)));
        }
        internal void AdvanceAnimations(double dt) {
            AdvanceThemeTransition(dt);
            var motion=Motion;
            bool active=internalDrag || dragLanding || themeProgress.Active;
            double accentActivity=internalDrag || dragLanding?.8:0;
            double now=animationClock.Elapsed.TotalSeconds;
            foreach(var v in views) {
                double pop=0, hop=0;
                if(v.ClickStarted>=0) {
                    double elapsed=now-v.ClickStarted;
                    if(elapsed<motion.ClickDuration) { pop=motion.ClickPop*Math.Sin(Math.PI*Math.Min(1,elapsed/motion.PopDuration)); hop=-motion.ClickHop*Math.Sin(Math.PI*Math.Min(1,elapsed/motion.ClickDuration)); active=true; }
                    else v.ClickStarted=-1;
                }
                v.SizeMotion.Target=Math.Min(1.85,v.HoverScale*(v.Pressing?motion.PressScale:1)+pop);
                v.LiftMotion.Target=v.HoverLift+(v.Pressing?1:0)+hop;
                v.SizeMotion.Step(dt,v.ScaleResponse); v.LiftMotion.Step(dt,motion.LiftResponse); v.ShiftMotion.Step(dt,motion.ShiftResponse); v.ShiftYMotion.Step(dt,motion.ShiftResponse); v.AlphaMotion.Step(dt,motion.AlphaResponse);
                bool still=!v.SizeMotion.IsSettled(.001) || !v.LiftMotion.IsSettled(.01) || !v.ShiftMotion.IsSettled(.01) || !v.ShiftYMotion.IsSettled(.01) || !v.AlphaMotion.IsSettled(.001);
                if(!still) { v.SizeMotion.SnapToTarget(); v.LiftMotion.SnapToTarget(); v.ShiftMotion.SnapToTarget(); v.ShiftYMotion.SnapToTarget(); v.AlphaMotion.SnapToTarget(); }
                active|=still;
                v.Scale.ScaleX=v.Scale.ScaleY=v.SizeMotion.Value;
                accentActivity=Math.Max(accentActivity,v.Pressing?1:Math.Max(0,v.SizeMotion.Value-1)/Math.Max(.01,Store.Settings.Zoom-1));
                if(v.HoverGlow!=null) { v.HoverGlow.Color=ThemeBrush("Accent").Color; v.HoverGlow.Opacity=auraWeight*Math.Max(0,Math.Min(.34,(v.SizeMotion.Value-1)*.65)); }
                v.Translate.X=verticalDock?(Store.Settings.Placement=="left"?-v.LiftMotion.Value:v.LiftMotion.Value):0;
                v.Translate.Y=verticalDock?0:Store.Settings.Placement=="top"?-v.LiftMotion.Value:v.LiftMotion.Value;
                if(v.LabelTranslate!=null) {
                    v.LabelTranslate.X=v.Translate.X;
                    v.LabelTranslate.Y=Store.Settings.Placement=="top"?Math.Max(0,effectiveSize*(v.SizeMotion.Value-1))+v.Translate.Y:
                        verticalDock?Math.Max(0,effectiveSize*(v.SizeMotion.Value-1)/2):0;
                }
                v.Shift.X=v.ShiftMotion.Value; v.Shift.Y=v.ShiftYMotion.Value; v.Button.Opacity=v.AlphaMotion.Value;
            }
            if(!externalDragFeedback) {
                var edge=ThemeBrush("GlassStroke").Color;
                byte alpha=(byte)Math.Min(255,edge.A+60*auraWeight*Math.Min(1,accentActivity));
                displayedGlassStroke.Color=Color.FromArgb(alpha,edge.R,edge.G,edge.B);
                glass.BorderBrush=displayedGlassStroke;
            }
            if(dragView!=null && dragGhost!=null) {
                ghostX.Step(dt,motion.DragResponse); ghostY.Step(dt,motion.DragResponse); ghostScale.Step(dt,motion.DragScaleResponse); ghostAlpha.Step(dt,motion.DragAlphaResponse); placeholderX.Step(dt,motion.PlaceholderResponse); placeholderY.Step(dt,motion.PlaceholderResponse);
                ghostAngle.Target=internalDrag?Math.Max(-motion.DragTilt,Math.Min(motion.DragTilt,ghostX.Velocity*.012)):0; ghostAngle.Step(dt,24);
                Canvas.SetLeft(dragGhost,ghostX.Value-effectiveSize/2); Canvas.SetTop(dragGhost,ghostY.Value-effectiveSize/2);
                ghostScaleTransform.ScaleX=ghostScaleTransform.ScaleY=ghostScale.Value; ghostRotation.Angle=ghostAngle.Value; dragGhost.Opacity=ghostAlpha.Value;
                if(dragPlaceholder!=null) { Canvas.SetLeft(dragPlaceholder,placeholderX.Value-(slot-5)/2); Canvas.SetTop(dragPlaceholder,placeholderY.Value-effectiveSize/2-2); }
                if(dragLanding && Math.Abs(ghostX.Value-ghostX.Target)<.7 && Math.Abs(ghostY.Value-ghostY.Target)<.7 && Math.Abs(ghostX.Velocity)<12 && Math.Abs(ghostY.Velocity)<12) {
                    dragView.AlphaMotion.Target=1; ghostAlpha.Target=0;
                    if(ghostAlpha.Value<.015 && dragView.AlphaMotion.Value>.985) FinishLanding();
                }
            }
            if(!active) StopRendering();
        }

        Point ScaleOrigin() {
            if(Store.Settings.Placement=="left") return new Point(0,.5);
            if(Store.Settings.Placement=="right") return new Point(1,.5);
            return new Point(.5,Store.Settings.Placement=="top"?0:1);
        }
        Point BaseImageCenter(IconView view) {
            var point=view.Image.TranslatePoint(new Point(effectiveSize/2,effectiveSize/2),root);
            Point origin=view.Image.RenderTransformOrigin;
            point.X-=view.Translate.X+effectiveSize*(.5-origin.X)*(view.Scale.ScaleX-1);
            point.Y-=view.Translate.Y+effectiveSize*(.5-origin.Y)*(view.Scale.ScaleY-1);
            return point;
        }
        void OnPointerMove(object sender,MouseEventArgs e) {
            var p=e.GetPosition(root);
            if(internalDrag) { UpdateInternalDrag(p); e.Handled=true; return; }
            if(pressed==null || e.LeftButton!=MouseButtonState.Pressed || helperRunning) return;
            if(Math.Abs(p.X-pressPoint.X)<SystemParameters.MinimumHorizontalDragDistance+3 && Math.Abs(p.Y-pressPoint.Y)<SystemParameters.MinimumVerticalDragDistance+3) return;
            BeginInternalDrag(pressed,p); e.Handled=true;
        }
        void OnPointerReleased(object sender,MouseButtonEventArgs e) {
            if(!internalDrag) return;
            UpdateInternalDrag(e.GetPosition(root)); EndInternalDrag(true); e.Handled=true;
        }
        internal void BeginInternalDrag(IconView view,Point pointer) {
            if(view==null || helperRunning || !views.Contains(view)) return;
            if(dragLanding) FinishLanding();
            dragView=view; dragSourceIndex=views.IndexOf(view); dragTargetIndex=dragSourceIndex;
            internalDrag=true; dragValid=true; pressed=null;
            foreach(var v in views) { v.Pressing=false; v.ClickStarted=-1; v.HoverScale=1; v.HoverLift=0; if(v.Button.ToolTip is ToolTip) ((ToolTip)v.Button.ToolTip).IsOpen=false; }
            Point center=view.Image.TranslatePoint(new Point(effectiveSize/2,effectiveSize/2),root);
            dragGrabOffset=new Point(pressPoint.X-center.X,pressPoint.Y-center.Y);
            ghostX.Reset(center.X); ghostY.Reset(center.Y); ghostScale.Reset(view.Scale.ScaleX); ghostAlpha.Reset(1); ghostAngle.Reset(0);
            Point target=SlotCenter(dragTargetIndex);
            placeholderX.Reset(target.X); placeholderY.Reset(target.Y);
            dragPlaceholder=new Border { Width=slot-5, Height=effectiveSize+5, CornerRadius=new CornerRadius(Math.Max(7,effectiveSize*.18)),
                Background=ThemePalette.Brush(Palette.Original?Color.FromArgb(18,180,210,255):Color.FromArgb(18,Palette.Accent.R,Palette.Accent.G,Palette.Accent.B)), BorderBrush=ThemePalette.Brush(Palette.Original?Color.FromArgb(44,180,210,255):Color.FromArgb(70,Palette.Accent.R,Palette.Accent.G,Palette.Accent.B)), BorderThickness=new Thickness(1) };
            Canvas.SetLeft(dragPlaceholder,placeholderX.Value-(slot-5)/2); Canvas.SetTop(dragPlaceholder,placeholderY.Value-effectiveSize/2-2);
            dragLayer.Children.Add(dragPlaceholder);
            ghostScaleTransform=new ScaleTransform(ghostScale.Value,ghostScale.Value); ghostRotation=new RotateTransform();
            var transform=new TransformGroup(); transform.Children.Add(ghostScaleTransform); transform.Children.Add(ghostRotation);
            dragGhost=new Image { Source=view.Image.Source, Width=effectiveSize, Height=effectiveSize, Stretch=Stretch.Uniform,
                RenderTransformOrigin=new Point(.5,.5), RenderTransform=transform,
                Effect=new DropShadowEffect { BlurRadius=15, ShadowDepth=6, Opacity=.4, Color=Colors.Black } };
            RenderOptions.SetBitmapScalingMode(dragGhost,BitmapScalingMode.HighQuality);
            Canvas.SetLeft(dragGhost,center.X-effectiveSize/2); Canvas.SetTop(dragGhost,center.Y-effectiveSize/2);
            dragLayer.Children.Add(dragGhost);
            view.AlphaMotion.Reset(0); view.Button.Opacity=0;
            endingDrag=true;
            bool captured=CaptureMouse();
            endingDrag=false;
            if(!captured) { EndInternalDrag(false); return; }
            Cursor=Cursors.SizeAll;
            UpdateInternalDrag(pointer); StartRendering();
        }
        internal void UpdateInternalDrag(Point pointer) {
            if(!internalDrag || dragView==null) return;
            var motion=Motion;
            ghostX.Target=pointer.X-dragGrabOffset.X; ghostY.Target=pointer.Y-dragGrabOffset.Y-motion.DragLift; ghostScale.Target=motion.DragScale;
            var allowed=new Rect(-18,-28,root.ActualWidth+36,root.ActualHeight+56);
            dragValid=allowed.Contains(pointer); ghostAlpha.Target=dragValid?1:.55;
            dragTargetIndex=dragValid?NearestSlot(new Point(ghostX.Target,ghostY.Target+motion.DragLift)):dragSourceIndex;
            Point target=SlotCenter(dragTargetIndex);
            placeholderX.Target=target.X; placeholderY.Target=target.Y;
            foreach(var v in views) {
                int i=views.IndexOf(v), destination=i;
                if(v!=dragView && dragSourceIndex<dragTargetIndex && i>dragSourceIndex && i<=dragTargetIndex) destination=i-1;
                if(v!=dragView && dragSourceIndex>dragTargetIndex && i>=dragTargetIndex && i<dragSourceIndex) destination=i+1;
                Point from=SlotCenter(i), to=SlotCenter(destination);
                v.ShiftMotion.Target=to.X-from.X; v.ShiftYMotion.Target=to.Y-from.Y;
            }
            StartRendering();
        }
        internal void EndInternalDrag(bool commit) {
            if(!internalDrag || dragView==null) return;
            endingDrag=true;
            bool changed=commit && dragValid && dragSourceIndex!=dragTargetIndex;
            if(changed) {
                var previous=new Dictionary<IconView,Point>();
                foreach(var v in views) previous[v]=v.Button.TranslatePoint(new Point(0,0),root);
                var item=dragView.Item;
                Store.Settings.Items.Remove(item); Store.Settings.Items.Insert(dragTargetIndex,item);
                views.Remove(dragView); views.Insert(dragTargetIndex,dragView);
                for(int i=0;i<views.Count;i++) SetCell(views[i].Button,i);
                iconGrid.UpdateLayout();
                foreach(var v in views) {
                    Point current=v.Button.TranslatePoint(new Point(0,0),root);
                    double offset=v==dragView?0:previous[v].X-(current.X-v.Shift.X);
                    double offsetY=v==dragView?0:previous[v].Y-(current.Y-v.Shift.Y);
                    double velocity=v.ShiftMotion.Velocity, velocityY=v.ShiftYMotion.Velocity;
                    v.ShiftMotion.Reset(offset); v.ShiftMotion.Velocity=velocity; v.ShiftMotion.Target=0; v.Shift.X=offset;
                    v.ShiftYMotion.Reset(offsetY); v.ShiftYMotion.Velocity=velocityY; v.ShiftYMotion.Target=0; v.Shift.Y=offsetY;
                }
                SaveQuietly();
            } else foreach(var v in views) { v.ShiftMotion.Target=0; v.ShiftYMotion.Target=0; }
            dragView.ShiftMotion.Reset(0); dragView.ShiftYMotion.Reset(0); dragView.Shift.X=0; dragView.Shift.Y=0;
            Point landing=BaseImageCenter(dragView);
            ghostX.Target=landing.X; ghostY.Target=landing.Y; ghostScale.Target=1; ghostAngle.Target=0; ghostAlpha.Target=1;
            if(dragPlaceholder!=null) { dragLayer.Children.Remove(dragPlaceholder); dragPlaceholder=null; }
            internalDrag=false; dragLanding=true;
            if(IsMouseCaptured) ReleaseMouseCapture();
            Cursor=null; endingDrag=false; StartRendering();
        }
        void FinishLanding() {
            if(dragView!=null) { dragView.AlphaMotion.Reset(1); dragView.Button.Opacity=1; }
            dragLayer.Children.Clear(); dragGhost=null; dragPlaceholder=null; dragView=null; dragLanding=false;
        }

        void OnDragOver(object sender,DragEventArgs e) {
            externalDragFeedback=true;
            bool own=e.Data.GetDataPresent(ItemFormat);
            bool files=e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects=own ? DragDropEffects.Move : files ? ((e.AllowedEffects & DragDropEffects.Copy)!=0?DragDropEffects.Copy:DragDropEffects.Link) : DragDropEffects.None;
            if(files && Store.Settings.Items.Count>=30) e.Effects=DragDropEffects.None;
            glass.BorderBrush=e.Effects==DragDropEffects.None ? NormalStroke() : ThemePalette.Brush(Palette.Original?Color.FromRgb(121,177,255):Palette.Accent);
            e.Handled=true;
        }
        void OnDrop(object sender,DragEventArgs e) {
            externalDragFeedback=false;
            glass.BorderBrush=NormalStroke(); e.Handled=true;
            if(e.Data.GetDataPresent(ItemFormat)) {
                string id=e.Data.GetData(ItemFormat) as string;
                var item=Store.Settings.Items.FirstOrDefault(x=>x.Id==id); if(item==null) return;
                int index=NearestSlot(e.GetPosition(root));
                Store.Settings.Items.Remove(item);
                index=Math.Max(0,Math.Min(index,Store.Settings.Items.Count));
                Store.Settings.Items.Insert(index,item); SaveQuietly(); ApplySettings(); return;
            }
            if(e.Data.GetDataPresent(DataFormats.FileDrop)) AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));
        }
        internal void AddPaths(IEnumerable<string> paths) {
            if(helperRunning) return;
            int pendingBefore=Store.Settings.Items.Count(x=>x.PendingPublicMove);
            int added=0, before=Store.Settings.Items.Count;
            var errors=new List<string>();
            foreach(var path in paths) {
                try { Store.Add(path); if(!String.IsNullOrEmpty(Store.LastNotice)) errors.Add(Store.LastNotice); }
                catch(Exception ex) { errors.Add(ex.Message); }
            }
            added=Store.Settings.Items.Count-before; ApplySettings(); SaveQuietly();
            Toast(errors.Count>0 ? String.Join(" · ",errors.Distinct().Take(2)) : added>0 ? L10n.T("AddedCount",added,Store.Settings.Items.Count) : L10n.T("AlreadyAdded"));
            if(Store.Settings.MoveDesktopShortcuts && Store.Settings.Items.Count(x=>x.PendingPublicMove)>pendingBefore) MovePublicShortcuts();
        }

        MenuItem Menu(string text,Action action) { var m=new MenuItem { Header=text, Foreground=ThemeBrush("Foreground"), Background=Brushes.Transparent }; m.Click+=delegate { action(); }; return m; }
        Separator MenuDivider() { return new Separator { Style=(Style)MenuTheme[typeof(Separator)] }; }
        ContextMenu NewMenu() {
            var palette=Palette;
            var menu=new ContextMenu { FontFamily=FontFamily, FontSize=13, Background=ThemeBrush("Menu"), Foreground=ThemeBrush("Foreground"), BorderBrush=ThemeBrush("MenuStroke"), BorderThickness=new Thickness(1), Padding=new Thickness(5), SnapsToDevicePixels=true };
            menu.Resources.MergedDictionaries.Add(themeResources);
            menu.Template=(ControlTemplate)MenuTheme["MenuSurface"];
            menu.Resources[typeof(MenuItem)]=MenuTheme[typeof(MenuItem)];
            menu.Resources[typeof(Separator)]=MenuTheme[typeof(Separator)];
            menu.Opened+=delegate { openMenus.Add(menu); if(host!=null) host.Refresh(); };
            menu.Closed+=delegate { openMenus.Remove(menu); if(host!=null) host.Refresh(); };
            return menu;
        }
        ContextMenu BuildDockMenu() {
            var menu=NewMenu();
            menu.Items.Add(Menu(L10n.T("AddFiles"),PickFiles));
            menu.Items.Add(Menu(L10n.T("AddFolder"),PickFolder));
            menu.Items.Add(Menu(L10n.T("Computer"),delegate { AddPaths(new[]{"shell:MyComputerFolder"}); }));
            menu.Items.Add(Menu(L10n.T("Trash"),delegate { AddPaths(new[]{"shell:RecycleBinFolder"}); }));
            int pending=Store.Settings.Items.Count(x=>x.PendingPublicMove);
            if(pending>0) menu.Items.Add(Menu(L10n.T("MoveSharedCount",pending),delegate { MovePublicShortcuts(); }));
            menu.Items.Add(MenuDivider());
            menu.Items.Add(Menu(L10n.T("SettingsMenu"),ShowSettings));
            menu.Items.Add(BuildLanguageMenu());
            menu.Items.Add(BuildPlacementMenu());
            menu.Items.Add(MenuDivider());
            menu.Items.Add(Menu(L10n.T("Exit"),Exit)); return menu;
        }
        ContextMenu BuildItemMenu(DockItem item) {
            var menu=NewMenu();
            menu.Items.Add(Menu(L10n.T("Open"),delegate { try { Native.Launch(item); } catch(Exception ex) { Toast(ex.Message); } }));
            if(!item.Path.StartsWith("shell:",StringComparison.OrdinalIgnoreCase)) menu.Items.Add(Menu(L10n.T("Reveal"),delegate { try { Native.Reveal(item); } catch(Exception ex) { Toast(ex.Message); } }));
            menu.Items.Add(Menu(L10n.T("Rename"),delegate {
                string name=AskName(L10n.DisplayName(item)); if(!String.IsNullOrWhiteSpace(name)) { item.Name=name.Trim(); item.UserNamed=true; SaveQuietly(); ApplySettings(); }
            }));
            menu.Items.Add(MenuDivider());
            menu.Items.Add(Menu(item.ManagedShortcut ? L10n.T("RemoveRestore") : L10n.T("Remove"),delegate { RemoveItem(item); })); return menu;
        }
        async void RemoveItem(DockItem item) {
            try {
                if(Store.NeedsPublicRestore(item)) {
                    if(!await RunPublicHelper("--public-restore",item.Id)) return;
                    item=Store.Settings.Items.FirstOrDefault(x=>x.Id==item.Id);
                    if(item==null) return;
                    AcknowledgeRestoredShortcut(item.LastRestoredPath);
                }
                Store.Remove(item); ApplySettings(); Toast(String.IsNullOrEmpty(Store.LastNotice)?L10n.T("Removed"):Store.LastNotice);
            } catch(Exception ex) { Toast(ex.Message); }
            finally { AcknowledgeRestoredShortcut(Store.LastRestoredPath); }
        }
        async void MovePublicShortcuts() {
            if(await RunPublicHelper("--public-move",null)) Toast(L10n.T("SharedMoved"));
        }
        async Task<bool> RunPublicHelper(string operation,string itemId) {
            if(helperRunning) return false;
            try { Store.Settings.Left=Left; Store.Settings.Top=Top; Store.Save(); }
            catch(Exception ex) { Toast(L10n.T("SaveDockFailed")+ex.Message); return false; }
            helperRunning=true; IsEnabled=false;
            if(settingsWindow!=null) settingsWindow.IsEnabled=false;
            string data=Store.DataDirectory;
            try {
                int code=await Task.Run(delegate {
                    var info=new System.Diagnostics.ProcessStartInfo(Native.ExePath,operation+(itemId==null?"":" "+itemId)+" --data-dir \""+data+"\"") {
                        UseShellExecute=true, Verb="runas", WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden
                    };
                    using(var process=System.Diagnostics.Process.Start(info)) { process.WaitForExit(); return process.ExitCode; }
                });
                Store=new Store(data); ApplySettings();
                if(code!=0) { Toast(L10n.T("Incomplete")); return false; }
                return true;
            } catch(System.ComponentModel.Win32Exception ex) {
                Toast(ex.NativeErrorCode==1223 ? L10n.T("Cancelled") : ex.Message); return false;
            } finally {
                helperRunning=false;
                IsEnabled=true; if(settingsWindow!=null) settingsWindow.IsEnabled=true;
            }
        }
        string AskName(string current) {
            var dialog=new Window { Title=L10n.T("ShortcutName"), Width=380, Height=165, ResizeMode=ResizeMode.NoResize, WindowStartupLocation=WindowStartupLocation.CenterOwner, Owner=this, Background=ThemePalette.Brush(Palette.Original?Color.FromRgb(30,34,45):Palette.Surface), Foreground=ThemePalette.Brush(Palette.Foreground), ShowInTaskbar=false };
            var layout=new StackPanel { Margin=new Thickness(20) }; var box=new TextBox { Text=current, FontSize=15, Margin=new Thickness(0,0,0,15), MaxLength=80 };
            var ok=new Button { Content=L10n.T("Save"), Height=31, IsDefault=true }; ok.Click+=delegate { dialog.DialogResult=true; };
            layout.Children.Add(box); layout.Children.Add(ok); dialog.Content=layout; dialog.Loaded+=delegate { box.Focus(); box.SelectAll(); };
            return dialog.ShowDialog()==true?box.Text:null;
        }
        internal void PickFiles() {
            if(helperRunning) return;
            var dialog=new Microsoft.Win32.OpenFileDialog { Title=L10n.T("AddToYarnelo"), Multiselect=true, CheckFileExists=true,
                Filter=L10n.T("FileFilter"), DereferenceLinks=false };
            if(dialog.ShowDialog(this)==true) AddPaths(dialog.FileNames);
        }
        void PickFolder() {
            if(helperRunning) return;
            using(var dialog=new Forms.FolderBrowserDialog { Description=L10n.T("FolderPrompt"), ShowNewFolderButton=false }) {
                if(dialog.ShowDialog()==Forms.DialogResult.OK) AddPaths(new[]{dialog.SelectedPath});
            }
        }
        void MoveDock(MouseButtonEventArgs e) {
            if(helperRunning) return;
            if(e.LeftButton!=MouseButtonState.Pressed) return;
            moving=true; try { DragMove(); } catch(InvalidOperationException) {} finally { moving=false; }
            Store.Settings.Placement="free";
            ApplySettings();
            if(settingsWindow!=null) settingsWindow.RefreshLanguage();
            ClampPosition(); SaveQuietly(); e.Handled=true;
        }
        internal void CenterBottom() {
            SetPlacement("bottom");
        }
        internal void SetPlacement(string placement) {
            if(helperRunning) return;
            if(placement!="free" && placement!="bottom" && placement!="top" && placement!="left" && placement!="right") placement="free";
            Store.Settings.Placement=placement;
            ApplySettings(); SaveQuietly();
            if(settingsWindow!=null) settingsWindow.RefreshLanguage();
        }
        void AnchorPosition(Rect area) {
            double gapX=Math.Min(10,Math.Max(0,(area.Width-Width)/2)), gapY=Math.Min(10,Math.Max(0,(area.Height-Height)/2));
            if(Store.Settings.Placement=="left" || Store.Settings.Placement=="right") {
                Left=Store.Settings.Placement=="left"?area.Left+gapX:area.Right-Width-gapX;
                Top=area.Top+(area.Height-Height)/2;
            } else {
                Left=area.Left+(area.Width-Width)/2;
                Top=Store.Settings.Placement=="top"?area.Top+gapY:area.Bottom-Height-gapY;
            }
            ClampPosition(area);
        }
        void ClampPosition() {
            ClampPosition(Native.WorkingArea(this));
        }
        void ClampPosition(Rect area) {
            Left=Math.Max(area.Left,Math.Min(area.Right-Width,Left)); Top=Math.Max(area.Top,Math.Min(area.Bottom-Height,Top));
        }
        internal void SaveQuietly() {
            if(helperRunning) return;
            if(ready) { Store.Settings.Left=Left; Store.Settings.Top=Top; }
            try { Store.Save(); } catch(Exception ex) { Toast(L10n.T("SaveSettingsFailed")+ex.Message); }
        }
        internal void Toast(string text) { notice.Text=text; notice.Visibility=Visibility.Visible; noticeTimer.Stop(); noticeTimer.Start(); }
        internal void ShowSettings() {
            if(helperRunning) return;
            if(settingsWindow!=null) { settingsWindow.Activate(); return; }
            settingsWindow=new SettingsWindow(this); settingsWindow.Owner=this;
            settingsWindow.Closed+=delegate { settingsWindow=null; SaveQuietly(); };
            settingsWindow.Show(); settingsWindow.Activate();
        }
        internal void SetLanguage(string language) {
            if(helperRunning) return;
            string normalized=L10n.Normalize(language);
            string previous=Store.Settings.Language;
            Store.Settings.Language=normalized;
            // Save first so the interface never claims a preference that was
            // lost on disk. Restore the current checkmarks on a failed save.
            try { Store.Save(); }
            catch(Exception ex) {
                Store.Settings.Language=previous;
                L10n.SetLanguage(previous);
                RefreshLanguageUi();
                Toast(L10n.T("SaveSettingsFailed")+ex.Message);
                return;
            }
            L10n.SetLanguage(normalized);
            RefreshLanguageUi();
        }
        internal void SetTheme(string theme) {
            if(helperRunning) return;
            if(ThemeCatalog.Normalize(theme)==ThemeCatalog.Normalize(Store.Settings.Theme)) return;
            if(glass.Background==null) ApplySettings();
            try { Store.SetTheme(theme); }
            catch(Exception ex) { if(settingsWindow!=null) settingsWindow.ApplyThemeSelection(); Toast(L10n.T("SaveSettingsFailed")+ex.Message); return; }
            if(!ThemeTransitionsEnabled) { RefreshLanguageUi(); return; }
            foreach(var menu in openMenus.ToArray()) menu.IsOpen=false;
            BeginThemeTransition();
            if(settingsWindow!=null) settingsWindow.ApplyThemeSelection();
        }
        void RefreshLanguageUi() {
            // An open popup retains old strings, so close it before replacing menus.
            foreach(var menu in openMenus.ToArray()) menu.IsOpen=false;
            notice.Visibility=Visibility.Collapsed;
            Title=L10n.T("DockTitle");
            ApplySettings();
            if(tray!=null) RefreshTrayMenu();
            if(settingsWindow!=null) settingsWindow.RefreshLanguage();
        }
        MenuItem BuildLanguageMenu() {
            var languageMenu=new MenuItem { Header=L10n.T("Language"), Foreground=ThemeBrush("Foreground"), Background=Brushes.Transparent };
            foreach(string code in new[]{"en","ru"}) {
                string choice=code;
                var item=Menu(code=="en"?"English":"Русский",delegate { SetLanguage(choice); });
                item.IsCheckable=true; item.IsChecked=Store.Settings.Language==code;
                languageMenu.Items.Add(item);
            }
            return languageMenu;
        }
        MenuItem BuildPlacementMenu() {
            var placementMenu=new MenuItem { Header=L10n.T("Placement"), Foreground=ThemeBrush("Foreground"), Background=Brushes.Transparent };
            foreach(string placement in new[]{"bottom","top","left","right","free"}) {
                string choice=placement;
                string key="Placement"+Char.ToUpperInvariant(choice[0])+choice.Substring(1);
                var item=Menu(L10n.T(key),delegate { SetPlacement(choice); });
                item.IsCheckable=true; item.IsChecked=Store.Settings.Placement==choice;
                placementMenu.Items.Add(item);
            }
            return placementMenu;
        }
        void ShowDock() { Show(); WindowState=WindowState.Normal; if(host==null || host.ShowRequested()) Activate(); }
        void InitTray() {
            tray=new Forms.NotifyIcon { Text=L10n.T("DockTitle"), Visible=true };
            try { tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Native.ExePath); } catch { tray.Icon=System.Drawing.SystemIcons.Application; }
            RefreshTrayMenu();
            tray.DoubleClick+=delegate { Dispatcher.BeginInvoke(new Action(ShowDock)); };
        }
        void RefreshTrayMenu() {
            var previous=tray.ContextMenuStrip;
            var menu=new Forms.ContextMenuStrip();
            menu.Items.Add(L10n.T("ShowDock"),null,delegate { Dispatcher.BeginInvoke(new Action(ShowDock)); });
            menu.Items.Add(L10n.T("Settings"),null,delegate { Dispatcher.BeginInvoke(new Action(ShowSettings)); });
            var languageMenu=new Forms.ToolStripMenuItem(L10n.T("Language"));
            foreach(string code in new[]{"en","ru"}) {
                string choice=code;
                var item=new Forms.ToolStripMenuItem(code=="en"?"English":"Русский") { Checked=Store.Settings.Language==code };
                item.Click+=delegate { Dispatcher.BeginInvoke(new Action(delegate { SetLanguage(choice); })); };
                languageMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(languageMenu);
            menu.Items.Add(L10n.T("Exit"),null,delegate { Dispatcher.BeginInvoke(new Action(Exit)); });
            tray.Text=L10n.T("DockTitle"); tray.ContextMenuStrip=menu;
            if(previous!=null) previous.Dispose();
        }
        void Exit() { if(helperRunning) return; exiting=true; SaveQuietly(); if(settingsWindow!=null) settingsWindow.Close(); Close(); }
    }
}
