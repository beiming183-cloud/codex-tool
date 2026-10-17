using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WButton=System.Windows.Controls.Button;

namespace CodexToolbox;
public sealed class ProfileDialog:Window
{
    public Profile Value {get;private set;}public bool Deleted{get;private set;}
    public ProfileDialog(Profile? existing)
    {
        Style=(Style)System.Windows.Application.Current.FindResource(typeof(Window));Value=existing is null?new():JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(existing))!;
        Title=existing is null?"添加线路":"编辑线路";Width=580;Height=690;MinWidth=510;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        WindowSizing.Prepare(this);
        var root=new Grid{Margin=new Thickness(28,22,28,22)};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=root;
        var header=new StackPanel();header.Children.Add(UI.Text(Title,24,bold:true));var subtitle=UI.Text("为这条线路取个名字，随时可以回来调整。",13,"#62656B");subtitle.Margin=new Thickness(0,8,0,12);header.Children.Add(subtitle);root.Children.Add(header);
        var form=new StackPanel{Margin=new Thickness(0,0,9,16)};var scroll=new ScrollViewer{Content=form};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        var name=UI.Field("线路名称",Value.Name,form,id:"ProfileName");var proxy=UI.Field("代理地址",Value.Proxy,form,id:"ProfileProxy");
        var hint=UI.Text("例如 http://127.0.0.1:7890 或 socks5://127.0.0.1:7890",12,"#62656B");hint.Margin=new Thickness(0,5,0,0);form.Children.Add(hint);
        var expected=UI.Field("预期 IP · 可选，留空仅检测",Value.ExpectedIp,form,id:"ProfileExpectedIp");
        var group=UI.Field("Clash 分组名称 · 可选",Value.Group,form,id:"ProfileGroup");
        var enabled=new System.Windows.Controls.CheckBox{Content="启用此线路的自动检查",IsChecked=Value.Enabled,Margin=new Thickness(0,17,0,8)};form.Children.Add(enabled);
        var advanced=new StackPanel();var appProxy=UI.Field("应用使用的代理地址 · 可选",Value.AppProxy,advanced,id:"ProfileAppProxy");
        var home=UI.Field("Codex 登录目录 · 可选，用于额度查询",Value.AuthHome,advanced,id:"ProfileAuthHome");
        var browse=UI.Button("选择登录目录…",()=>{var dlg=new Microsoft.Win32.OpenFolderDialog{Title="选择包含 auth.json 的 Codex 目录"};if(dlg.ShowDialog(this)==true)home.Text=dlg.FolderName;});browse.HorizontalAlignment=HorizontalAlignment.Left;browse.Margin=new Thickness(0,8,0,0);advanced.Children.Add(browse);
        var safety=UI.Text("此目录仅保存在本机，导出配置时会移除。不会上传登录文件或刷新登录凭据。",12,"#62656B");safety.Margin=new Thickness(0,8,0,0);advanced.Children.Add(safety);
        form.Children.Add(new Expander{Header="账号与应用",Content=advanced,Margin=new Thickness(0,8,0,0),IsExpanded=Value.AuthHome.Length>0||Value.AppProxy.Length>0});
        var footer=new StackPanel();var error=UI.Text("",12,"#AC382F");error.Margin=new Thickness(0,0,0,12);footer.Children.Add(error);
        var buttons=new Grid();buttons.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});buttons.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        if(existing!=null){var delete=UI.Button("删除线路",()=>{if(System.Windows.MessageBox.Show(this,"删除后将停止检查此线路。确认删除？","删除线路",MessageBoxButton.OKCancel,MessageBoxImage.Warning)==MessageBoxResult.OK){Deleted=true;DialogResult=true;}},id:"DeleteProfile");delete.Foreground=UI.Color("#AC382F");delete.HorizontalAlignment=HorizontalAlignment.Left;buttons.Children.Add(delete);}
        var cancel=UI.Button("取消",()=>DialogResult=false,id:"CancelProfile");cancel.IsCancel=true;
        var save=UI.Button("保存",()=>{try{Value.Name=name.Text.Trim();Value.Proxy=proxy.Text.Trim();Value.ExpectedIp=expected.Text.Trim();Value.Group=group.Text.Trim();Value.AppProxy=appProxy.Text.Trim();Value.AuthHome=home.Text.Trim();Value.Enabled=enabled.IsChecked==true;Store.Validate(new Settings{Profiles=[Value]});DialogResult=true;}catch(Exception e){error.Text=e.Message;}},true,"SaveProfile");save.IsDefault=true;save.Margin=new Thickness(0);
        var right=UI.Horizontal(cancel,save);Grid.SetColumn(right,1);buttons.Children.Add(right);footer.Children.Add(buttons);Grid.SetRow(footer,2);root.Children.Add(footer);
        Loaded+=(_,_)=>{name.Focus();name.SelectAll();};
    }
}

public sealed class ResultWindow:Window
{
    readonly Func<CancellationToken,Task<List<ResultRow>>> load;readonly DataGrid table=new();readonly TextBlock status=UI.Text("准备检查…",13,"#62656B");readonly WButton refresh,copy;
    readonly CancellationTokenSource lifetime=new();List<ResultRow> rows=[];bool busy,closed;
    public ResultWindow(string title,Func<CancellationToken,Task<List<ResultRow>>> loader)
    {
        Style=(Style)System.Windows.Application.Current.FindResource(typeof(Window));load=loader;Title=title+" · Codex 工具箱";Width=830;Height=535;MinWidth=680;MinHeight=410;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        WindowSizing.Prepare(this);
        var root=new Grid{Margin=new Thickness(25)};root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=root;
        var header=new StackPanel();header.Children.Add(UI.Text(title,24,bold:true));status.Margin=new Thickness(0,10,0,20);header.Children.Add(status);root.Children.Add(header);
        var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));textStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center));
        table.Columns.Add(new DataGridTextColumn{Header="检查项",Binding=new System.Windows.Data.Binding("Name"),Width=new DataGridLength(155),ElementStyle=textStyle});
        table.Columns.Add(new DataGridTextColumn{Header="结果",Binding=new System.Windows.Data.Binding("Value"),Width=new DataGridLength(1,DataGridLengthUnitType.Star),ElementStyle=textStyle});
        table.Columns.Add(new DataGridTextColumn{Header="状态",Binding=new System.Windows.Data.Binding("State"),Width=new DataGridLength(110),ElementStyle=textStyle});AutomationProperties.SetAutomationId(table,"ResultTable");
        var frame=UI.Group(table,new Thickness(0));frame.Margin=new Thickness(0,0,0,18);Grid.SetRow(frame,1);root.Children.Add(frame);
        copy=UI.Button("复制全部",()=>UI.Copy(Title+Environment.NewLine+string.Join(Environment.NewLine,rows.Select(r=>$"{r.Name}\t{r.Value}\t{r.State}")),text=>status.Text=text),id:"CopyResults");copy.IsEnabled=false;
        refresh=UI.Button("重新检查",async()=>await Refresh(),true,"RetryTool");var close=UI.Button("关闭",Close);close.IsCancel=true;
        var footer=UI.Horizontal(copy,refresh,close);footer.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetRow(footer,2);root.Children.Add(footer);
        Loaded+=async(_,_)=>await Refresh();Closed+=(_,_)=>{closed=true;lifetime.Cancel();};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}else if(e.Key==Key.F5||(e.Key==Key.R&&Keyboard.Modifiers==ModifierKeys.Control)){_=Refresh();e.Handled=true;}else if(e.Key==Key.C&&Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)){copy.RaiseEvent(new RoutedEventArgs(WButton.ClickEvent));e.Handled=true;}};
    }
    async Task Refresh()
    {
        if(busy)return;busy=true;refresh.IsEnabled=false;copy.IsEnabled=false;status.Foreground=UI.Color("#0067D9");status.Text="正在检查，请稍候…";
        try{rows=await load(lifetime.Token);if(closed)return;table.ItemsSource=rows;status.Text=$"{DateTime.Now:HH:mm:ss} 已完成 · {rows.Count} 项结果";status.Foreground=UI.Color("#62656B");copy.IsEnabled=rows.Count>0;}
        catch(Exception e){if(!closed){rows=[];table.ItemsSource=null;status.Foreground=UI.Color("#AC382F");status.Text="检查未完成："+(e is TaskCanceledException?"连接超时，请检查网络并重试。":e.Message);}}
        finally{busy=false;if(!closed)refresh.IsEnabled=true;}
    }
}

public sealed class QuotaWindow:Window
{
    static readonly Dictionary<string,QuotaWindow> windows=[];
    readonly Profile profile;readonly string probeUrl;readonly TextBlock value=UI.Text("正在读取额度…",16,bold:true),detail=UI.Text("",12,"#62656B");readonly DispatcherTimer timer=new();readonly CancellationTokenSource lifetime=new();bool busy,closed;
    public static void Open(Window owner,Profile p,string url){if(windows.TryGetValue(p.Id,out var current)){current.Activate();return;}var w=new QuotaWindow(p,url){Owner=owner};windows[p.Id]=w;w.Closed+=(_,_)=>windows.Remove(p.Id);w.Show();}
    QuotaWindow(Profile p,string url)
    {
        Style=(Style)System.Windows.Application.Current.FindResource(typeof(Window));profile=p;probeUrl=url;Title=p.Name+" · 额度";Width=340;Height=245;MinWidth=300;MinHeight=220;Topmost=true;ShowInTaskbar=false;WindowStyle=WindowStyle.ToolWindow;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        WindowSizing.Prepare(this);
        var root=new StackPanel{Margin=new Thickness(19)};root.Children.Add(value);detail.Margin=new Thickness(0,10,0,12);root.Children.Add(detail);root.Children.Add(UI.Button("刷新额度",async()=>await Refresh()));Content=new ScrollViewer{Content=root};
        timer.Interval=TimeSpan.FromMinutes(1);timer.Tick+=async(_,_)=>await Refresh();Loaded+=async(_,_)=>{timer.Start();await Refresh();};Closed+=(_,_)=>{closed=true;timer.Stop();lifetime.Cancel();};PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }
    async Task Refresh()
    {
        if(busy)return;busy=true;detail.Text="正在核验出口并读取额度…";
        try
        {
            var text=await QuotaService.Read(profile,probeUrl,lifetime.Token);
            if(!closed){value.Text=text;detail.Text=$"{DateTime.Now:HH:mm:ss} 更新 · 每分钟自动刷新";}
        }
        catch(Exception e){if(!closed){value.Text="额度暂不可用";detail.Text=e is IOException?e.Message:e is FileNotFoundException?"找不到 auth.json，请编辑线路并检查登录目录。":"读取未完成，请检查登录状态和网络后重试。";}}
        finally{busy=false;}
    }
}
