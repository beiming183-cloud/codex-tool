using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Interop;
using Microsoft.Win32;
using WButton=System.Windows.Controls.Button;
using WTextBox=System.Windows.Controls.TextBox;
using WCheckBox=System.Windows.Controls.CheckBox;
using WOrientation=System.Windows.Controls.Orientation;
using WMessageBox=System.Windows.MessageBox;
using WClipboard=System.Windows.Clipboard;
using Path=System.IO.Path;

namespace CodexToolbox;

public static class UI
{
    public static Brush Color(string c)=>new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(c));
    public static TextBlock Text(string text,double size=14,string color="#202124",bool bold=false)=>new(){Text=text,FontSize=size,LineHeight=Math.Ceiling(size*1.5),Foreground=Color(color),FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};
    public static WButton Button(string name,Action action,bool primary=false,string? id=null)
    {
        var b=new WButton{Content=name,Margin=new Thickness(0,0,8,0)};if(primary)b.Style=(Style)System.Windows.Application.Current.FindResource("Primary");
        AutomationProperties.SetName(b,name);if(id!=null)AutomationProperties.SetAutomationId(b,id);b.Click+=(_,_)=>action();return b;
    }
    public static WTextBox Field(string label,string value,Panel parent,bool multiline=false,string? id=null)
    {
        var t=Text(label,13,"#62656B");t.Margin=new Thickness(0,15,0,6);parent.Children.Add(t);
        var box=new WTextBox{Text=value,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden};
        if(multiline){box.Height=90;box.VerticalContentAlignment=VerticalAlignment.Top;}
        AutomationProperties.SetName(box,label);if(id!=null)AutomationProperties.SetAutomationId(box,id);parent.Children.Add(box);return box;
    }
    public static Border Group(UIElement content,Thickness? padding=null)=>new(){Background=Color("#FCFDFF"),BorderBrush=Color("#DDE4EF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=padding??new Thickness(24),Child=content,Margin=new Thickness(0,0,0,20)};
    public static Expander Fold(string title,UIElement content)=>new(){Header=title,Content=content,Margin=new Thickness(2,4,2,18),Padding=new Thickness(0,8,0,0),FontSize=13};
    public static Border Rule()=>new(){Background=Color("#ECECF0"),Height=1,Margin=new Thickness(0,0,0,0)};
    public static StackPanel Horizontal(params UIElement[] children){var p=new StackPanel{Orientation=WOrientation.Horizontal};foreach(var c in children)p.Children.Add(c);return p;}
    public static void Copy(string text,Action<string> feedback)
    {try{WClipboard.SetText(text);feedback("已复制到剪贴板");}catch{feedback("剪贴板暂时被占用，请稍后重试。");}}
    public static System.Windows.Shapes.Path Icon(string data,double size=19)=>new(){Data=Geometry.Parse(data),Stroke=Color("#62656B"),StrokeThickness=1.65,Width=size,Height=size,Stretch=Stretch.Uniform,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,11,0)};
}

public sealed partial class MainWindow:Window
{
    Settings settings;
    readonly Grid shell=new();readonly StackPanel content=new();readonly StackPanel nav=new();readonly ScrollViewer pageScroll=new();
    readonly TextBlock heading=UI.Text("线路总览",27,bold:true),subtitle=UI.Text("",13,"#62656B"),notice=UI.Text("准备就绪",12,"#62656B");
    readonly WrapPanel actions=new(){Orientation=WOrientation.Horizontal};readonly Dictionary<string,ProbeResult> results=[];
    readonly Dictionary<string,(TextBlock Ip,TextBlock State,TextBlock Detail)> profileViews=[];
    readonly Dictionary<string,ResultWindow> toolWindows=[];
    readonly CancellationTokenSource lifetime=new();readonly DispatcherTimer timer=new();
    readonly string[] pageNames=["首页","额度浮层","校园网保护","设置","使用帮助"];
    int page;bool refreshing,closing,guardBusy;DateTime? checkedAt; readonly string[] startupArgs;
    WButton? refreshButton;readonly List<WButton> checkButtons=[];
    public MainWindow(Settings s,string[] args)
    {
        Style=(Style)System.Windows.Application.Current.FindResource(typeof(Window));settings=s;startupArgs=args;Title="Codex 工具箱";Width=1120;Height=800;MinWidth=600;MinHeight=460;ResizeMode=ResizeMode.CanResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        shell.ColumnDefinitions.Add(new(){Width=new GridLength(218)});shell.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Content=shell;
        BuildSidebar();
        var right=new Grid{Margin=new Thickness(32,28,32,18),MaxWidth=1360};rightPanel=right;Grid.SetColumn(right,1);shell.Children.Add(right);
        right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=GridLength.Auto});right.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});right.RowDefinitions.Add(new(){Height=GridLength.Auto});BuildUpdateBanner(right);
        var header=new Grid{Margin=new Thickness(0,0,0,24)};pageHeader=header;header.RowDefinitions.Add(new(){Height=GridLength.Auto});header.RowDefinitions.Add(new(){Height=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var titles=new StackPanel();titles.Children.Add(heading);subtitle.Margin=new Thickness(0,8,0,0);titles.Children.Add(subtitle);header.Children.Add(titles);
        Grid.SetColumn(actions,1);actions.VerticalAlignment=VerticalAlignment.Top;actions.Margin=new Thickness(16,1,0,0);header.Children.Add(actions);Grid.SetRow(header,1);right.Children.Add(header);
        pageScroll.Content=content;Grid.SetRow(pageScroll,2);right.Children.Add(pageScroll);
        BuildDisplayBar(right);
        ConfigureDisplay();WindowSizing.Prepare(this);SizeChanged+=(_,_)=>UpdateResponsiveLayout();
        PreviewKeyDown+=OnKeys;Closing+=(_,_)=>{closing=true;timer.Stop();foreach(var t in quotaTimers.Values)t.Stop();lifetime.Cancel();foreach(var w in toolWindows.Values.ToArray())w.Close();};
        timer.Interval=TimeSpan.FromSeconds(s.RefreshSeconds);timer.Tick+=async(_,_)=>{if(page==2)await RefreshCampusRecognition();else await RefreshProfiles();};timer.Start();
        ShowPage(0);
        SourceInitialized+=(_,_)=>Native.SetProp(new WindowInteropHelper(this).Handle,"CodexToolbox."+Program.InstanceKey,new IntPtr(1));
        SourceInitialized+=(_,_)=>{if(Array.IndexOf(args,"--capture-dir") is int captureIndex&&captureIndex>=0&&captureIndex+1<args.Length){var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source.AddHook((IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)=>{if(msg==0x8008){CaptureForVerification(args[captureIndex+1],wp.ToInt32());handled=true;}return IntPtr.Zero;});}};
        Loaded+=async(_,_)=>{UpdateInstaller.Acknowledge(args);StartUpdates();if(args.Contains("--page-tools"))ShowPage(1);else if(args.Contains("--page-campus"))ShowPage(2);else if(args.Contains("--page-settings"))ShowPage(3);if(args.Contains("--screenshot")){await Task.Delay(1000);await RefreshProfiles();await Task.Delay(300);Snapshot();}else if(page==0)await RefreshProfiles();};
    }
    void BuildSidebar()
    {
        var side=new Grid{Background=UI.Color("#E8EEF8")};side.RowDefinitions.Add(new(){Height=GridLength.Auto});side.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});side.RowDefinitions.Add(new(){Height=GridLength.Auto});shell.Children.Add(side);
        var brand=new StackPanel{Margin=new Thickness(16,24,16,26)};brandPanel=brand;brandTitle=UI.Text("Codex 工具箱",20,"#23354D",true);brand.Children.Add(brandTitle);brandCaption=UI.Text("连接、检查与保护",12,"#53647C");brandCaption.Margin=new Thickness(0,6,0,0);brand.Children.Add(brandCaption);sidebarToggle=UI.Button("收起侧栏",ToggleSidebar,id:"ToggleSidebar");sidebarToggle.Margin=new Thickness(0,16,0,0);sidebarToggle.HorizontalAlignment=HorizontalAlignment.Left;brand.Children.Add(sidebarToggle);side.Children.Add(brand);
        nav.Margin=new Thickness(12,0,12,0);var navScroll=new ScrollViewer{Content=nav,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(navScroll,1);side.Children.Add(navScroll);
        string[] icons=["M2,5 L20,5 M2,12 L20,12 M2,19 L20,19 M6,2 L6,8 M16,9 L16,15 M8,16 L8,22","M3,20 L3,13 L7,13 L7,20 Z M10,20 L10,8 L14,8 L14,20 Z M17,20 L17,3 L21,3 L21,20 Z","M12,2 L21,6 L20,14 Q18,20 12,23 Q6,20 4,14 L3,6 Z M8,12 L11,15 L17,9","M3,4 L21,4 M3,12 L21,12 M3,20 L21,20 M8,1 L8,7 M16,9 L16,15 M9,17 L9,23","M12,2 A10,10 0 1 1 11.9,2 M9,8 Q10,5 13,6 Q18,8 12,12 L12,15 M12,18 L12,19"];
        for(int i=0;i<pageNames.Length;i++)
        {
            int index=i;var label=UI.Text(pageNames[i],14);var b=UI.Button(pageNames[i],()=>ShowPage(index),id:"Nav"+i);b.Content=UI.Horizontal(UI.Icon(icons[i]),label);b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Padding=new Thickness(12,11,8,11);b.Margin=new Thickness(0,2,0,3);b.Tag=index;nav.Children.Add(b);
        }
        var footer=new StackPanel{Margin=new Thickness(24,16,12,24)};sidebarFooter=footer;footer.Children.Add(UI.Text("Windows 版  "+Updates.CurrentLabel,12,"#53647C"));var t=UI.Text("配置保存在此电脑",11,"#53647C");t.Margin=new Thickness(0,4,0,0);footer.Children.Add(t);Grid.SetRow(footer,2);side.Children.Add(footer);
    }
    void OnKeys(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract or Key.D0 or Key.NumPad0){ChangeZoom(e.Key is Key.D0 or Key.NumPad0?100:settings.ZoomPercent+(e.Key is Key.OemPlus or Key.Add?10:-10));e.Handled=true;return;}
        if(e.Key==Key.F5 || (Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.R)){if(page==0)_=RefreshProfiles();e.Handled=true;}
        else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key is >=Key.D1 and <=Key.D5){ShowPage((int)e.Key-(int)Key.D1);e.Handled=true;}
        else if(Keyboard.Modifiers==ModifierKeys.Control&&e.Key==Key.N){EditProfile(null);e.Handled=true;}
        else if(e.Key==Key.F1){ShowPage(4);e.Handled=true;}
    }
    void ShowPage(int index)
    {
        bool changed=page!=index;
        page=index;content.Children.Clear();actions.Children.Clear();profileViews.Clear();checkButtons.Clear();refreshButton=null;autoHost=null;heading.Text=pageNames[index];
        PaintNavigation();ConfigureDisplay();
        switch(index){case 0:BuildProfiles();break;case 1:BuildQuota();break;case 2:BuildCampus();break;case 3:BuildSettings();break;default:BuildHelp();break;}
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>pageScroll.ScrollToTop()));
        if(changed)Feedback(index==0?(refreshing?"正在检查，请稍候…":autoReport!=null?$"{autoReport.Time:HH:mm:ss} 已更新 · 自动检查已开启":"正在准备自动检查…"):index==2?"启用保护需要 Windows 管理员授权。":"配置仅保存在此电脑。");
        if(SystemParameters.ClientAreaAnimation)content.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(.86,1,TimeSpan.FromMilliseconds(130)){EasingFunction=new System.Windows.Media.Animation.ExponentialEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut,Exponent=4}});
    }
    void Feedback(string text){notice.Text=text;notice.ToolTip=text;}
    void Save(){Store.Save(settings);timer.Interval=TimeSpan.FromSeconds(settings.RefreshSeconds);}
    void BuildProfiles()
    {
        subtitle.Text="打开 Codex 后自动检查，看看连接是否一致。";
        refreshButton=UI.Button(refreshing?"检查中…":"重新检查",async()=>await RefreshProfiles(),true,"RefreshProfiles");refreshButton.IsEnabled=!refreshing;actions.Children.Add(refreshButton);
        BuildAutoOverview();
        var manual=new StackPanel();manual.Children.Add(UI.Text("需要额外检查某个代理时再添加，平时无需设置。",13,"#62656B"));
        var add=UI.Button("添加自定义线路",()=>EditProfile(null),id:"AddProfile");add.HorizontalAlignment=HorizontalAlignment.Left;add.Margin=new Thickness(0,12,0,14);manual.Children.Add(add);
        if(settings.Profiles.Count==0)
        {
            manual.Children.Add(UI.Text("尚未添加自定义线路。目标 IP 可以留空。",12,"#62656B"));
        }
        else
        {
            var list=new StackPanel();foreach(var p in settings.Profiles)
            {
                if(list.Children.Count>0)list.Children.Add(UI.Rule());
                var row=new Grid{Margin=new Thickness(0,19,0,19)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var left=new StackPanel();var top=UI.Horizontal(UI.Text(p.Name,17,bold:true));if(!p.Enabled){var off=UI.Text("已暂停",12,"#62656B");off.Margin=new Thickness(12,0,0,0);top.Children.Add(off);}left.Children.Add(top);
                var proxy=UI.Text(p.Proxy,12,"#62656B");proxy.Margin=new Thickness(0,4,0,10);left.Children.Add(proxy);
                var ip=UI.Text("尚未检查",29,bold:true);ip.FontFamily=new FontFamily("Segoe UI");ip.LineHeight=38;ip.Cursor=System.Windows.Input.Cursors.Hand;ip.ToolTip="点击复制实测 IP";ip.MouseLeftButtonUp+=(_,_)=>{if(results.TryGetValue(p.Id,out var r)&&r.Ip!="—")UI.Copy(r.Ip,Feedback);};AutomationProperties.SetName(ip,p.Name+"实测 IP");left.Children.Add(ip);
                var state=UI.Text("等待本轮检查",13,"#62656B");state.Margin=new Thickness(0,5,0,0);left.Children.Add(state);
                var detail=UI.Text(p.ExpectedIp.Length>0?"预期 "+p.ExpectedIp:"未设置预期 IP",12,"#62656B");detail.Margin=new Thickness(0,3,0,0);left.Children.Add(detail);row.Children.Add(left);
                var buttons=new StackPanel{VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(16,0,0,0)};
                var edit=UI.Button("编辑",()=>EditProfile(p),id:"Edit-"+p.Id);edit.Margin=new Thickness(0,0,0,8);buttons.Children.Add(edit);
                var check=UI.Button("检查",async()=>await CheckOne(p),id:"Probe-"+p.Id);check.IsEnabled=p.Enabled&&!refreshing;check.Tag=p;checkButtons.Add(check);check.Margin=new Thickness(0);buttons.Children.Add(check);Grid.SetColumn(buttons,1);row.Children.Add(buttons);
                list.Children.Add(row);profileViews[p.Id]=(ip,state,detail);PaintProfile(p);
            }
            manual.Children.Add(list);
        }
        content.Children.Add(UI.Fold("自定义线路 · 可选",manual));
    }
    void PaintProfile(Profile p)
    {
        if(!profileViews.TryGetValue(p.Id,out var v))return;
        if(results.TryGetValue(p.Id,out var r)){v.Ip.Text=r.Ip;v.State.Text=r.State;v.State.Foreground=UI.Color(r.Good?"#247043":"#AC382F");v.Detail.Text=r.Detail+(p.ExpectedIp.Length>0&&r.Good?" · 预期 "+p.ExpectedIp:"");}
        else if(!p.Enabled){v.Ip.Text="—";v.State.Text="已暂停自动检查";}
    }
    async Task CheckOne(Profile p)
    {
        if(refreshing||!p.Enabled)return;refreshing=true;UpdateBusy();
        if(profileViews.TryGetValue(p.Id,out var v)){v.State.Text="正在通过代理检查…";v.State.Foreground=UI.Color("#0067D9");}
        Feedback("正在检查 "+p.Name+"…");try{var r=await NetworkService.Probe(p,settings.ProbeUrl,lifetime.Token);if(!closing&&settings.Profiles.Contains(p)){results[p.Id]=r;PaintProfile(p);Feedback(p.Name+"："+r.State);}}finally{refreshing=false;UpdateBusy();}
    }
    async Task RefreshProfiles()
    {
        if(refreshing||closing)return;
        refreshing=true;UpdateBusy();if(page==0)Feedback("正在检查 Codex 的连接与出口…");foreach(var p in settings.Profiles.Where(x=>x.Enabled)){if(profileViews.TryGetValue(p.Id,out var v)){v.State.Text="正在检查…";v.State.Foreground=UI.Color("#0067D9");}}
        var copy=settings.Profiles.Where(p=>p.Enabled).ToArray();using var gate=new SemaphoreSlim(4);
        try
        {
            var autoTask=inspector.Scan(settings,lifetime.Token);
            await Task.WhenAll(copy.Select(async p=>{await gate.WaitAsync(lifetime.Token);try{var r=await NetworkService.Probe(p,settings.ProbeUrl,lifetime.Token);if(!closing&&settings.Profiles.Contains(p)){results[p.Id]=r;PaintProfile(p);}}finally{gate.Release();}}));
            autoReport=await autoTask;if(!closing&&page==0)PaintAutoOverview();
            checkedAt=DateTime.Now;if(!closing&&page==0)Feedback($"{checkedAt:HH:mm:ss} 已更新 · 自动检查已开启");
        }
        catch(OperationCanceledException){}
        catch(Exception){if(!closing)Feedback("自动检查未完成，请检查网络和进程读取权限后重试。");}
        finally{refreshing=false;UpdateBusy();}
    }
    void UpdateBusy(){if(refreshButton!=null){refreshButton.IsEnabled=!refreshing;refreshButton.Content=refreshing?"检查中…":"重新检查";}foreach(var b in checkButtons)b.IsEnabled=!refreshing&&((Profile)b.Tag).Enabled;}
    void EditProfile(Profile? p)
    {
        var dialog=new ProfileDialog(p){Owner=this};if(dialog.ShowDialog()!=true)return;
        try{var candidate=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings),Store.Json)!;int i=p is null?-1:candidate.Profiles.FindIndex(x=>x.Id==p.Id);if(i>=0){candidate.Profiles.RemoveAt(i);results.Remove(p!.Id);}if(!dialog.Deleted){if(i>=0)candidate.Profiles.Insert(i,dialog.Value);else candidate.Profiles.Add(dialog.Value);}Store.Save(candidate);settings=candidate;ShowPage(0);Feedback(dialog.Deleted?"线路已删除":"线路已保存");}catch(Exception e){Feedback(e.Message);}
    }
    void AddToolRow(StackPanel list,string name,string description,string key)
    {
        if(list.Children.Count>0)list.Children.Add(UI.Rule());
        var row=new Grid{Margin=new Thickness(0,19,0,19)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var text=new StackPanel();text.Children.Add(UI.Text(name,16,bold:true));var d=UI.Text(description,13,"#62656B");d.Margin=new Thickness(0,5,18,0);text.Children.Add(d);row.Children.Add(text);
        var button=UI.Button(key=="overlay"?"打开":"检查",()=>OpenTool(key),id:"Tool-"+key);button.Margin=new Thickness(8,0,0,0);button.VerticalAlignment=VerticalAlignment.Center;AutomationProperties.SetName(button,name);Grid.SetColumn(button,1);row.Children.Add(button);list.Children.Add(row);
    }
    void OpenTool(string key)
    {
        if(key=="overlay"){ShowPage(1);return;}
        if(toolWindows.TryGetValue(key,out var existing)){if(existing.WindowState==WindowState.Minimized)existing.WindowState=WindowState.Normal;existing.Activate();return;}
        string title=key switch{"auto"=>"自动线路核验","clash"=>"Clash 状态","connections"=>"应用连接","ips"=>"公网 IP","overlay"=>"额度浮层","account"=>"账号界面代理",_=>"现有网络保护"};
        var w=new ResultWindow(title,async ct=>
        {
            switch(key)
            {
                case "auto":autoReport=await inspector.Scan(settings,ct);if(page==0)PaintAutoOverview();return AutoInspector.Rows(autoReport);
                case "clash":return await NetworkService.CoreStatus(settings,ct);
                case "connections":return await NetworkService.Connections(settings,ct);
                case "ips":
                    var probes=settings.Profiles.Where(p=>p.Enabled).ToList();probes.AddRange(settings.Profiles.Where(p=>p.Enabled&&p.AppProxy.Length>0).Select(p=>new Profile{Name=p.Name+" · 应用代理",Proxy=p.AppProxy,ExpectedIp=p.ExpectedIp}));probes.Add(new Profile{Name="本机直连",Proxy=""});
                    var observed=autoReport??await inspector.Scan(settings,ct);foreach(var i in observed.Instances)foreach(var p in i.Probes)if(!probes.Any(existing=>existing.Proxy==p.Proxy))probes.Add(new Profile{Name="自动发现 · 实例 "+i.Pid,Proxy=p.Proxy});
                    using(var gate=new SemaphoreSlim(4))return (await Task.WhenAll(probes.Select(async p=>{await gate.WaitAsync(ct);try{var r=await NetworkService.Probe(p,settings.ProbeUrl,ct);return new ResultRow(p.Name,r.Ip+" · "+r.Detail,r.State);}finally{gate.Release();}}))).ToList();
                case "overlay":return await NetworkService.Legacy(settings,"Overlay",ct);
                case "account":return await NetworkService.Legacy(settings,"CurrentExit",ct);
                default:return await NetworkService.Legacy(settings,"Network",ct);
            }
        }){Owner=this};toolWindows[key]=w;w.Closed+=(_,_)=>toolWindows.Remove(key);w.Show();
    }
    void BuildCampus()
    {
        subtitle.Text="识别校园网后，自动暂停代理联网。";
        var state=new StackPanel();state.Children.Add(UI.Text(Guard.Installed?"本工具的保护已安装":"本工具的保护尚未启用",17,bold:true));
        var behavior=UI.Text((settings.Campus.AutoDetect?"自动识别校园网":"按已保存的校园网规则识别")+"；"+(settings.Campus.AutoDetectPrograms?"自动发现代理程序。":"限制已指定的代理程序。")+(settings.Campus.BlockOnUnknown?"无法识别网络时也限制代理。":"无法识别网络时放行代理。"),13,"#62656B");behavior.Margin=new Thickness(0,9,0,0);state.Children.Add(behavior);
        if(settings.LegacyRoot.Length>0){var old=UI.Text("此电脑原有保护仍独立运行。",12,"#62656B");old.Margin=new Thickness(0,7,0,0);state.Children.Add(old);}content.Children.Add(UI.Group(state));
        campusRecognition=UI.Text("正在识别当前网络…",17,bold:true);campusEvidence=UI.Text("",12,"#62656B");campusEvidence.Margin=new Thickness(0,9,0,10);campusMarkActions=new StackPanel{Orientation=WOrientation.Horizontal};var recognition=new StackPanel();recognition.Children.Add(campusRecognition);recognition.Children.Add(campusEvidence);recognition.Children.Add(campusMarkActions);campusDetails=UI.Text("",12,"#62656B");recognition.Children.Add(UI.Fold("查看识别依据",campusDetails));content.Children.Add(UI.Group(recognition));
        var form=new StackPanel();form.Children.Add(UI.Text("校园网识别",17,bold:true));
        var explanation=UI.Text("结合当前网络、SSID 和教育域 DNS 后缀识别，不绑定学校。仅名称相似时提示确认，不自动误断流。",12,"#62656B");explanation.Margin=new Thickness(0,8,0,0);form.Children.Add(explanation);
        var automatic=new WCheckBox{Content="自动识别教育 / 校园网络",IsChecked=settings.Campus.AutoDetect};form.Children.Add(automatic);
        var networks=UI.Field("网络名称或匹配式",string.Join(Environment.NewLine,settings.Campus.Networks),form,true,"CampusNetworks");
        var detect=UI.Button("重新识别当前网络",async()=>await RefreshCampusRecognition(),id:"ReadNetwork");detect.HorizontalAlignment=HorizontalAlignment.Left;detect.Margin=new Thickness(0,10,0,0);form.Children.Add(detect);
        var ordinary=UI.Field("已确认的普通网络 · 可选，每行一个",string.Join(Environment.NewLine,settings.Campus.OrdinaryNetworks),form,true,"OrdinaryNetworks");
        var autoPrograms=new WCheckBox{Content="自动发现正在运行的代理内核",IsChecked=settings.Campus.AutoDetectPrograms};form.Children.Add(autoPrograms);
        var programs=UI.Field("受限代理程序 · 每行一个完整 .exe 路径",string.Join(Environment.NewLine,settings.Campus.Programs),form,true,"CampusPrograms");
        var browse=UI.Button("选择代理程序…",()=>{var dlg=new Microsoft.Win32.OpenFileDialog{Filter="Windows 程序|*.exe",Multiselect=true};if(dlg.ShowDialog(this)==true)programs.Text=string.Join(Environment.NewLine,Lines(programs.Text).Concat(dlg.FileNames).Distinct(StringComparer.OrdinalIgnoreCase));});browse.HorizontalAlignment=HorizontalAlignment.Left;browse.Margin=new Thickness(0,10,0,0);form.Children.Add(browse);
        var findPrograms=UI.Button("自动填写代理程序",async()=>{try{var detected=await CampusDiscovery.DetectPrograms(lifetime.Token);programs.Text=string.Join(Environment.NewLine,Lines(programs.Text).Concat(detected).Distinct(StringComparer.OrdinalIgnoreCase));Feedback(detected.Count>0?$"发现 {detected.Count} 个正在运行的代理内核，保存后生效。":"尚未发现支持的代理内核，可先打开代理客户端或手动选择。");}catch{Feedback("代理进程读取失败，请重试。");}},id:"DetectProxyPrograms");findPrograms.Margin=new Thickness(0,10,0,0);findPrograms.HorizontalAlignment=HorizontalAlignment.Left;form.Children.Add(findPrograms);
        var unknown=new WCheckBox{Content="无法识别网络时，继续限制指定代理程序",IsChecked=settings.Campus.BlockOnUnknown};form.Children.Add(unknown);
        CampusPolicy Read()=>new(){Networks=Lines(networks.Text),OrdinaryNetworks=Lines(ordinary.Text),Programs=Lines(programs.Text),BlockOnUnknown=unknown.IsChecked==true,AutoDetect=automatic.IsChecked==true,AutoDetectPrograms=autoPrograms.IsChecked==true};
        void SavePolicy(){var policy=Read();Guard.Validate(policy,false);settings.Campus=policy;Save();}
        campusMarkActions.Children.Add(UI.Button("记为校园网",()=>{if(campusAssessment is null)return;networks.Text=string.Join(Environment.NewLine,Lines(networks.Text).Concat(campusAssessment.RememberNames).Distinct(StringComparer.OrdinalIgnoreCase));ordinary.Text=string.Join(Environment.NewLine,Lines(ordinary.Text).Where(n=>!campusAssessment.RememberNames.Contains(n,StringComparer.OrdinalIgnoreCase)));try{SavePolicy();_=RefreshCampusRecognition();Feedback("已记为校园网；已安装的自动保护需要点击页头更新后应用。");}catch(Exception e){Feedback(e.Message);}},id:"RememberCampus"));
        campusMarkActions.Children.Add(UI.Button("记为普通网络",()=>{if(campusAssessment is null)return;ordinary.Text=string.Join(Environment.NewLine,Lines(ordinary.Text).Concat(campusAssessment.RememberNames).Distinct(StringComparer.OrdinalIgnoreCase));networks.Text=string.Join(Environment.NewLine,Lines(networks.Text).Where(n=>!campusAssessment.RememberNames.Contains(n,StringComparer.OrdinalIgnoreCase)));try{SavePolicy();_=RefreshCampusRecognition();Feedback("已记为普通网络；已安装的自动保护需要点击页头更新后应用。");}catch(Exception e){Feedback(e.Message);}},id:"RememberOrdinary"));
        var buttons=UI.Horizontal();buttons.Margin=new Thickness(0,12,0,0);
        buttons.Children.Add(UI.Button("保存并预览",async()=>{try{SavePolicy();await RefreshCampusRecognition();Feedback(campusAssessment?.IsCampus==true?"预览：已识别校园网；启用保护后将限制代理程序。":"预览已更新；保存本身不改变防火墙。 ");}catch(Exception e){Feedback(e.Message);}},id:"PreviewCampus"));
        WButton? enable=null;
        enable=UI.Button(guardBusy?"操作进行中…":Guard.Installed?"更新自动保护…":"启用自动保护…",async()=>
        {
            if(guardBusy)return;
            try
            {
                SavePolicy();Guard.Validate(settings.Campus,true);
                if(WMessageBox.Show(this,"将安装仅管理所选程序的校园网保护。\n\n匹配校园网时，这些程序会被阻止出站联网；其他网络会自动放行。关闭工具箱后保护仍会运行。接下来 Windows 将请求管理员授权。","启用校园网自动保护",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
                guardBusy=true;enable!.IsEnabled=false;enable.Content="正在等待 Windows 授权…";buttons.IsEnabled=false;Feedback("正在请求 Windows 管理员授权并安装保护，请稍候…");var draft=Path.Combine(Store.DirectoryPath,"guard-draft.json");File.WriteAllText(draft,System.Text.Json.JsonSerializer.Serialize(settings.Campus,Store.Json));
                await Task.Run(()=>Guard.Elevated("--install-guard",draft));Feedback("校园网保护已安装。稍后重新打开此页可查看运行状态。");
            }
            catch(Exception e){Feedback(e is System.ComponentModel.Win32Exception?"已取消 Windows 授权，自动保护未改变。":e.Message);}
            finally{guardBusy=false;buttons.IsEnabled=true;if(page==2)ShowPage(2);}
        },true,"EnableGuard");enable.IsEnabled=!guardBusy;actions.Children.Add(enable);form.Children.Add(buttons);
        var note=UI.Text("启用或更新需要 Windows 管理员授权。代理内核更新后，请重新检查受限程序。",12,"#62656B");note.Margin=new Thickness(0,15,0,0);form.Children.Add(note);content.Children.Add(UI.Fold("调整识别规则与受限程序",form));
        var protectionDetails=new StackPanel();foreach(var r in Guard.Status())protectionDetails.Children.Add(UI.Text(r.Name+"："+r.Value,12,"#62656B"));content.Children.Add(UI.Fold("保护运行详情",protectionDetails));
        if(Guard.Installed){WButton? remove=null;remove=UI.Button(guardBusy?"操作进行中…":"停用并移除本工具的保护…",async()=>{if(guardBusy)return;if(WMessageBox.Show(this,"将移除本工具的任务与防火墙规则。确认停用？","停用保护",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;guardBusy=true;remove!.IsEnabled=false;enable.IsEnabled=false;remove.Content="正在等待 Windows 授权…";Feedback("正在请求 Windows 管理员授权并移除本工具保护，请稍候…");try{await Task.Run(()=>Guard.Elevated("--remove-guard"));Feedback("本工具的校园网保护已移除。");}catch(Exception e){Feedback(e is System.ComponentModel.Win32Exception?"已取消 Windows 授权，原保护未改变。":e.Message);}finally{guardBusy=false;if(page==2)ShowPage(2);}},id:"RemoveGuard");remove.IsEnabled=!guardBusy;remove.HorizontalAlignment=HorizontalAlignment.Left;content.Children.Add(remove);}
        _=RefreshCampusRecognition();
    }
    static List<string> Lines(string s)=>s.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    void BuildSettings()
    {
        subtitle.Text="通常保持默认即可，无需填写路径或接口。";
        BuildUpdateSettings();
        var defaults=new StackPanel();defaults.Children.Add(UI.Text("自动发现已开启",18,bold:true));var defaultsText=UI.Text("自动查找 Codex、检查线路和识别校园网。\n需要排查问题或迁移配置时，再展开下面的选项。",13,"#62656B");defaultsText.Margin=new Thickness(0,9,0,0);defaults.Children.Add(defaultsText);content.Children.Add(UI.Group(defaults));
        var form=new StackPanel();form.Children.Add(UI.Text("连接与检查",17,bold:true));
        var controller=UI.Field("Clash 控制接口 · 留空自动识别",settings.Controller,form,id:"Controller");controller.ToolTip="例如 http://127.0.0.1:9090；也支持运行内核公开的本机管道。";
        var label=UI.Text("控制接口密钥 · 仅保存在此 Windows 用户下",13,"#62656B");label.Margin=new Thickness(0,15,0,6);form.Children.Add(label);
        var secret=new PasswordBox{Padding=new Thickness(10,8,10,8),MinHeight=38};try{secret.Password=LocalSecret.Unprotect(settings.SecretProtected);}catch{Feedback("原接口密钥无法解密，请重新输入并保存。");}form.Children.Add(secret);AutomationProperties.SetName(secret,"控制接口密钥");
        var probe=UI.Field("公网 IP 检测地址 · HTTPS",settings.ProbeUrl,form,id:"ProbeUrl");
        var interval=UI.Field("自动检查间隔 · 秒，10–3600",settings.RefreshSeconds.ToString(),form,id:"RefreshSeconds");
        var advanced=new StackPanel();var legacy=UI.Field("原有工具目录 · 可选，仅用于保留本机旧版集成",settings.LegacyRoot,advanced,id:"LegacyRoot");
        var expander=new Expander{Header="本机集成",Content=advanced,Margin=new Thickness(0,18,0,0)};form.Children.Add(expander);
        var save=UI.Button("保存设置",()=>{try
        {
            if(!int.TryParse(interval.Text,out var seconds))throw new InvalidDataException("刷新间隔需要输入整数。");
            var candidate=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings),Store.Json)!;
            candidate.Controller=controller.Text.Trim();candidate.SecretProtected=LocalSecret.Protect(secret.Password);candidate.ProbeUrl=probe.Text.Trim();candidate.RefreshSeconds=seconds;candidate.LegacyRoot=legacy.Text.Trim();Store.Validate(candidate);Store.Save(candidate);settings=candidate;timer.Interval=TimeSpan.FromSeconds(seconds);Feedback("设置已保存");
        }catch(Exception e){Feedback(e.Message);}},true,"SaveSettings");save.HorizontalAlignment=HorizontalAlignment.Left;save.Margin=new Thickness(0,20,0,0);form.Children.Add(save);content.Children.Add(UI.Fold("高级连接设置",form));
        var diagnostics=new StackPanel();AddToolRow(diagnostics,"线路检查详情","查看每个 Codex 的连接证据和 IP 变化记录。","auto");AddToolRow(diagnostics,"Clash 状态","检查代理是否连接，以及线路分组状态。","clash");AddToolRow(diagnostics,"应用连接","查看已采到的请求与代理链。","connections");AddToolRow(diagnostics,"公网 IP","检查代理出口与本机直连出口。","ips");if(settings.LegacyRoot.Length>0){AddToolRow(diagnostics,"账号界面代理","检查此电脑原启动器的代理。","account");AddToolRow(diagnostics,"现有网络保护","检查此电脑原有保护。","legacy");}content.Children.Add(UI.Fold("诊断工具",diagnostics));
        var exchange=new StackPanel();exchange.Children.Add(UI.Text("配置与分享",17,bold:true));var d=UI.Text("导出包含线路地址、目标 IP 和校园网规则。接口密钥、登录目录和本机集成不会导出；分享前仍请检查文件内容。导入后需要单独启用校园网保护。",12,"#62656B");d.Margin=new Thickness(0,10,0,16);exchange.Children.Add(d);
        var exchangeButtons=new WrapPanel();foreach(var b in new[]{UI.Button("导入配置…",Import,id:"ImportConfig"),UI.Button("导出配置…",Export,id:"ExportConfig"),UI.Button("打开配置位置",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",Store.DirectoryPath){UseShellExecute=true}))}){b.Margin=new Thickness(0,0,8,8);exchangeButtons.Children.Add(b);}exchange.Children.Add(exchangeButtons);content.Children.Add(UI.Fold("配置备份与迁移",exchange));
    }
    void Export()
    {
        var dlg=new Microsoft.Win32.SaveFileDialog{Filter="工具箱配置|*.json",FileName="codex-toolbox-settings.json"};if(dlg.ShowDialog(this)!=true)return;
        try{File.WriteAllText(dlg.FileName,System.Text.Json.JsonSerializer.Serialize(Store.ShareCopy(settings),Store.Json));Feedback("已导出配置；分享前请检查其中的 IP 和网络名称。");}catch(Exception e){Feedback(e.Message);}
    }
    void Import()
    {
        var dlg=new Microsoft.Win32.OpenFileDialog{Filter="工具箱配置|*.json"};if(dlg.ShowDialog(this)!=true)return;
        try
        {
            if(new FileInfo(dlg.FileName).Length>1024*1024)throw new InvalidDataException("配置文件过大。");
            var s=System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(dlg.FileName),Store.Json)??throw new InvalidDataException("配置为空。");
            s.SecretProtected="";s.LegacyRoot="";foreach(var p in s.Profiles)p.AuthHome="";Store.Validate(s);
            if(WMessageBox.Show(this,$"将替换当前配置，导入 {s.Profiles.Count} 条线路。原配置会保留为 settings.json.bak。\n\n导入不会启用或更新已安装的校园网保护。","导入配置",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
            Store.Save(s);settings=s;results.Clear();timer.Interval=TimeSpan.FromSeconds(s.RefreshSeconds);ShowPage(0);Feedback("配置已导入；请检查连接设置，校园网保护需要单独启用或更新。");
        }catch(Exception e){Feedback("导入未完成："+e.Message);}
    }
    void BuildHelp()
    {
        subtitle.Text="从自己的线路开始，逐步确认每一段连接。";
        var body=new StackPanel();
        foreach(var item in new[]{("自动线路检查","打开 Codex 后会自动发现桌面实例、CLI、代理和已采到的 TCP 请求。无需双开、固定 IP 或提前添加线路；手动线路只用于额外检查。"),("区分出口与应用连接","出口检测会通过指定代理访问检测接口。应用连接读取 Clash 中已采到的 TCP 请求；没有观测到请求时，无法判断应用实际线路。"),("设置校园网","默认结合当前网络、SSID 和教育域 DNS 后缀识别。依据不足时可一键记为校园网或普通网络；代理程序也可自动发现。确认启用并通过 Windows 授权后，才会自动限制联网。"),("查看额度","进入“额度浮层”，工具会自动查找正在运行的 Codex 与对应登录状态。点击添加即可；不支持直接添加的窗口使用独立浮窗。登录过期时请在 Codex 中重新登录。工具不会刷新登录凭据。"),("键盘操作","F5 / Ctrl+R 刷新线路；Ctrl+N 添加；Ctrl+1–5 切换页面；Esc 关闭子窗口；结果窗口可复制完整结果。"),("分享与恢复","分享便携包不包含个人配置。配置按 Windows 用户保存；保存时会保留上一份备份。导出的配置仍可能含 IP 和网络名称，分享前请检查。")})
        {if(body.Children.Count>0){var line=UI.Rule();line.Margin=new Thickness(0,19,0,19);body.Children.Add(line);}body.Children.Add(UI.Text(item.Item1,16,bold:true));var text=UI.Text(item.Item2,13,"#62656B");text.Margin=new Thickness(0,8,0,0);body.Children.Add(text);}
        content.Children.Add(UI.Group(body));
    }
    void Snapshot()
    {
        int i=Array.IndexOf(startupArgs,"--screenshot");if(i<0||i+1>=startupArgs.Length)return;
        UpdateLayout();var visual=(FrameworkElement)VisualTreeHelper.GetChild(this,0);var dpi=VisualTreeHelper.GetDpi(visual);var b=new RenderTargetBitmap((int)Math.Ceiling(visual.RenderSize.Width*dpi.DpiScaleX),(int)Math.Ceiling(visual.RenderSize.Height*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);b.Render(visual);
        var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var f=File.Create(startupArgs[i+1]);e.Save(f);
        if(startupArgs.Contains("--close-after-snapshot"))Close();
    }
    void CaptureForVerification(string directory,int tag)
    {
        Directory.CreateDirectory(directory);Window target=this;foreach(Window owned in OwnedWindows)if(owned.IsVisible)target=owned;
        target.UpdateLayout();var visual=(FrameworkElement)VisualTreeHelper.GetChild(target,0);var dpi=VisualTreeHelper.GetDpi(visual);
        var image=new RenderTargetBitmap((int)Math.Ceiling(visual.RenderSize.Width*dpi.DpiScaleX),(int)Math.Ceiling(visual.RenderSize.Height*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);image.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var f=File.Create(Path.Combine(directory,$"view-{tag}.png")))encoder.Save(f);
        File.WriteAllText(Path.Combine(directory,$"view-{tag}.json"),System.Text.Json.JsonSerializer.Serialize(new{target.Title,Width=visual.ActualWidth,Height=visual.ActualHeight,Dpi=dpi.PixelsPerInchX,Font=target.FontFamily.Source,BodyFont=heading.FontFamily.Source,Page=pageNames[page],settings.ZoomPercent,CompactSidebar=compactSidebar,ContentWidth=pageScroll.ViewportWidth,HeaderStacked=Grid.GetRow(actions)==1,CardColumns=AdaptiveColumnsPanel.ColumnCount(pageScroll.ViewportWidth)},Store.Json));
    }
}
