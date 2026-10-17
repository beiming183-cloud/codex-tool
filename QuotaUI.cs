using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CodexToolbox;

public sealed partial class MainWindow
{
    CodexDiscoveryResult? quotaDiscovery;StackPanel? quotaHost;bool findingCodex;
    readonly Dictionary<int,DispatcherTimer> quotaTimers=[];
    readonly Dictionary<int,string> quotaStates=[];
    readonly HashSet<int> quotaBusy=[];
    void BuildQuota()
    {
        subtitle.Text="自动查找 Codex，选择窗口即可添加。";
        actions.Children.Add(UI.Button("重新查找",async()=>await FindQuota(),id:"FindCodex"));
        quotaHost=new StackPanel();content.Children.Add(quotaHost);PaintQuota();
        var help=new StackPanel();help.Children.Add(UI.Text("不需要填写安装路径",16,bold:true));
        var note=UI.Text("先打开 Codex 并登录。支持连接的窗口可直接添加额度；其他窗口会使用独立浮窗。工具箱保持打开时，每分钟更新一次。",13,"#62656B");note.Margin=new Thickness(0,9,0,0);help.Children.Add(note);
        content.Children.Add(UI.Group(help));
        var advanced=new StackPanel();advanced.Children.Add(UI.Text("只有自定义启动器未提供登录位置时才需要手动选择。选择包含 auth.json 的目录，不需要寻找 Codex.exe。",12,"#62656B"));
        var routeLabel=UI.Text("此账号的连接方式",13,"#62656B");routeLabel.Margin=new Thickness(0,12,0,6);advanced.Children.Add(routeLabel);
        var route=new ComboBox{MinHeight=36,Margin=new Thickness(0,0,0,12)};AutomationProperties.SetName(route,"此账号的连接方式");
        route.Items.Add(new ComboBoxItem{Content="请选择连接方式",Tag=null});route.Items.Add(new ComboBoxItem{Content="本机直连",Tag=""});
        foreach(var p in settings.Profiles.Where(p=>p.Enabled))route.Items.Add(new ComboBoxItem{Content=p.Name+" · "+p.Proxy,Tag=p.Proxy});
        foreach(var p in (autoReport?.Instances.SelectMany(i=>i.Probes).Select(p=>p.Proxy).Distinct()??[]).Where(p=>p.Length>0&&!settings.Profiles.Any(s=>s.Proxy==p)))route.Items.Add(new ComboBoxItem{Content="自动发现 · "+p,Tag=p});
        route.SelectedIndex=0;advanced.Children.Add(route);
        advanced.Children.Add(UI.Button("选择登录目录…",()=>
        {
            if(route.SelectedItem is not ComboBoxItem{Tag:string selectedProxy}){Feedback("请先选择此账号的连接方式。");return;}
            var folder=new Microsoft.Win32.OpenFolderDialog{Title="选择 Codex 登录目录（包含 auth.json）"};
            if(folder.ShowDialog(this)!=true)return;
            if(!File.Exists(Path.Combine(folder.FolderName,"auth.json"))){Feedback("此目录没有登录状态。请先在 Codex 中登录，或重新选择目录。");return;}
            // The user explicitly selects a login for a standalone window; do not assign it to an arbitrary Codex process.
            QuotaWindow.Open(this,new Profile{Id="manual-quota-"+folder.FolderName,Name="所选账号",AuthHome=folder.FolderName,Proxy=selectedProxy},settings.ProbeUrl);
        },id:"ChooseQuotaHome"));
        content.Children.Add(UI.Fold("找不到账号？",advanced));
        if(quotaDiscovery==null)_=FindQuota();
    }
    void PaintQuota()
    {
        if(page!=1||quotaHost==null)return;quotaHost.Children.Clear();
        var body=new StackPanel();
        if(quotaDiscovery==null){body.Children.Add(UI.Text(findingCodex?"正在查找 Codex…":"暂未完成查找",18,bold:true));body.Children.Add(UI.Text("自动识别正在运行的窗口与登录位置。",13,"#62656B"));}
        else if(quotaDiscovery.Instances.Count==0)
        {
            body.Children.Add(UI.Text("先打开 Codex",20,bold:true));
            var t=UI.Text(quotaDiscovery.InstalledPath.Length>0?"已找到安装位置。打开 Codex 并登录后，点击“重新查找”。":"打开并登录 Codex 后，点击“重新查找”。便携版和自定义位置也会按运行窗口识别。",13,"#62656B");t.Margin=new Thickness(0,10,0,0);body.Children.Add(t);
        }
        else
        {
            var cards=new AdaptiveColumnsPanel{Margin=new Thickness(0,0,0,20)};
            foreach(var i in quotaDiscovery.Instances)
            {
            var cardBody=new StackPanel();
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var left=new StackPanel();left.Children.Add(UI.Text(i.Label,17,bold:true));
            var state=UI.Text(quotaStates.GetValueOrDefault(i.Pid)??(i.RouteProblem.Length>0?i.RouteProblem:i.Ready?"已找到登录状态":"尚未找到此窗口的登录状态"),13,"#62656B");state.Margin=new Thickness(0,7,16,0);left.Children.Add(state);row.Children.Add(left);
            var b=UI.Button(quotaBusy.Contains(i.Pid)?"正在添加…":quotaTimers.ContainsKey(i.Pid)?"刷新额度":"添加浮层",async()=>await AttachQuota(i),true,"AttachQuota-"+i.Pid);b.IsEnabled=!quotaBusy.Contains(i.Pid)&&(i.Ready||i.Port>0);b.Margin=new Thickness(0);b.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(b,1);row.Children.Add(b);cardBody.Children.Add(row);
            var detail=new StackPanel();detail.Children.Add(UI.Text("程序位置："+i.Path+"\n登录位置："+(i.Home.Length>0?i.Home:"未匹配；请先登录，或展开下方帮助手动选择。"),12,"#62656B"));
            var exp=UI.Fold("位置与兼容性",detail);exp.Margin=new Thickness(0,18,0,0);cardBody.Children.Add(exp);
            var card=UI.Group(cardBody);card.Margin=new Thickness(0);cards.Children.Add(card);
            }
            quotaHost.Children.Add(cards);return;
        }
        quotaHost.Children.Add(UI.Group(body));
    }
    async Task FindQuota()
    {
        if(findingCodex||closing)return;findingCodex=true;PaintQuota();Feedback("正在自动查找 Codex…");
        try
        {
            var found=await CodexDiscovery.Find(settings,lifetime.Token);
            // Prefer the observed per-process route when no explicit proxy argument exists.
            foreach(var i in found.Instances)
            {
                var routes=autoReport?.Instances.FirstOrDefault(r=>r.Pid==i.Pid)?.Probes.Where(p=>!p.ReferenceOnly).Select(p=>p.Proxy).Distinct().ToArray();
                if(routes?.Length==1){i.Proxy=routes[0];i.RouteProblem="";}
                else if(routes?.Length>1)i.RouteProblem="此窗口存在多条线路，请先在首页确认后重试。";
            }
            quotaDiscovery=found;Feedback(found.Instances.Count>0?$"已找到 {found.Instances.Count} 个 Codex 窗口，无需填写安装路径。":"打开 Codex 并登录后，点击重新查找。");
        }
        catch{Feedback("暂时无法读取 Codex 窗口，请稍后重新查找。");}
        finally{findingCodex=false;PaintQuota();}
    }
    async Task AttachQuota(CodexInstance i)
    {
        if(closing||!quotaBusy.Add(i.Pid))return;PaintQuota();
        try
        {
            if(i.RouteProblem.Length>0)throw new IOException(i.RouteProblem);
            using var process=System.Diagnostics.Process.GetProcessById(i.Pid);
            if(process.HasExited||!string.Equals(process.MainModule?.FileName,i.Path,StringComparison.OrdinalIgnoreCase))throw new IOException("此窗口已关闭，请重新查找 Codex。");
            if(i.Port<=0)
            {
                QuotaWindow.Open(this,i.Profile,settings.ProbeUrl);quotaStates[i.Pid]="已打开独立浮窗；此窗口暂不支持直接添加。";return;
            }
            var facts=await AutoInspector.Snapshot(lifetime.Token);
            if(!facts.Listeners.Any(s=>s.Pid==i.Pid&&s.LocalPort==i.Port))throw new IOException("窗口连接尚未准备好，请稍后重试。");
            var previous=await QuotaService.Evaluate(i.Port,"window.__codexQuotaOverlay ? 'existing' : 'ready'",lifetime.Token);
            if(previous=="existing"){quotaStates[i.Pid]="额度浮层已在此窗口显示，无需重复添加。";return;}
            if(!i.Ready)throw new IOException("尚未找到此窗口的登录状态，请先登录，或展开下方帮助选择。");
            var value=await QuotaService.Read(i.Profile,settings.ProbeUrl,lifetime.Token);
            var outcome=await QuotaService.Evaluate(i.Port,"window.__codexToolboxQuotaDismissed=false;"+QuotaService.Script(i.Label+"\n"+value+"\n"+DateTime.Now.ToString("HH:mm")+" 更新 · 保持工具箱打开"),lifetime.Token);
            quotaStates[i.Pid]=outcome=="attached"?"已添加到 Codex，每分钟自动更新。":"额度浮层已存在。";
            if(outcome=="attached"&&!quotaTimers.ContainsKey(i.Pid))
            {
                var timer=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};timer.Tick+=async(_,_)=>await UpdateQuota(i,timer);quotaTimers[i.Pid]=timer;timer.Start();
            }
        }
        catch(Exception e){quotaStates[i.Pid]=e is IOException?e.Message:"添加未完成，请检查登录和网络后重试。";Feedback(quotaStates[i.Pid]);}
        finally{quotaBusy.Remove(i.Pid);PaintQuota();}
    }
    async Task UpdateQuota(CodexInstance i,DispatcherTimer timer)
    {
        if(closing){timer.Stop();return;}if(!quotaBusy.Add(i.Pid))return;
        try
        {
            using var p=System.Diagnostics.Process.GetProcessById(i.Pid);if(p.HasExited){timer.Stop();return;}
            var value=await QuotaService.Read(i.Profile,settings.ProbeUrl,lifetime.Token);
            var state=await QuotaService.Evaluate(i.Port,QuotaService.Script(i.Label+"\n"+value+"\n"+DateTime.Now.ToString("HH:mm")+" 更新 · 保持工具箱打开"),lifetime.Token);
            if(state is "dismissed" or "existing"){timer.Stop();quotaTimers.Remove(i.Pid);quotaStates[i.Pid]="已隐藏，点击添加可再次显示。";}
            else quotaStates[i.Pid]="已更新 · "+DateTime.Now.ToString("HH:mm:ss");
        }
        catch{quotaStates[i.Pid]="自动更新未完成，点击刷新额度重试。";try{await QuotaService.Evaluate(i.Port,QuotaService.Script("额度暂不可用\n请回工具箱检查登录与网络。"),lifetime.Token);}catch{timer.Stop();quotaTimers.Remove(i.Pid);}}
        finally{quotaBusy.Remove(i.Pid);PaintQuota();}
    }
}
