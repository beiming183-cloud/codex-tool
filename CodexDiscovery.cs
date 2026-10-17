using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace CodexToolbox;

public sealed class CodexInstance
{
    public int Pid {get;set;} public int Port {get;set;}
    public string Path {get;set;}=""; public string UserData {get;set;}="";
    public string Proxy {get;set;}=""; public string Home {get;set;}="";
    public string RouteProblem {get;set;}="";
    public string Label {get;set;}="";
    [JsonIgnore] public string Identity {get;set;}="";
    public bool Ready=>Home.Length>0&&File.Exists(System.IO.Path.Combine(Home,"auth.json"));
    [JsonIgnore] public Profile Profile=>new(){Id="auto-quota-"+Pid,Name=Label,Proxy=Proxy,AuthHome=Home,QuotaIdentity=Identity};
}
public sealed class CodexDiscoveryResult
{
    public List<CodexInstance> Instances {get;set;}=[];
    public string InstalledPath {get;set;}="";
}
public static class CodexDiscovery
{
    public static string WindowsProxy(string raw)
    {
        if(!raw.Contains('='))return AutoInspector.NormalizeProxy(raw);
        var parts=raw.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(s=>s.Split('=',2)).Where(p=>p.Length==2).ToArray();
        return AutoInspector.NormalizeProxy(parts.FirstOrDefault(p=>p[0].Equals("https",StringComparison.OrdinalIgnoreCase))?.Last()??parts.FirstOrDefault(p=>p[0].Equals("http",StringComparison.OrdinalIgnoreCase))?.Last()??"");
    }
    public static string ResolveHome(string userData,string defaultHome,IEnumerable<Profile> profiles,string proxy)
    {
        // A custom Electron directory is not automatically an authentication directory.
        if(userData.Length>0)return File.Exists(System.IO.Path.Combine(userData,"auth.json"))?userData:ExplicitHome(profiles,proxy);
        return File.Exists(System.IO.Path.Combine(defaultHome,"auth.json"))?defaultHome:ExplicitHome(profiles,proxy);
    }
    static string ExplicitHome(IEnumerable<Profile> profiles,string proxy)
    {
        var matches=profiles.Where(p=>p.Enabled&&p.AuthHome.Length>0&&p.Proxy==proxy).Select(p=>p.AuthHome).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return matches.Length==1&&File.Exists(System.IO.Path.Combine(matches[0],"auth.json"))?matches[0]:"";
    }
    static string CockpitHome(string userData)
    {
        if(userData.Length==0)return "";
        try
        {
            var root=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".antigravity_cockpit");
            var file=System.IO.Path.Combine(root,"codex_instances.json");if(!File.Exists(file)||new FileInfo(file).Length>1024*1024)return "";
            using var j=JsonDocument.Parse(File.ReadAllText(file));if(!j.RootElement.TryGetProperty("instances",out var instances))return "";
            var homes=new List<string>();
            foreach(var item in instances.EnumerateArray())
            {
                if(!item.TryGetProperty("userDataDir",out var field)||field.GetString() is not {Length:>0} home||!System.IO.Path.IsPathFullyQualified(home))continue;
                home=System.IO.Path.GetFullPath(home).TrimEnd('\\');
                var hash=Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes((@"\\?\"+home).ToLowerInvariant()))).ToLowerInvariant();
                var web=System.IO.Path.Combine(root,"instances","codex-app-data",hash);
                if((string.Equals(home,userData.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||string.Equals(web,userData.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))&&File.Exists(System.IO.Path.Combine(home,"auth.json")))homes.Add(home);
            }
            return homes.Distinct(StringComparer.OrdinalIgnoreCase).Count()==1?homes[0]:"";
        }
        catch{return "";}
    }
    public static async Task<CodexDiscoveryResult> Find(Settings settings,CancellationToken ct)
    {
        var json=await Runner.PowerShell("""
        $ErrorActionPreference='Stop'
        function Flag($s,$key){$m=[regex]::Match($s,'(?:^|\s)--'+[regex]::Escape($key)+'(?:=|\s+)(?:"([^"]+)"|([^\s]+))');if($m.Success){if($m.Groups[1].Success){return $m.Groups[1].Value};return $m.Groups[2].Value};return ''}
        $items=@(Get-CimInstance Win32_Process -Filter "Name='ChatGPT.exe' OR Name='Codex.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine -notmatch '(?:^|\s)--type(?:=|\s)' -and $_.ExecutablePath -notmatch '(?i)[\\/](?:resources|node_modules|vendor)[\\/]' -and $_.ExecutablePath -match '(?i)OpenAI\.Codex_|[\\/]Codex(?:[\\/._-]|$)' -and ($_.Name -ieq 'ChatGPT.exe' -or (Flag $_.CommandLine 'remote-debugging-port') -or (Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue).MainWindowHandle)} | ForEach-Object {
            [pscustomobject]@{Pid=[int]$_.ProcessId;Port=if((Flag $_.CommandLine 'remote-debugging-port') -match '^\d+$'){[int](Flag $_.CommandLine 'remote-debugging-port')}else{0};Path=[string]$_.ExecutablePath;UserData=(Flag $_.CommandLine 'user-data-dir');Proxy=(Flag $_.CommandLine 'proxy-server')}
        })
        $installed='';if($items.Count -gt 0){$installed=$items[0].Path}else{$pkg=Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction SilentlyContinue | Select-Object -First 1;if($pkg){foreach($rel in @('app\ChatGPT.exe','app\Codex.exe','Codex.exe')){$candidate=Join-Path $pkg.InstallLocation $rel;if(Test-Path -LiteralPath $candidate){$installed=$candidate;break}}}}
        $internet=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction SilentlyContinue
        [pscustomobject]@{Instances=$items;InstalledPath=$installed;WindowsProxy=if($internet.ProxyEnable -eq 1){[string]$internet.ProxyServer}else{''};Pac=[bool]$internet.AutoConfigURL}|ConvertTo-Json -Depth 4 -Compress
        """,ct:ct);
        var found=JsonSerializer.Deserialize<CodexDiscoveryResult>(json,Store.Json)??new();
        using var system=JsonDocument.Parse(json);var windowsProxy=system.RootElement.GetProperty("WindowsProxy").GetString()??"";var pac=system.RootElement.GetProperty("Pac").GetBoolean();
        var defaultHome=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
        // CODEX_HOME belongs to this toolbox's environment, not necessarily to another Codex process.
        foreach(var i in found.Instances)
        {
            var raw=i.Proxy;i.Proxy=AutoInspector.NormalizeProxy(raw);
            if(raw.Length>0&&i.Proxy.Length==0)i.RouteProblem="此窗口的代理参数暂不支持，请先在首页检查线路。";
            else if(raw.Length==0){i.Proxy=WindowsProxy(windowsProxy);if(pac||windowsProxy.Length>0&&i.Proxy.Length==0)i.RouteProblem="尚未确认此窗口的代理线路，请先在首页检查后重新查找。";}
            i.Home=ResolveHome(i.UserData,defaultHome,settings.Profiles,i.Proxy);
            if(i.Home.Length==0)i.Home=CockpitHome(i.UserData);
            if(i.Ready)try{i.Identity=QuotaService.Identity(i.Home);}catch{}
        }
        found.Instances=found.Instances.OrderBy(i=>i.Pid).ToList();
        for(int n=0;n<found.Instances.Count;n++)found.Instances[n].Label=found.Instances.Count==1?"Codex":"Codex 窗口 "+(n+1);
        return found;
    }
}
