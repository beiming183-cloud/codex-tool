using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CodexToolbox;

public sealed partial class MainWindow
{
    Border updateBanner=null!;Grid updateBannerLayout=null!;readonly TextBlock updateTitle=UI.Text("",14,"#245DD7",true);
    readonly WrapPanel updateActions=new();Button updateButton=null!,updateNotes=null!,updateDismiss=null!;
    TextBlock? updateStatus;Button? checkUpdatesButton;
    ReleaseUpdate? availableUpdate;CancellationTokenSource? updateDownload;bool checkingUpdates;string updateMessage="尚未检查更新";
    readonly DispatcherTimer updateTimer=new(){Interval=TimeSpan.FromHours(6)};
    void BuildUpdateBanner(Grid right)
    {
        updateBannerLayout=new Grid();updateBannerLayout.RowDefinitions.Add(new(){Height=GridLength.Auto});updateBannerLayout.RowDefinitions.Add(new(){Height=GridLength.Auto});updateBannerLayout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});updateBannerLayout.ColumnDefinitions.Add(new(){Width=GridLength.Auto});updateTitle.VerticalAlignment=VerticalAlignment.Center;updateBannerLayout.Children.Add(updateTitle);
        updateButton=UI.Button("下载更新",async()=>{if(updateDownload!=null){updateDownload.Cancel();return;}await InstallUpdate();},true,"DownloadUpdate");
        updateNotes=UI.Button("说明",()=>OpenProject(availableUpdate?.PageUrl??Updates.Repository+"/releases"),id:"UpdateNotes");
        updateDismiss=UI.Button("稍后",()=>updateBanner.Visibility=Visibility.Collapsed,id:"DismissUpdate");
        foreach(var b in new[]{updateButton,updateNotes,updateDismiss}){b.MinHeight=36;b.Padding=new Thickness(12,5,12,5);b.Margin=new Thickness(0,0,6,0);updateActions.Children.Add(b);}Grid.SetColumn(updateActions,1);updateBannerLayout.Children.Add(updateActions);
        updateBanner=new Border{Background=UI.Color("#E5EEFF"),BorderBrush=UI.Color("#CEDDF8"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(14),Margin=new Thickness(0,0,0,18),Child=updateBannerLayout,Visibility=Visibility.Collapsed};AutomationProperties.SetAutomationId(updateBanner,"UpdateBanner");right.Children.Add(updateBanner);
    }
    void StartUpdates()
    {
        updateTimer.Tick+=async(_,_)=>{if(settings.CheckUpdatesOnStartup)await CheckForUpdates(false);};updateTimer.Start();
        Closed+=(_,_)=>{updateTimer.Stop();updateDownload?.Cancel();};
        _=CheckAfterStartup();
    }
    async Task CheckAfterStartup(){try{await Task.Delay(2000,lifetime.Token);UpdateInstaller.Cleanup();if(settings.CheckUpdatesOnStartup)await CheckForUpdates(false);}catch(OperationCanceledException){}}
    void BuildUpdateSettings()
    {
        var body=new StackPanel();body.Children.Add(UI.Text("版本与更新",17,"#23354D",true));
        var version=UI.Text("当前版本 "+Updates.CurrentLabel+" · Windows x64",13,"#53647C");version.Margin=new Thickness(0,8,0,6);body.Children.Add(version);
        updateStatus=UI.Text(updateMessage,12,"#53647C");AutomationProperties.SetAutomationId(updateStatus,"UpdateStatus");body.Children.Add(updateStatus);
        var automatic=new CheckBox{Content="启动时自动检查新版",IsChecked=settings.CheckUpdatesOnStartup};automatic.Click+=(_,_)=>{var previous=settings.CheckUpdatesOnStartup;settings.CheckUpdatesOnStartup=automatic.IsChecked==true;try{Save();Feedback(settings.CheckUpdatesOnStartup?"自动检查已开启，发现新版会在顶部提示。":"自动检查已关闭，可随时手动检查。");}catch{settings.CheckUpdatesOnStartup=previous;automatic.IsChecked=previous;Feedback("更新设置未保存，请重试。");}};body.Children.Add(automatic);
        checkUpdatesButton=UI.Button(checkingUpdates?"正在检查…":"检查更新",async()=>await CheckForUpdates(true),id:"CheckUpdates");checkUpdatesButton.IsEnabled=!checkingUpdates;
        var controls=new WrapPanel{Margin=new Thickness(0,8,0,0)};controls.Children.Add(checkUpdatesButton);controls.Children.Add(UI.Button("项目主页",()=>OpenProject(Updates.Repository),id:"ProjectHome"));body.Children.Add(controls);content.Children.Add(UI.Group(body));
    }
    static void OpenProject(string url)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});
    void UpdateStatus(string message){updateMessage=message;if(updateStatus!=null)updateStatus.Text=message;}
    async Task CheckForUpdates(bool manual)
    {
        if(checkingUpdates||closing)return;checkingUpdates=true;if(checkUpdatesButton!=null){checkUpdatesButton.IsEnabled=false;checkUpdatesButton.Content="正在检查…";}
        UpdateStatus("正在检查 GitHub 上的正式版本…");
        try
        {
            var release=await Updates.Check(manual,lifetime.Token);if(closing)return;
            if(release!=null&&release.Version>Updates.CurrentVersion)
            {availableUpdate=release;updateTitle.Text="发现新版 v"+release.Version.ToString(3);updateBanner.Visibility=Visibility.Visible;UpdateStatus("新版 v"+release.Version.ToString(3)+" 已发布，可在顶部下载更新。");UpdateResponsiveLayout();}
            else{availableUpdate=null;updateBanner.Visibility=Visibility.Collapsed;UpdateStatus(release==null?"暂未发布可安装的正式版本，可在项目主页查看进度。":"已是最新版本 · "+DateTime.Now.ToString("HH:mm")+" 检查");}
            if(manual)Feedback(updateMessage);
        }
        catch(Exception e){if(!closing){UpdateStatus(e is IOException?e.Message:"暂时无法连接 GitHub，请检查系统代理或稍后重试。");if(manual)Feedback(updateMessage);}}
        finally{checkingUpdates=false;if(checkUpdatesButton!=null){checkUpdatesButton.IsEnabled=true;checkUpdatesButton.Content="检查更新";}}
    }
    async Task InstallUpdate()
    {
        if(availableUpdate==null||updateDownload!=null)return;
        updateDownload=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);updateNotes.IsEnabled=updateDismiss.IsEnabled=false;updateButton.Content="取消";
        try
        {
            var release=availableUpdate;var progress=new Progress<int>(n=>{updateTitle.Text=$"正在下载新版 · {n}%";});
            var candidate=await Updates.Download(release,progress,updateDownload.Token);updateDownload.Token.ThrowIfCancellationRequested();updateTitle.Text="文件已校验，正在准备更新…";
            var plan=await Task.Run(()=>UpdateInstaller.StagePlan(candidate,release),updateDownload.Token);updateDownload.Token.ThrowIfCancellationRequested();
            UpdateInstaller.Launch(plan);Close();
        }
        catch(OperationCanceledException){if(!closing){updateTitle.Text="更新已取消，当前版本保留";Feedback("已取消更新，稍后可以重新下载。");}}
        catch(Exception e){if(!closing){updateTitle.Text="更新未完成，可重新尝试";Feedback(e is IOException?e.Message:"下载未完成，请检查网络后重试。当前版本已保留。");}}
        finally{updateDownload?.Dispose();updateDownload=null;if(!closing){updateNotes.IsEnabled=updateDismiss.IsEnabled=true;updateButton.Content="重新下载";}}
    }
}
