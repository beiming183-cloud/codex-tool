using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CodexToolbox;

public sealed class AdaptiveColumnsPanel:Panel
{
    const double Gap=18;int columns=1;double cellWidth;
    public static int ColumnCount(double width)=>width>=1140?3:width>=740?2:1;
    protected override Size MeasureOverride(Size available)
    {
        double width=double.IsFinite(available.Width)?available.Width:800;
        columns=Math.Max(1,Math.Min(ColumnCount(width),InternalChildren.Count));cellWidth=Math.Max(0,(width-Gap*(columns-1))/columns);
        double height=0,rowHeight=0;int index=0;
        foreach(UIElement child in InternalChildren){child.Measure(new Size(cellWidth,double.PositiveInfinity));rowHeight=Math.Max(rowHeight,child.DesiredSize.Height);if(++index%columns==0){height+=rowHeight+Gap;rowHeight=0;}}
        if(index%columns!=0)height+=rowHeight;else if(index>0)height-=Gap;
        return new Size(width,Math.Max(0,height));
    }
    protected override Size ArrangeOverride(Size final)
    {
        // WPF may arrange at a slightly rounded width; use the measured column count.
        double width=Math.Max(0,(final.Width-Gap*(columns-1))/columns),y=0;
        for(int row=0;row<InternalChildren.Count;row+=columns)
        {
            double height=0;for(int c=0;c<columns&&row+c<InternalChildren.Count;c++)height=Math.Max(height,InternalChildren[row+c].DesiredSize.Height);
            for(int c=0;c<columns&&row+c<InternalChildren.Count;c++)InternalChildren[row+c].Arrange(new Rect(c*(width+Gap),y,width,height));
            y+=height+Gap;
        }
        return final;
    }
}

public sealed partial class MainWindow
{
    Grid rightPanel=null!,pageHeader=null!;StackPanel brandPanel=null!,sidebarFooter=null!;
    TextBlock brandTitle=null!,brandCaption=null!;Button sidebarToggle=null!,zoomOut=null!,zoomIn=null!,zoomReset=null!;
    double displayScale;bool compactSidebar;
    public double DisplayScale=>settings.ZoomPercent/100.0;
    void BuildDisplayBar(Grid right)
    {
        var footer=new Grid{Margin=new Thickness(0,16,0,0)};footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetRow(footer,3);right.Children.Add(footer);
        notice.Margin=new Thickness(0,0,14,0);notice.VerticalAlignment=VerticalAlignment.Center;notice.TextWrapping=TextWrapping.NoWrap;notice.TextTrimming=TextTrimming.CharacterEllipsis;footer.Children.Add(notice);
        zoomOut=UI.Button("缩小界面",()=>ChangeZoom(settings.ZoomPercent-10),id:"ZoomOut");zoomIn=UI.Button("放大界面",()=>ChangeZoom(settings.ZoomPercent+10),id:"ZoomIn");zoomReset=UI.Button("100%",()=>ChangeZoom(100),id:"ZoomReset");
        foreach(var b in new[]{zoomOut,zoomReset,zoomIn}){b.MinHeight=34;b.Padding=new Thickness(10,5,10,5);b.Margin=new Thickness(0,0,4,0);}
        var minus=UI.Icon("M3,12 L21,12",13);minus.Margin=new Thickness(0);zoomOut.Content=minus;
        var plus=UI.Icon("M3,12 L21,12 M12,3 L12,21",13);plus.Margin=new Thickness(0);zoomIn.Content=plus;
        zoomOut.ToolTip="缩小界面 · Ctrl + 减号";zoomIn.ToolTip="放大界面 · Ctrl + 加号";zoomReset.ToolTip="恢复 100% · Ctrl + 0";
        var controls=UI.Horizontal(zoomOut,zoomReset,zoomIn);Grid.SetColumn(controls,1);footer.Children.Add(controls);
        PreviewMouseWheel+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Control){ChangeZoom(settings.ZoomPercent+(e.Delta>0?10:-10));e.Handled=true;}};
    }
    internal void ChangeZoom(int value)
    {
        value=Math.Clamp(value,75,150);if(value==settings.ZoomPercent)return;var previous=settings.ZoomPercent;settings.ZoomPercent=value;
        try{Save();ConfigureDisplay();Feedback($"界面缩放 {value}% · Ctrl+0 恢复默认");}catch{settings.ZoomPercent=previous;Feedback("缩放未保存，请检查配置目录是否可写。");}
    }
    void ConfigureDisplay()
    {
        var scale=DisplayScale;if(displayScale!=scale){displayScale=scale;shell.LayoutTransform=new ScaleTransform(scale,scale);}
        foreach(Window owned in OwnedWindows)if(owned.Content is FrameworkElement child)child.LayoutTransform=new ScaleTransform(scale,scale);
        zoomReset.Content=settings.ZoomPercent+"%";AutomationProperties.SetName(zoomReset,"当前缩放 "+settings.ZoomPercent+"%，点击恢复默认");zoomOut.IsEnabled=settings.ZoomPercent>75;zoomIn.IsEnabled=settings.ZoomPercent<150;
        UpdateResponsiveLayout();
    }
    void ToggleSidebar()
    {
        if((ActualWidth>0?ActualWidth:Width)/DisplayScale<900)
        {
            var menu=new ContextMenu();for(int i=0;i<pageNames.Length;i++){int target=i;var item=new MenuItem{Header=pageNames[i],IsCheckable=true,IsChecked=page==i};item.Click+=(_,_)=>ShowPage(target);menu.Items.Add(item);}menu.PlacementTarget=sidebarToggle;menu.IsOpen=true;return;
        }
        var previous=settings.SidebarCollapsed;settings.SidebarCollapsed=!previous;
        try{Save();UpdateResponsiveLayout();}catch{settings.SidebarCollapsed=previous;Feedback("侧栏设置未保存，请重试。");}
    }
    void UpdateResponsiveLayout()
    {
        if(rightPanel==null||sidebarToggle==null)return;
        double width=(ActualWidth>0?ActualWidth:Width)/DisplayScale;
        compactSidebar=settings.SidebarCollapsed||width<900;
        double sidebar=compactSidebar?76:218; shell.ColumnDefinitions[0].Width=new GridLength(sidebar);
        double logicalHeight=(ActualHeight>0?ActualHeight:Height)/DisplayScale;bool shortWindow=logicalHeight<450;
        double margin=width<800?18:32;rightPanel.Margin=new Thickness(margin,shortWindow?12:width<800?20:28,margin,14);
        double available=width-sidebar-2*margin;var narrow=available<590&&!(shortWindow&&page is 0 or 1&&available>=235);
        if(updateBannerLayout!=null){bool stack=available<650;Grid.SetRow(updateActions,stack?1:0);Grid.SetColumn(updateActions,stack?0:1);Grid.SetColumnSpan(updateActions,stack?2:1);updateActions.Margin=new Thickness(stack?0:14,stack?10:0,0,0);}
        Grid.SetRow(actions,narrow?1:0);Grid.SetColumn(actions,narrow?0:1);Grid.SetColumnSpan(actions,narrow?2:1);
        actions.Margin=narrow?new Thickness(0,shortWindow?8:16,0,0):new Thickness(16,1,0,0);
        subtitle.Visibility=shortWindow?Visibility.Collapsed:Visibility.Visible;pageHeader.Margin=new Thickness(0,0,0,shortWindow?14:24);
        if(autoSummary!=null)autoSummary.Visibility=shortWindow&&autoReport?.Instances.Count>0?Visibility.Collapsed:Visibility.Visible;
        heading.FontSize=narrow?25:29;heading.LineHeight=Math.Ceiling(heading.FontSize*1.5);
        brandTitle.Visibility=brandCaption.Visibility=compactSidebar?Visibility.Collapsed:Visibility.Visible;
        sidebarFooter.Visibility=compactSidebar||(ActualHeight>0?ActualHeight:Height)/DisplayScale<510?Visibility.Collapsed:Visibility.Visible;
        brandPanel.Margin=compactSidebar?new Thickness(12,20,12,20):new Thickness(22,28,16,24);
        sidebarToggle.Content=UI.Icon(compactSidebar?"M3,5 L21,5 M3,12 L21,12 M3,19 L21,19":"M3,3 L21,3 L21,21 L3,21 Z M9,3 L9,21 M16,9 L13,12 L16,15",18);((System.Windows.Shapes.Path)sidebarToggle.Content).Margin=new Thickness(0);
        sidebarToggle.Margin=compactSidebar?new Thickness(0):new Thickness(0,16,0,0);sidebarToggle.ToolTip=compactSidebar?"展开导航":"收起侧栏";
        AutomationProperties.SetName(sidebarToggle,compactSidebar?"展开导航":"收起侧栏");
        PaintNavigation();
    }
    void PaintNavigation()
    {
        foreach(Button b in nav.Children)
        {
            bool selected=(int)b.Tag==page;var ink=UI.Color(selected?"#245DD7":"#53647C");b.Background=selected?UI.Color("#D6E3FC"):Brushes.Transparent;b.BorderThickness=new Thickness(0);b.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;b.ToolTip=pageNames[(int)b.Tag];
            b.HorizontalContentAlignment=compactSidebar?HorizontalAlignment.Center:HorizontalAlignment.Left;b.Padding=compactSidebar?new Thickness(10,12,10,12):new Thickness(12,12,8,12);
            if(b.Content is StackPanel row){foreach(var child in row.Children){if(child is TextBlock t){t.Visibility=compactSidebar?Visibility.Collapsed:Visibility.Visible;t.Foreground=ink;}if(child is System.Windows.Shapes.Path icon){icon.Stroke=ink;icon.Margin=new Thickness(0,0,compactSidebar?0:12,0);}}}
        }
    }
}

public static class WindowSizing
{
    [StructLayout(LayoutKind.Sequential)]struct RectI{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public RectI Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
    [DllImport("user32.dll")]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    public static void Prepare(Window window)
    {
        window.PreviewKeyDown+=(_,e)=>{if(window.Owner is MainWindow owner&&Keyboard.Modifiers==ModifierKeys.Control&&e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract or Key.D0 or Key.NumPad0){owner.ChangeZoom(e.Key is Key.D0 or Key.NumPad0?100:(int)Math.Round(owner.DisplayScale*100)+(e.Key is Key.OemPlus or Key.Add?10:-10));e.Handled=true;}};
        window.PreviewMouseWheel+=(_,e)=>{if(window.Owner is MainWindow owner&&Keyboard.Modifiers==ModifierKeys.Control){owner.ChangeZoom((int)Math.Round(owner.DisplayScale*100)+(e.Delta>0?10:-10));e.Handled=true;}};
        window.SourceInitialized+=(_,_)=>
        {
            Fit(window);var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook((IntPtr h,int message,IntPtr wp,IntPtr lp,ref bool handled)=>{if(message==0x02E0)window.Dispatcher.BeginInvoke(new Action(()=>Fit(window)));return IntPtr.Zero;});
        };
        window.Loaded+=(_,_)=>{if(window.Owner is MainWindow owner&&window.Content is FrameworkElement content)content.LayoutTransform=new ScaleTransform(owner.DisplayScale,owner.DisplayScale);Fit(window);};
    }
    static void Fit(Window window)
    {
        var monitor=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(window).Handle,2),ref monitor))return;
        var dpi=VisualTreeHelper.GetDpi(window);double width=(monitor.Work.Right-monitor.Work.Left)/dpi.DpiScaleX,height=(monitor.Work.Bottom-monitor.Work.Top)/dpi.DpiScaleY;
        window.MinWidth=Math.Min(window.MinWidth,width);window.MinHeight=Math.Min(window.MinHeight,height);
        if(window.WindowState==WindowState.Normal){window.Width=Math.Min(window.Width,width);window.Height=Math.Min(window.Height,height);}
    }
}
