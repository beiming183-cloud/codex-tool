using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace CodexToolbox;
public sealed partial class MainWindow
{
    readonly AutoInspector inspector=new();AutoReport? autoReport;
    StackPanel? autoHost,autoSummary;TextBlock? campusRecognition,campusEvidence,campusDetails;StackPanel? campusMarkActions;
    CampusAssessment? campusAssessment;bool campusReading;
    void BuildAutoOverview()
    {
        autoHost=new StackPanel();content.Children.Add(autoHost);PaintAutoOverview();
    }
    void PaintAutoOverview()
    {
        if(autoHost==null||page!=0)return;autoHost.Children.Clear();
        autoSummary=new StackPanel();autoHost.Children.Add(autoSummary);
        var top=new Grid();top.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});top.ColumnDefinitions.Add(new(){Width=GridLength.Auto});top.Children.Add(UI.Text("连接概览",18,"#23354D",true));
        var badge=new Border{Background=UI.Color("#E2EBFC"),CornerRadius=new CornerRadius(12),Padding=new Thickness(10,3,10,3),Child=UI.Text("自动更新",12,"#245DD7")};Grid.SetColumn(badge,1);top.Children.Add(badge);autoSummary.Children.Add(top);
        var intro=UI.Text(autoReport==null?"正在查找 Codex 并检查连接…":!autoReport.DiscoveryAvailable?"暂时无法读取 Codex，请稍后重试。":autoReport.Instances.Count==0?"尚未发现正在运行的 Codex":$"已发现 {autoReport.Instances.Count} 个 Codex，分别检查各自的连接。",13,"#62656B");intro.Margin=new Thickness(0,9,0,9);autoSummary.Children.Add(intro);
        if(autoReport!=null)
        {
            var cards=new AdaptiveColumnsPanel{Margin=new Thickness(0,12,0,20)};autoHost.Children.Add(cards);
            int number=0;foreach(var item in autoReport.Instances)
            {
                var row=new StackPanel();row.Children.Add(UI.Text(autoReport.Instances.Count==1?"Codex":$"Codex {++number}",18,"#23354D",true));
                var kind=UI.Text(item.Desktop?"桌面应用":"命令行 / 后台",12,"#53647C");kind.Margin=new Thickness(0,4,0,18);row.Children.Add(kind);
                var verdict=item.Verdict.Contains("TCP")?"还有连接尚未确认":item.Verdict=="当前观测一致"?"当前连接一致":item.Verdict;
                var state=UI.Text(verdict,13,item.Tone switch{"good"=>"#247043","bad"=>"#AC382F","warn"=>"#805812",_=>"#53647C"},true);AutomationProperties.SetName(state,"自动核验结论："+item.Verdict);
                row.Children.Add(new Border{Background=UI.Color(item.Tone switch{"good"=>"#E8F5EC","bad"=>"#FDEDEB","warn"=>"#FBF2DF",_=>"#EFF3F9"}),CornerRadius=new CornerRadius(9),Padding=new Thickness(10,6,10,6),Child=state,HorizontalAlignment=HorizontalAlignment.Left,MaxWidth=600});
                var ips=item.Probes.SelectMany(p=>p.Ips).Distinct().ToArray();var label=UI.Text("代理出口采样",12,"#53647C");label.Margin=new Thickness(0,20,0,5);row.Children.Add(label);
                var ip=UI.Text(ips.Length>0?string.Join("\n",ips):"等待采样",20,"#23354D",true);ip.Cursor=System.Windows.Input.Cursors.Hand;ip.ToolTip="点击复制出口 IP";ip.MouseLeftButtonUp+=(_,_)=>{if(ips.Length>0)UI.Copy(string.Join(Environment.NewLine,ips),Feedback);};row.Children.Add(ip);
                var card=UI.Group(row);card.Margin=new Thickness(0);cards.Children.Add(card);
            }
            if(autoReport.Instances.Count==0){var empty=UI.Text("打开 Codex 后会自动检查；一个窗口、多个窗口和 CLI 都可以。无需填写目标 IP。",14);empty.Margin=new Thickness(0,9,0,12);autoHost.Children.Add(empty);}
        }
        var detail=UI.Button("查看检查详情",()=>OpenTool("auto"),id:"AutoDetails");detail.HorizontalAlignment=HorizontalAlignment.Left;detail.Margin=new Thickness(0,18,0,0);autoHost.Children.Add(detail);
        var scope=UI.Text("不同账号可以有不同 IP。代理采样不能代表每条模型请求。",12,"#53647C");scope.Margin=new Thickness(0,14,0,22);autoHost.Children.Add(scope);
        UpdateResponsiveLayout();
    }
    async Task RefreshCampusRecognition()
    {
        if(campusReading||closing)return;campusReading=true;
        try
        {
            var found=await CampusDiscovery.Inspect(settings.Campus,lifetime.Token);campusAssessment=found;
            if(page!=2||campusRecognition==null||campusEvidence==null)return;
            campusRecognition.Text=found.Title;campusRecognition.Foreground=UI.Color(found.Kind=="Campus"?"#805812":"#202124");
            campusEvidence.Text="当前网络："+(found.Names.Count>0?string.Join(" / ",found.Names):"暂不可读")+"\n"+(found.Kind=="Ordinary"?"如果这是学校网络，可以在下方记为校园网。":found.Kind=="Candidate"?"需要你确认一次，之后会记住。":found.Kind=="Unknown"?"暂时无法确认网络，请稍后重新识别。":"已识别为校园网。");
            if(campusDetails!=null)campusDetails.Text=string.Join("\n",found.Evidence);
            if(campusMarkActions!=null)campusMarkActions.IsEnabled=found.Names.Count>0;
        }
        catch{if(page==2&&campusRecognition!=null){campusRecognition.Text="当前网络暂不可读取";if(campusEvidence!=null)campusEvidence.Text="请重新识别；无法确认时不会显示为普通网络。";}campusAssessment=null;}
        finally{campusReading=false;}
    }
}
