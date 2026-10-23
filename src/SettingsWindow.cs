using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Polka {
    internal class SettingsWindow : Window {
        DockWindow dock;
        StackPanel body;
        readonly SolidColorBrush muted=new SolidColorBrush(Color.FromRgb(158,170,194));
        public SettingsWindow(DockWindow parent) {
            dock=parent;
            Title=L10n.T("SettingsTitle"); Width=470; Height=740; ResizeMode=ResizeMode.NoResize;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
            ShowInTaskbar=false; WindowStartupLocation=WindowStartupLocation.CenterScreen;
            FontFamily=new FontFamily("Segoe UI"); Foreground=Brushes.White;
            RefreshLanguage();
            KeyDown+=delegate(object s,KeyEventArgs e) { if(e.Key==Key.Escape) Close(); };
            Loaded+=delegate { Height=Math.Min(Height,Native.WorkingArea(this).Height-24); };
        }
        internal void RefreshLanguage() {
            Title=L10n.T("SettingsTitle");
            var frame=new Border { CornerRadius=new CornerRadius(20), Padding=new Thickness(28,23,28,22),
                Background=new SolidColorBrush(Color.FromRgb(25,29,40)), BorderBrush=new SolidColorBrush(Color.FromRgb(66,75,96)), BorderThickness=new Thickness(1) };
            var grid=new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
            var header=new Grid { Margin=new Thickness(0,0,0,18) };
            var title=new TextBlock { Text="Yarnelo", FontSize=27, FontWeight=FontWeights.SemiBold };
            header.Children.Add(title);
            var close=DockWindow.PlainButton(new TextBlock { Text="×", FontSize=27 },L10n.T("CloseSettings"));
            close.HorizontalAlignment=HorizontalAlignment.Right; close.Width=32; close.Height=35; close.Click+=delegate { Close(); }; header.Children.Add(close);
            header.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { if(e.LeftButton==MouseButtonState.Pressed) DragMove(); };
            grid.Children.Add(header);
            var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
            body=new StackPanel(); scroll.Content=body; Grid.SetRow(scroll,1); grid.Children.Add(scroll); frame.Child=grid; Content=frame;
            body.Children.Add(new TextBlock { Text=L10n.T("Tagline"), Foreground=muted, FontSize=13, Margin=new Thickness(0,0,0,22) });
            AddLanguage();
            AddSlider(L10n.T("Opacity"),15,100,dock.Store.Settings.Opacity*100,delegate(double v) { dock.Store.Settings.Opacity=v/100; },"%");
            AddSlider(L10n.T("IconSize"),24,80,dock.Store.Settings.IconSize,delegate(double v) { dock.Store.Settings.IconSize=v; }," px");
            AddSlider(L10n.T("HoverZoom"),100,165,dock.Store.Settings.Zoom*100,delegate(double v) { dock.Store.Settings.Zoom=v/100; },"%");
            AddRows();
            AddCheck(L10n.T("ShowLabels"),dock.Store.Settings.ShowLabels,delegate(bool v) { dock.Store.Settings.ShowLabels=v; });
            var floating=AddCheck(L10n.T("AlwaysOnTop"),dock.Store.Settings.AlwaysOnTop,delegate(bool v) { dock.Store.Settings.AlwaysOnTop=v; });
            floating.ToolTip=L10n.T("VisibilityHelp");
            bool startup=!Program.TestMode && Native.GetStartupEnabled();
            var startupCheck=AddCheck(L10n.T("Startup"),startup,delegate(bool v) {
                if(Program.TestMode) return;
                Native.SetStartup(v); dock.Store.Settings.LaunchAtLogin=v;
            });
            startupCheck.IsEnabled=!Program.TestMode;
            AddCheck(L10n.T("MoveDesktop"),dock.Store.Settings.MoveDesktopShortcuts,delegate(bool v) { dock.Store.Settings.MoveDesktopShortcuts=v; });
            body.Children.Add(new TextBlock { Text=L10n.T("MoveHelp"),
                Foreground=muted, TextWrapping=TextWrapping.Wrap, FontSize=11, LineHeight=17, Margin=new Thickness(0,4,0,17) });
            var center=new Button { Content=L10n.T("CenterBottom"), Height=36, Background=new SolidColorBrush(Color.FromRgb(53,70,105)), Foreground=Brushes.White, BorderThickness=new Thickness(0), Cursor=Cursors.Hand };
            center.Click+=delegate { dock.CenterBottom(); }; body.Children.Add(center);
            body.Children.Add(new TextBlock { Text=L10n.T("UsageHelp"),
                Foreground=muted, TextWrapping=TextWrapping.Wrap, FontSize=11, LineHeight=18, Margin=new Thickness(0,17,0,0) });
            body.Children.Add(new TextBlock { Text=L10n.T("AutoSaveHelp"), Foreground=muted, FontSize=10, Margin=new Thickness(0,15,0,0) });
        }
        void AddLanguage() {
            body.Children.Add(new TextBlock { Text=L10n.T("Language"), FontSize=13, Margin=new Thickness(0,0,0,9) });
            var choices=new Grid { Margin=new Thickness(0,0,0,20) };
            foreach(string code in new[]{"en","ru"}) {
                string choice=code;
                int column=choices.ColumnDefinitions.Count;
                choices.ColumnDefinitions.Add(new ColumnDefinition());
                bool selected=dock.Store.Settings.Language==code;
                var button=new Button { Content=(selected?"✓  ":"")+(code=="en"?"English":"Русский"), Height=34,
                    Foreground=Brushes.White, Background=new SolidColorBrush(selected?Color.FromRgb(58,88,145):Color.FromRgb(36,43,58)),
                    BorderBrush=new SolidColorBrush(Color.FromRgb(66,75,96)), Cursor=Cursors.Hand,
                    Margin=new Thickness(column==0?0:4,0,0,0) };
                System.Windows.Automation.AutomationProperties.SetName(button,"Language: "+code);
                button.Click+=delegate { dock.SetLanguage(choice); };
                Grid.SetColumn(button,column); choices.Children.Add(button);
            }
            body.Children.Add(choices);
        }
        void AddRows() {
            body.Children.Add(new TextBlock { Text=L10n.T("Rows"), FontSize=13, Margin=new Thickness(0,0,0,9) });
            var choices=new Grid { Margin=new Thickness(0,0,0,20) };
            var buttons=new Button[4];
            for(int index=0;index<4;index++) {
                int count=index+1;
                choices.ColumnDefinitions.Add(new ColumnDefinition());
                var button=new Button { Content=count==1?L10n.T("OneRow"):L10n.T("RowCount",count), Height=34,
                    Foreground=Brushes.White, BorderThickness=new Thickness(1), Cursor=Cursors.Hand,
                    Margin=new Thickness(index==0?0:4,0,0,0) };
                button.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='body' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='body' Property='BorderBrush' Value='#8BAEFF'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='body' Property='Opacity' Value='.75'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='body' Property='BorderBrush' Value='#C5D7FF'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
                System.Windows.Automation.AutomationProperties.SetName(button,L10n.T("RowsAutomation")+count);
                button.Click+=delegate {
                    dock.Store.Settings.Rows=count;
                    dock.ApplySettings(); dock.SaveQuietly();
                    UpdateRows(buttons);
                };
                buttons[index]=button; Grid.SetColumn(button,index); choices.Children.Add(button);
            }
            UpdateRows(buttons); body.Children.Add(choices);
        }
        void UpdateRows(Button[] buttons) {
            for(int index=0;index<buttons.Length;index++) {
                bool selected=dock.Store.Settings.Rows==index+1;
                buttons[index].Background=new SolidColorBrush(selected?Color.FromRgb(58,88,145):Color.FromRgb(36,43,58));
                buttons[index].BorderBrush=new SolidColorBrush(selected?Color.FromRgb(116,161,255):Color.FromRgb(66,75,96));
                buttons[index].FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
            }
        }
        void AddSlider(string title,double min,double max,double value,Action<double> set,string unit) {
            var row=new Grid();
            row.Children.Add(new TextBlock { Text=title, FontSize=13 });
            var label=new TextBlock { Text=Math.Round(value)+unit, HorizontalAlignment=HorizontalAlignment.Right, Foreground=muted, FontSize=12 }; row.Children.Add(label); body.Children.Add(row);
            var slider=new Slider { Minimum=min, Maximum=max, Value=value, TickFrequency=1, IsSnapToTickEnabled=true, Margin=new Thickness(0,9,0,20), Foreground=new SolidColorBrush(Color.FromRgb(116,161,255)) };
            System.Windows.Automation.AutomationProperties.SetName(slider,title);
            slider.ValueChanged+=delegate { set(slider.Value); label.Text=Math.Round(slider.Value)+unit; dock.ApplySettings(); dock.SaveQuietly(); };
            body.Children.Add(slider);
        }
        CheckBox AddCheck(string text,bool value,Action<bool> set) {
            var box=new CheckBox { Content=text, IsChecked=value, FontSize=12, Foreground=Brushes.White, Margin=new Thickness(0,0,0,14), Cursor=Cursors.Hand };
            RoutedEventHandler change=delegate {
                try { set(box.IsChecked==true); dock.ApplySettings(); dock.SaveQuietly(); }
                catch(Exception ex) { dock.Toast(ex.Message); }
            };
            box.Checked+=change; box.Unchecked+=change; body.Children.Add(box); return box;
        }
    }
}
