using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Polka {
    internal class SettingsWindow : Window {
        const double DesignWidth=900, DesignHeight=760;
        readonly DockWindow dock;
        SolidColorBrush muted;
        ThemePalette palette;
        readonly Dictionary<string,Button> themeChoices=new Dictionary<string,Button>();
        readonly Dictionary<string,Slider> appearanceSliders=new Dictionary<string,Slider>();
        bool refreshingThemeValues;
        public SettingsWindow(DockWindow parent) {
            dock=parent;
            Title=L10n.T("SettingsTitle"); Width=DesignWidth; Height=DesignHeight; ResizeMode=ResizeMode.NoResize;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
            ShowInTaskbar=false; WindowStartupLocation=WindowStartupLocation.CenterOwner;
            FontFamily=new FontFamily("Segoe UI"); Foreground=Brushes.White;
            RefreshLanguage();
            KeyDown+=delegate(object s,KeyEventArgs e) { if(e.Key==Key.Escape) Close(); };
            Loaded+=delegate {
                var area=Native.WorkingArea(dock);
                FitToWorkingArea(area);
                Left=area.Left+(area.Width-Width)/2;
                Top=area.Top+(area.Height-Height)/2;
            };
        }
        // Scale the complete two-column sheet together on small or high-DPI
        // displays, keeping every setting reachable without a scroll region.
        internal void FitToWorkingArea(Rect area) {
            double scale=Math.Min(1,Math.Min(Math.Max(1,area.Width-24)/DesignWidth,Math.Max(1,area.Height-24)/DesignHeight));
            Width=DesignWidth*scale; Height=DesignHeight*scale;
        }
        internal void RefreshLanguage() {
            palette=dock.Palette; muted=dock.ThemeBrush("Muted"); Foreground=dock.ThemeBrush("Foreground");
            themeChoices.Clear(); appearanceSliders.Clear();
            if(!Resources.MergedDictionaries.Contains(dock.ThemeResources)) Resources.MergedDictionaries.Add(dock.ThemeResources);
            var hintStyle=new Style(typeof(ToolTip));
            hintStyle.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension(DockWindow.ThemeResource("Hint"))));
            hintStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension(DockWindow.ThemeResource("Foreground"))));
            hintStyle.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension(DockWindow.ThemeResource("HintStroke"))));
            Resources[typeof(ToolTip)]=hintStyle;
            Title=L10n.T("SettingsTitle");
            var frame=new Border { Width=DesignWidth, Height=DesignHeight, CornerRadius=new CornerRadius(20), Padding=new Thickness(26,20,26,18),
                Background=dock.ThemeBrush("Surface"), BorderBrush=dock.ThemeBrush("Stroke"), BorderThickness=new Thickness(1) };
            var grid=new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            var header=new Grid { Margin=new Thickness(0,0,0,20) };
            var heading=new StackPanel();
            heading.Children.Add(new TextBlock { Text="Yarnelo", FontSize=27, FontWeight=FontWeights.SemiBold });
            heading.Children.Add(Help("Tagline",new Thickness(0,5,0,0)));
            header.Children.Add(heading);
            var close=DockWindow.PlainButton(new TextBlock { Text="×", FontSize=27 },L10n.T("CloseSettings"),palette,dock);
            close.HorizontalAlignment=HorizontalAlignment.Right; close.VerticalAlignment=VerticalAlignment.Top;
            close.Width=32; close.Height=35; close.Tag="settings-close"; close.Click+=delegate { Close(); }; header.Children.Add(close);
            header.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.LeftButton==MouseButtonState.Pressed) DragMove(); };
            grid.Children.Add(header);
            var themeBar=ThemeChoices(); Grid.SetRow(themeBar,1); grid.Children.Add(themeBar);
            var columns=new Grid { Tag="settings-columns" };
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(28) });
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            var appearance=new StackPanel { Tag="settings-appearance" };
            var behavior=new StackPanel { Tag="settings-behavior" }; Grid.SetColumn(behavior,2);
            columns.Children.Add(appearance); columns.Children.Add(behavior); Grid.SetRow(columns,2); grid.Children.Add(columns);
            AddLanguage(appearance);
            AddHeading(appearance,"Appearance");
            AddSlider(appearance,"Opacity",15,100,dock.Store.Settings.Opacity*100,delegate(double v) { dock.Store.Settings.Opacity=v/100; },"%");
            AddSlider(appearance,"IconSize",24,80,dock.Store.Settings.IconSize,delegate(double v) { dock.Store.Settings.IconSize=v; }," px");
            AddSlider(appearance,"HoverZoom",100,165,dock.Store.Settings.Zoom*100,delegate(double v) { dock.Store.Settings.Zoom=v/100; },"%");
            AddCorners(appearance);
            AddRows(appearance);
            AddPlacement(behavior);
            AddHeading(behavior,"Behavior");
            AddCheck(behavior,"ShowLabels",dock.Store.Settings.ShowLabels,delegate(bool v) { dock.Store.Settings.ShowLabels=v; });
            var floating=AddCheck(behavior,"AlwaysOnTop",dock.Store.Settings.AlwaysOnTop,delegate(bool v) { dock.Store.Settings.AlwaysOnTop=v; });
            floating.ToolTip=L10n.T("VisibilityHelp");
            bool startup=false, startupAvailable=!Program.TestMode;
            if(startupAvailable) {
                try { startup=Native.GetStartupEnabled(); }
                catch(Exception ex) { startupAvailable=false; dock.Toast(L10n.T("StartupReadFailed")+ex.Message); }
            }
            var startupCheck=AddCheck(behavior,"Startup",startup,delegate(bool v) {
                if(Program.TestMode) return;
                Native.SetStartup(v); dock.Store.Settings.LaunchAtLogin=v;
            });
            startupCheck.IsEnabled=startupAvailable;
            startupCheck.ToolTip=L10n.T("StartupHelp");
            var automatic=AddCheck(behavior,"AutoAddDesktop",dock.Store.Settings.AutoAddDesktopShortcuts,delegate(bool v) {
                bool previous=dock.Store.Settings.AutoAddDesktopShortcuts;
                dock.Store.Settings.AutoAddDesktopShortcuts=v;
                try { dock.Store.Save(); }
                catch { dock.Store.Settings.AutoAddDesktopShortcuts=previous; throw; }
            });
            automatic.ToolTip=L10n.T("AutoAddHelp");
            AddCheck(behavior,"MoveDesktop",dock.Store.Settings.MoveDesktopShortcuts,delegate(bool v) { dock.Store.Settings.MoveDesktopShortcuts=v; });
            behavior.Children.Add(Help("MoveHelp",new Thickness(0,0,0,12)));
            behavior.Children.Add(Help("UsageHelp",new Thickness(0,0,0,0)));
            var footer=Help("AutoSaveHelp",new Thickness(0,12,0,0)); footer.FontSize=10; Grid.SetRow(footer,3); grid.Children.Add(footer);
            frame.Child=grid;
            Content=new Viewbox { Stretch=Stretch.Uniform, StretchDirection=StretchDirection.DownOnly, Child=frame };
        }
        internal void ApplyThemeSelection() {
            palette=dock.Palette;
            foreach(var pair in themeChoices) Select(pair.Value,pair.Key==dock.Store.Settings.Theme);
            refreshingThemeValues=true;
            try {
                Slider opacity,corners;
                if(appearanceSliders.TryGetValue("Opacity",out opacity)) opacity.Value=dock.Store.Settings.Opacity*100;
                if(appearanceSliders.TryGetValue("CornerRadius",out corners)) corners.Value=dock.Store.Settings.CornerRadius;
            } finally { refreshingThemeValues=false; }
        }
        FrameworkElement ThemeChoices() {
            var body=new StackPanel { Margin=new Thickness(0,0,0,18), Tag="settings-themes" };
            body.Children.Add(new TextBlock { Text=L10n.T("Theme"), FontSize=13, Margin=new Thickness(0,0,0,8) });
            var choices=new Grid();
            foreach(string id in ThemeCatalog.Ids) {
                string choice=id; int column=choices.ColumnDefinitions.Count;
                choices.ColumnDefinitions.Add(new ColumnDefinition());
                var content=new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(24) }); content.ColumnDefinitions.Add(new ColumnDefinition());
                var sample=ThemePalette.For(id);
                content.Children.Add(new Border { Width=15, Height=24, CornerRadius=new CornerRadius(6), Background=ThemePalette.Brush(sample.Surface), BorderBrush=ThemePalette.Brush(sample.Accent), BorderThickness=new Thickness(2), HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Center });
                var names=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
                names.Children.Add(new TextBlock { Text=L10n.T("ThemeName_"+id), FontSize=12, FontWeight=FontWeights.SemiBold });
                names.Children.Add(new TextBlock { Text=L10n.T("ThemeDescription_"+id), FontSize=9, TextWrapping=TextWrapping.Wrap, MaxWidth=125, Foreground=muted });
                Grid.SetColumn(names,1); content.Children.Add(names);
                var button=Choice(String.Empty,dock.Store.Settings.Theme==id); button.Content=content; button.Height=57; button.Padding=new Thickness(7,0,5,0);
                button.Margin=new Thickness(column==0?0:6,0,0,0); button.Tag="theme-"+id;
                System.Windows.Automation.AutomationProperties.SetName(button,"Theme: "+id);
                button.ToolTip=L10n.T("ThemeDescription_"+id)+" · "+L10n.T("ThemeHelp");
                button.Click+=delegate { dock.SetTheme(choice); };
                themeChoices[id]=button;
                Grid.SetColumn(button,column); choices.Children.Add(button);
            }
            body.Children.Add(choices); return body;
        }
        TextBlock Help(string key,Thickness margin) {
            return new TextBlock { Text=L10n.T(key), Foreground=muted, FontSize=11, LineHeight=16,
                TextWrapping=TextWrapping.Wrap, Margin=margin };
        }
        void AddHeading(Panel target,string key) {
            target.Children.Add(new TextBlock { Text=L10n.T(key), FontSize=14, FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,0,0,13) });
        }
        Button Choice(string text,bool selected) {
            var button=new Button { Content=new TextBlock { Text=text, TextWrapping=TextWrapping.Wrap, TextAlignment=TextAlignment.Center },
                Height=33, Padding=new Thickness(4,0,4,0), Foreground=dock.ThemeBrush("Foreground"), BorderThickness=new Thickness(1), Cursor=Cursors.Hand };
            button.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                ("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='body' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='body' Property='BorderBrush' Value='#8BAEFF'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='body' Property='Opacity' Value='.75'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='body' Property='BorderBrush' Value='#C5D7FF'/></Trigger></ControlTemplate.Triggers></ControlTemplate>").Replace("#8BAEFF","{DynamicResource YarneloThemeHoverBorder}").Replace("#C5D7FF","{DynamicResource YarneloThemeFocusBorder}"));
            Select(button,selected); return button;
        }
        void Select(Button button,bool selected) {
            button.Background=dock.ThemeBrush(selected?"Selected":"Choice");
            button.BorderBrush=dock.ThemeBrush(selected?"SelectedBorder":"Stroke");
            button.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
        }
        void AddLanguage(Panel target) {
            target.Children.Add(new TextBlock { Text=L10n.T("Language"), FontSize=13, Margin=new Thickness(0,0,0,8) });
            var choices=new Grid { Margin=new Thickness(0,0,0,18) };
            foreach(string code in new[]{"en","ru"}) {
                string choice=code; int column=choices.ColumnDefinitions.Count;
                choices.ColumnDefinitions.Add(new ColumnDefinition());
                var button=Choice(code=="en"?"English":"Русский",dock.Store.Settings.Language==code);
                button.Margin=new Thickness(column==0?0:5,0,0,0);
                System.Windows.Automation.AutomationProperties.SetName(button,"Language: "+code);
                button.Click+=delegate { dock.SetLanguage(choice); };
                Grid.SetColumn(button,column); choices.Children.Add(button);
            }
            target.Children.Add(choices);
        }
        void AddCorners(Panel target) {
            var slider=AddSlider(target,"CornerRadius",0,100,dock.Store.Settings.CornerRadius,delegate(double v) { dock.Store.Settings.CornerRadius=v; },String.Empty);
            slider.Margin=new Thickness(0,7,0,8);
            var choices=new Grid { Margin=new Thickness(0,0,0,5) };
            var values=new[]{0,23,100}; var keys=new[]{"CornerSquare","CornerSoft","CornerRound"}; var buttons=new Button[3];
            for(int index=0;index<values.Length;index++) {
                int value=values[index];
                choices.ColumnDefinitions.Add(new ColumnDefinition());
                var button=Choice(L10n.T(keys[index]),Math.Abs(slider.Value-value)<.01); button.Height=29;
                button.Tag="corner-preset-"+value; button.Margin=new Thickness(index==0?0:5,0,0,0);
                button.Click+=delegate { slider.Value=value; }; buttons[index]=button;
                Grid.SetColumn(button,index); choices.Children.Add(button);
            }
            slider.ValueChanged+=delegate { for(int index=0;index<values.Length;index++) Select(buttons[index],Math.Abs(slider.Value-values[index])<.01); };
            target.Children.Add(choices); target.Children.Add(Help("CornerHelp",new Thickness(0,0,0,14)));
        }
        void AddRows(Panel target) {
            bool vertical=dock.Store.Settings.Placement=="left" || dock.Store.Settings.Placement=="right";
            target.Children.Add(new TextBlock { Text=L10n.T(vertical?"Columns":"Rows"), FontSize=13, Margin=new Thickness(0,0,0,8) });
            var choices=new Grid { Margin=new Thickness(0,0,0,7) }; var buttons=new Button[4];
            for(int index=0;index<4;index++) {
                int count=index+1; choices.ColumnDefinitions.Add(new ColumnDefinition());
                var button=Choice(count==1?L10n.T(vertical?"OneColumn":"OneRow"):L10n.T(vertical?"ColumnCount":"RowCount",count),dock.Store.Settings.Rows==count);
                button.Margin=new Thickness(index==0?0:5,0,0,0);
                System.Windows.Automation.AutomationProperties.SetName(button,L10n.T(vertical?"ColumnsAutomation":"RowsAutomation")+count);
                button.Click+=delegate {
                    dock.Store.Settings.Rows=count; dock.ApplySettings(); dock.SaveQuietly();
                    for(int n=0;n<buttons.Length;n++) Select(buttons[n],dock.Store.Settings.Rows==n+1);
                };
                buttons[index]=button; Grid.SetColumn(button,index); choices.Children.Add(button);
            }
            target.Children.Add(choices); target.Children.Add(Help("AutoWrapHelp",new Thickness(0,0,0,0)));
        }
        void AddPlacement(Panel target) {
            AddHeading(target,"Placement");
            var choices=new Grid { Margin=new Thickness(0,0,0,8) };
            choices.ColumnDefinitions.Add(new ColumnDefinition()); choices.ColumnDefinitions.Add(new ColumnDefinition());
            choices.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); choices.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            string[] positions={"top","bottom","left","right"}, keys={"PlacementTop","PlacementBottom","PlacementLeft","PlacementRight"};
            for(int index=0;index<positions.Length;index++) {
                string position=positions[index]; var button=Choice(L10n.T(keys[index]),dock.Store.Settings.Placement==position);
                button.Margin=new Thickness(index%2==0?0:5,index>=2?5:0,0,0);
                System.Windows.Automation.AutomationProperties.SetName(button,"Placement: "+position);
                button.Click+=delegate { dock.SetPlacement(position); RefreshLanguage(); };
                Grid.SetRow(button,index/2); Grid.SetColumn(button,index%2); choices.Children.Add(button);
            }
            target.Children.Add(choices);
            var free=Choice(L10n.T("PlacementFree"),dock.Store.Settings.Placement=="free"); free.Height=29; free.Margin=new Thickness(0,0,0,6);
            System.Windows.Automation.AutomationProperties.SetName(free,"Placement: free");
            free.Click+=delegate { dock.SetPlacement("free"); RefreshLanguage(); }; target.Children.Add(free);
            target.Children.Add(Help("PlacementHelp",new Thickness(0,0,0,18)));
        }
        Slider AddSlider(Panel target,string key,double min,double max,double value,Action<double> set,string unit) {
            var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            row.Children.Add(new TextBlock { Text=L10n.T(key), FontSize=13, TextWrapping=TextWrapping.Wrap });
            var label=new TextBlock { Text=Math.Round(value)+unit, Foreground=muted, FontSize=12, Margin=new Thickness(8,0,0,0) };
            Grid.SetColumn(label,1); row.Children.Add(label); target.Children.Add(row);
            var slider=new Slider { Minimum=min, Maximum=max, Value=value, TickFrequency=1, IsSnapToTickEnabled=true,
                Margin=new Thickness(0,7,0,16), Foreground=dock.ThemeBrush("Accent"), Tag="setting-"+key };
            slider.Template=SliderTemplate();
            System.Windows.Automation.AutomationProperties.SetName(slider,L10n.T(key));
            appearanceSliders[key]=slider;
            slider.ValueChanged+=delegate { label.Text=Math.Round(slider.Value)+unit; if(refreshingThemeValues) return; set(slider.Value); dock.ApplySettings(); dock.SaveQuietly(); };
            target.Children.Add(slider); return slider;
        }
        CheckBox AddCheck(Panel target,string key,bool value,Action<bool> set) {
            var box=new CheckBox { Content=new TextBlock { Text=L10n.T(key), TextWrapping=TextWrapping.Wrap }, IsChecked=value,
                FontSize=12, Foreground=dock.ThemeBrush("Foreground"), Margin=new Thickness(0,0,0,12), Cursor=Cursors.Hand, Tag="setting-"+key };
            box.Template=CheckTemplate();
            System.Windows.Automation.AutomationProperties.SetName(box,L10n.T(key));
            bool updating=false, accepted=value;
            RoutedEventHandler change=delegate {
                if(updating) return;
                bool requested=box.IsChecked==true;
                try { set(requested); dock.ApplySettings(); dock.SaveQuietly(); accepted=requested; }
                catch(Exception ex) {
                    updating=true; box.IsChecked=accepted; updating=false;
                    dock.Toast(ex.Message);
                }
            };
            box.Checked+=change; box.Unchecked+=change; target.Children.Add(box); return box;
        }
        ControlTemplate SliderTemplate() {
            string accent="{DynamicResource YarneloThemeSliderFill}",track="{DynamicResource YarneloThemeSliderTrack}",surface="{DynamicResource YarneloThemeSurface}";
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>
                <Grid Height='18'><Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' IsDirectionReversed='{TemplateBinding IsDirectionReversed}'>
                    <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Grid Background='Transparent'><Border Height='3' CornerRadius='1.5' Background='"+accent+@"'/></Grid></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
                    <Track.Thumb><Thumb Width='13' Height='13'><Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Fill='"+accent+@"' Stroke='"+surface+@"' StrokeThickness='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                    <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Grid Background='Transparent'><Border Height='3' CornerRadius='1.5' Background='"+track+@"'/></Grid></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
                </Track></Grid></ControlTemplate>");
        }
        ControlTemplate CheckTemplate() {
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
                <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='21'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
                    <Border x:Name='box' Width='14' Height='14' VerticalAlignment='Top' Margin='0,1,0,0' CornerRadius='3' Background='{DynamicResource YarneloThemeCheckOff}' BorderBrush='{DynamicResource YarneloThemeStroke}' BorderThickness='1'/>
                    <Path x:Name='mark' Data='M 2,7 L 5,10 L 11,3' Stroke='{DynamicResource YarneloThemeCheckMark}' StrokeThickness='1.7' Width='13' Height='13' VerticalAlignment='Top' Margin='0,1,0,0' Visibility='Collapsed'/>
                    <ContentPresenter Grid.Column='1' RecognizesAccessKey='True'/></Grid>
                <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='box' Property='Background' Value='{DynamicResource YarneloThemeCheckFill}'/><Setter TargetName='mark' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='{DynamicResource YarneloThemeAccent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        }
    }
}
