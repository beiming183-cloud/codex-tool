using System.IO;
using System.Net;
using System.Text.Json;

namespace CodexToolbox;
public static class SelfTest
{
    public static int Run()
    {
        var checks=new List<string>();
        void Check(bool pass,string name){if(!pass)throw new Exception(name);checks.Add(name);}
        void Reject(Action action,string name){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,name);}
        try
        {
            Check(Updates.ParseVersion("v2.10.0")>Updates.ParseVersion("v2.9.9"),"update versions compare numerically");
            Check(Updates.ParseVersion("v2.4.0-beta")==null&&Updates.ParseVersion("../../2.4.0")==null,"update ignores prerelease and malformed version tags");
            Check(Updates.ValidAssetUrl("https://github.com/beiming183-cloud/codex-tool/releases/download/v2.4.0/a.zip","v2.4.0")&&!Updates.ValidAssetUrl("https://example.com/beiming183-cloud/codex-tool/releases/download/v2.4.0/a.zip","v2.4.0"),"update download stays within the configured repository");
            var releaseFixture=JsonSerializer.Serialize(new{draft=false,prerelease=false,tag_name="v9.0.0",assets=new[]{new{name="CodexToolbox-9.0.0-win-x64.zip",browser_download_url="https://github.com/beiming183-cloud/codex-tool/releases/download/v9.0.0/CodexToolbox-9.0.0-win-x64.zip",size=12345,digest="sha256:"+new string('a',64)}}});
            Check(Updates.ParseRelease(releaseFixture) is {Version.Major:9,Sha256.Length:64},"release metadata selects the exact Windows package and digest");
            Check(Updates.ParseRelease(releaseFixture.Replace("\"prerelease\":false","\"prerelease\":true"))==null,"prerelease is never offered as a stable update");
            Reject(()=>Updates.ParseRelease(releaseFixture.Replace("https://github.com/","https://example.com/")),"foreign release asset rejected");
            Check(Updates.Checksum(new string('a',64)+"  package.zip\n","package.zip")==new string('A',64),"release checksum manifest parsed by exact filename");
            Reject(()=>Updates.Checksum(new string('a',64)+"  package.zip\n"+new string('b',64)+"  package.zip","package.zip"),"ambiguous checksums rejected");
            var updateDir=Path.Combine(Path.GetTempPath(),"CodexToolbox-plan-test","updates",Guid.NewGuid().ToString("N"));var plan=new UpdatePlan{Target=Path.Combine(Path.GetTempPath(),"CodexToolbox.exe"),Candidate=Path.Combine(updateDir,"candidate.exe"),DataDirectory=Path.GetDirectoryName(Path.GetDirectoryName(updateDir))!,Version="2.4.0"};
            Check(UpdateInstaller.ValidPlan(plan,updateDir),"update plan allows a known staging directory on another drive");plan.Candidate=Path.Combine(Path.GetTempPath(),"outside.exe");Check(!UpdateInstaller.ValidPlan(plan,updateDir),"update plan cannot substitute an outside executable");
            Check(Guard.Matches("WHUT-A","WHUT-*")&&!Guard.Matches("Home","WHUT-*"),"campus exact scope");
            Check(Guard.Matches("school.5G","school.?G")&&!Guard.Matches("schoolX5G","school.?G"),"campus escaped pattern");
            Reject(()=>Guard.Validate(new CampusPolicy{Networks=["*"]},false),"reject blanket network restriction");
            Reject(()=>Guard.Validate(new CampusPolicy{Programs=["powershell.exe"]},false),"reject relative executable");
            var campusPolicy=new CampusPolicy();
            Check(CampusDiscovery.Evaluate([new NetworkFact{Name="eduroam"}],campusPolicy).IsCampus,"academic network auto recognition");
            Check(CampusDiscovery.Evaluate([new NetworkFact{Name="Ethernet",DnsSuffix="net.example.edu.cn"}],campusPolicy).IsCampus,"wired education DNS recognition");
            Check(CampusDiscovery.Evaluate([new NetworkFact{Name="Student-WiFi"}],campusPolicy).Kind=="Candidate","weak campus name asks for confirmation");
            Check(!CampusDiscovery.Evaluate([new NetworkFact{Name="Home-WiFi"}],campusPolicy).ShouldBlock(campusPolicy),"ordinary home stays online");
            Check(CampusDiscovery.Evaluate([],campusPolicy).IsUnknown,"missing network never becomes confirmed ordinary");
            Check(CampusDiscovery.Evaluate([new NetworkFact{Name="Network 2",Adapter="Wi-Fi"}],campusPolicy).IsUnknown,"unreadable Wi-Fi SSID is not declared ordinary");
            Check(!CampusDiscovery.Evaluate([new NetworkFact{Name="Student-WiFi"}],campusPolicy).ShouldBlock(campusPolicy),"candidate name does not automatically block");
            campusPolicy.OrdinaryNetworks=["eduroam"];Check(!CampusDiscovery.Evaluate([new NetworkFact{Name="eduroam"}],campusPolicy).IsCampus,"ordinary confirmation overrides auto feature");
            campusPolicy.Networks=["MySchool-*"];Check(CampusDiscovery.Evaluate([new NetworkFact{Name="MySchool-5G"}],campusPolicy).IsCampus,"custom school works without hardcoded campus");
            campusPolicy.OrdinaryNetworks.Add("MySchool-Guest");Check(!CampusDiscovery.Evaluate([new NetworkFact{Name="MySchool-Guest 2",Ssid="MySchool-Guest"}],campusPolicy).IsCampus,"ordinary exemption overrides broad campus rule on same interface");
            Check(CampusDiscovery.Evaluate([new NetworkFact{Name="Campus-Wifi",Ssid="Campus-Wifi"},new NetworkFact{Name="VPN"}],new CampusPolicy()).RememberNames.SequenceEqual(["Campus-Wifi"]),"remembering Wi-Fi does not remember unrelated VPN profiles");
            Guard.Validate(new CampusPolicy(),true);checks.Add("automatic guard requires no hardcoded networks or paths");
            var executableAcl=Guard.InstalledFileSecurity();Check(executableAcl.AreAccessRulesProtected&&executableAcl.GetAccessRules(true,false,typeof(System.Security.Principal.SecurityIdentifier)).Cast<System.Security.AccessControl.FileSystemAccessRule>().Where(r=>r.IdentityReference.Value=="S-1-5-32-545").All(r=>(r.FileSystemRights&System.Security.AccessControl.FileSystemRights.WriteData)==0),"installed SYSTEM executable grants ordinary users no write access");
            var one=new ProcessFact{Pid=10,Name="ChatGPT.exe",Path=@"C:\Apps\Codex\ChatGPT.exe"};var backend=new ProcessFact{Pid=11,Parent=10,Name="codex.exe"};var child=new ProcessFact{Pid=12,Parent=10,Name="ChatGPT.exe",Child=true};
            Check(AutoInspector.FindRoots([one,backend,child]).Count==1,"one desktop with backend remains one instance");
            var second=new ProcessFact{Pid=20,Name="ChatGPT.exe",Path=@"C:\Apps\Codex\ChatGPT.exe"};Check(AutoInspector.FindRoots([one,backend,second]).Count==2,"two desktops are compared independently");
            var cli=new ProcessFact{Pid=30,Name="codex.exe"};Check(AutoInspector.FindRoots([one,backend,cli]).Count==2,"unrelated CLI is not guessed as a desktop account");
            Check(AutoInspector.FindRoots([null!,new ProcessFact{Pid=40,Name=null!,Path=null!,Scope=null!},one]).Count==1,"unreadable process properties do not break automatic discovery");
            Check(AutoInspector.NormalizeProxy("socks5://127.0.0.1:8500")=="socks5://127.0.0.1:8500","automatic proxy parsing with arbitrary port");
            Check(!AutoInspector.LocalController("http://192.0.2.10:9090")&&AutoInspector.LocalController("http://127.0.0.1:9090"),"remote controller PID cannot be treated as a local application PID");
            var stable=new RouteProbe{Ips=["192.0.2.1","192.0.2.1","192.0.2.1"],Attempts=3};var other=new RouteProbe{Ips=["192.0.2.2"],Attempts=1};
            Check(AutoInspector.Judge(3,0,1,false,[stable]).Tone=="good","no fixed expected IP required for observed consistency");
            Check(AutoInspector.Judge(0,0,0,false,[stable]).Tone!="good","proxy samples without requests never prove model route");
            Check(AutoInspector.Judge(3,0,1,false,[stable,other]).Tone=="bad","same instance multiple proxy exits detected");
            Check(AutoInspector.Judge(3,0,2,true,[stable]).Tone=="warn","multiple terminal names are not falsely called multiple IPs");
            Check(AutoInspector.Judge(3,0,1,false,[new RouteProbe{Ips=["192.0.2.1","192.0.2.2"]}]).Tone=="warn","short-term rotating IP distinguished from concurrent exits");
            stable.ChangedBefore=true;Check(AutoInspector.Judge(3,0,1,false,[stable]).Tone=="warn","historical dynamic IP change is not a hard failure");stable.ChangedBefore=false;
            Check(AutoInspector.Judge(3,0,1,false,[stable,new RouteProbe{Ips=["2001:db8::1"]}]).Tone=="good","IPv4 and IPv6 dual stack not mistaken for unstable multi IP");
            Check(AutoInspector.Judge(3,1,1,false,[stable]).Tone!="good","unmapped request prevents full current consistency verdict");
            var terminalMap=AutoInspector.TerminalIdentities("proxies:\n  - server: host.example\n    password: 'a:b'\n    port: 9000\n    type: http\n    name: h\n  - type: socks5\n    name: s\n    server: host.example\n    port: 9000\n    password: 'a:b'\n  - name: different\n    type: http\n    server: host.example\n    port: 9000\n    password: other\nproxy-groups:\n");
            Check(terminalMap["h"]==terminalMap["s"]&&terminalMap["h"]!=terminalMap["different"],"HTTP and SOCKS compare terminal identity without exposing credentials");
            var s=new Settings{Profiles=Enumerable.Range(1,5).Select(i=>new Profile{Name="线路"+i,ExpectedIp=i==5?"2001:db8::1":"",Proxy="socks5://127.0.0.1:"+(7900+i)}).ToList()};
            Check(AdaptiveColumnsPanel.ColumnCount(320)==1&&AdaptiveColumnsPanel.ColumnCount(739)==1&&AdaptiveColumnsPanel.ColumnCount(740)==2&&AdaptiveColumnsPanel.ColumnCount(1140)==3,"responsive cards switch columns at available logical width");
            s.ZoomPercent=75;Store.Validate(s);s.ZoomPercent=150;Store.Validate(s);s.ZoomPercent=151;Reject(()=>Store.Validate(s),"unsupported zoom rejected");s.ZoomPercent=100;
            Store.Validate(s);Check(s.Profiles.Count==5,"more than two profiles");
            Reject(()=>Store.ValidateProxy("http://user:password@127.0.0.1:7890",false),"reject credentials in proxy URL");
            Reject(()=>Store.ValidateProxy("file:///test",false),"reject unsupported scheme");
            s.Profiles[0].ExpectedIp="invalid";Reject(()=>Store.Validate(s),"invalid IP rejected");s.Profiles[0].ExpectedIp="";
            s.Profiles[0].AuthHome=@"C:\private-auth";s.LegacyRoot=@"C:\private-scripts";s.SecretProtected=LocalSecret.Protect("test-secret-value");
            Check(LocalSecret.Unprotect(s.SecretProtected)=="test-secret-value"&&!s.SecretProtected.Contains("test-secret-value"),"DPAPI round trip");
            var share=Store.ShareCopy(s);Check(share.SecretProtected==""&&share.LegacyRoot==""&&share.Profiles.All(p=>p.AuthHome==""),"export strips private auth integration and secret");
            var temp=Path.Combine(Path.GetTempPath(),"CodexToolbox-Test-"+Guid.NewGuid().ToString("N"));Store.DirectoryPath=temp;
            try{Store.Save(s);Check(Store.Load().Profiles.Count==5,"config persistence");s.RefreshSeconds=60;Store.Save(s);Check(File.Exists(Store.FilePath+".bak")&&Store.Load().RefreshSeconds==60,"atomic save backup");File.WriteAllText(Store.FilePath,"broken");Reject(()=>Store.Load(),"corrupt config preserved");Check(File.ReadAllText(Store.FilePath)=="broken","does not overwrite corrupt settings");}
            finally{Directory.Delete(temp,true);}
            var homes=Path.Combine(Path.GetTempPath(),"CodexToolbox-Homes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(homes);
            try
            {
                var normal=Path.Combine(homes,"default");var custom=Path.Combine(homes,"custom");Directory.CreateDirectory(normal);Directory.CreateDirectory(custom);File.WriteAllText(Path.Combine(normal,"auth.json"),"{}");
                Check(CodexDiscovery.ResolveHome("",normal,[],"")==normal,"automatically finds default login without asking for executable path");
                Check(CodexDiscovery.ResolveHome(custom,normal,[],"")=="","custom instance never borrows default account login");
                File.WriteAllText(Path.Combine(custom,"auth.json"),"{}");Check(CodexDiscovery.ResolveHome(custom,normal,[],"")==custom,"custom instance uses its own login");
                File.Delete(Path.Combine(custom,"auth.json"));var manual=new[]{new Profile{AuthHome=normal,Proxy="http://127.0.0.1:8765"}};
                Check(CodexDiscovery.ResolveHome(custom,normal,manual,"http://127.0.0.1:8766")=="","unmatched proxy does not borrow another account login");
                Check(CodexDiscovery.ResolveHome(custom,normal,manual,"http://127.0.0.1:8765")==normal,"explicit matching local integration can locate login");
                Check(QuotaService.Script("<script>never HTML</script>").Contains("textContent=")&&!QuotaService.Script("x").Contains("innerHTML"),"quota rendering uses text only");
            }
            finally{Directory.Delete(homes,true);}
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"CodexToolbox-selftest.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks},Store.Json));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(Path.GetTempPath(),"CodexToolbox-selftest.json"),JsonSerializer.Serialize(new{Passed=false,Checks=checks,Error=e.Message},Store.Json));return 1;}
    }
}
