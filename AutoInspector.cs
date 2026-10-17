using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodexToolbox;

public sealed class ProcessFact
{
    public int Pid {get;set;} public int Parent {get;set;}
    public string Name {get;set;}=""; public string Path {get;set;}="";
    public string Proxy {get;set;}="";public string Scope {get;set;}="";public bool Child {get;set;}
}
public sealed class SocketFact
{
    public int Pid {get;set;} public int LocalPort {get;set;}public int RemotePort {get;set;}
    public string LocalAddress {get;set;}="";public string RemoteAddress {get;set;}="";
}
public sealed class SystemFacts
{
    public List<ProcessFact> Processes {get;set;}=[];public List<SocketFact> Sockets {get;set;}=[];
    public List<SocketFact> Listeners {get;set;}=[];
}
public sealed class IpObservation
{
    public DateTimeOffset Time {get;set;}=DateTimeOffset.Now;public string Ip {get;set;}="";
}
public sealed class RouteProbe
{
    public string Proxy {get;set;}="";public string Source {get;set;}="";public List<string> Ips {get;set;}=[];
    public int Attempts {get;set;}public int Failed {get;set;}public bool ChangedBefore {get;set;}
    public List<IpObservation> History {get;set;}=[];
    public bool ReferenceOnly {get;set;}
}
public sealed class InstanceReport
{
    public string Name {get;set;}="";public int Pid {get;set;}public bool Desktop {get;set;}
    public string Verdict {get;set;}="";public string Tone {get;set;}="wait";
    public List<string> Evidence {get;set;}=[];public List<string> Routes {get;set;}=[];
    public List<RouteProbe> Probes {get;set;}=[];public int TcpCount {get;set;}public int UnknownTcp {get;set;}
}
public sealed class AutoReport
{
    public DateTimeOffset Time {get;set;}=DateTimeOffset.Now;
    public List<InstanceReport> Instances {get;set;}=[];public List<string> Notes {get;set;}=[];
    public bool DiscoveryAvailable {get;set;}=true;
    public string Core {get;set;}="尚未连接";public int DesktopCount=>Instances.Count(i=>i.Desktop);
    public int BackendCount=>Instances.Count(i=>!i.Desktop);
    public string Summary=>!DiscoveryAvailable?"进程状态暂不可读，自动发现尚未完成。":Instances.Count==0?"尚未发现运行中的 Codex":$"发现 {DesktopCount} 个桌面实例、{BackendCount} 个独立 CLI / 后台；分别核验，不跨账号比较 IP。";
}
public sealed class AutoInspector
{
    readonly SemaphoreSlim gate=new(1,1);
    Dictionary<string,List<IpObservation>> history=[];
    string HistoryPath=>System.IO.Path.Combine(Store.DirectoryPath,"route-observations.json");
    bool loaded;
    AutoReport? last;string lastKey="";
    static string Hash(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public static bool IsDesktop(ProcessFact p)=>p!=null&&!p.Child&&(string.Equals(p.Name,"ChatGPT.exe",StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(p.Path??"",@"OpenAI\.Codex_|[\\/]Codex(?:[\\/._-]|$)",RegexOptions.IgnoreCase)||string.Equals(p.Name,"Codex.exe",StringComparison.OrdinalIgnoreCase)&&p.Scope?.StartsWith("desktop:",StringComparison.Ordinal)==true);
    public static List<ProcessFact> FindRoots(List<ProcessFact> processes)
    {
        processes=processes.Where(p=>p!=null).ToList();var selected=processes.Where(p=>IsDesktop(p)||string.Equals(p.Name,"codex.exe",StringComparison.OrdinalIgnoreCase)&&!p.Child).ToList();
        return selected.Where(p=>!selected.Any(parent=>parent.Pid!=p.Pid&&Descends(p.Pid,parent.Pid,processes))).OrderByDescending(IsDesktop).ThenBy(p=>p.Pid).ToList();
    }
    public static bool Descends(int pid,int ancestor,List<ProcessFact> processes)
    {
        var seen=new HashSet<int>();var byId=processes.ToDictionary(p=>p.Pid);
        while(byId.TryGetValue(pid,out var p)&&seen.Add(pid)){if(pid==ancestor)return true;pid=p.Parent;}return false;
    }
    public static async Task<SystemFacts> Snapshot(CancellationToken ct=default)
    {
        var code="""
        function Arg($c,$n){$m=[regex]::Match($c,'(?:^|\s)'+[regex]::Escape($n)+'(?:\s+|=)(?:"([^"]+)"|''([^'']+)''|(\S+))');if($m.Success){foreach($i in 1..3){if($m.Groups[$i].Success){return $m.Groups[$i].Value}}}}
        $procs=@(Get-CimInstance Win32_Process|ForEach-Object {
            $proxy=Arg $_.CommandLine '--proxy-server';if($proxy -match '@'){$proxy=''}
            $scope=Arg $_.CommandLine '--user-data-dir';$isDesktop=$_.Name -eq 'Codex.exe' -and $_.ExecutablePath -match 'WindowsApps|[\\/]app-[^\\/]+' -and $_.ExecutablePath -notmatch '[\\/]resources[\\/]' -and $_.CommandLine -notmatch '\bapp-server\b|\bexec\b|--cli'
            @{Pid=[int]$_.ProcessId;Parent=[int]$_.ParentProcessId;Name=[string]$_.Name;Path=[string]$_.ExecutablePath;Proxy=[string]$proxy;Scope=$(if($isDesktop){'desktop:'+[string]$scope}else{[string]$scope});Child=($_.CommandLine -match '(?:^|\s)--type=')}
        })
        $tcp=@(Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue|ForEach-Object {@{Pid=[int]$_.OwningProcess;LocalPort=[int]$_.LocalPort;RemotePort=[int]$_.RemotePort;LocalAddress=[string]$_.LocalAddress;RemoteAddress=[string]$_.RemoteAddress}})
        $listeners=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue|ForEach-Object {@{Pid=[int]$_.OwningProcess;LocalPort=[int]$_.LocalPort}})
        @{Processes=$procs;Sockets=$tcp;Listeners=$listeners}|ConvertTo-Json -Depth 5 -Compress
        """;
        return JsonSerializer.Deserialize<SystemFacts>(await Runner.PowerShell(code,22,ct),Store.Json)??new();
    }
    public static string NormalizeProxy(string value)
    {
        value=(value??"").Trim().Trim('"','\'');if(value.Contains(';'))value=value.Split(';').FirstOrDefault(x=>x.StartsWith("https=",StringComparison.OrdinalIgnoreCase))?.Split('=',2)[1]??value.Split(';')[0];
        if(value.StartsWith("https=",StringComparison.OrdinalIgnoreCase)||value.StartsWith("http=",StringComparison.OrdinalIgnoreCase))value=value.Split('=',2)[1];
        if(value.Length==0||value.Contains('@'))return "";
        if(!value.Contains("://"))value="http://"+value;
        try{Store.ValidateProxy(value,false);return new Uri(value).GetLeftPart(UriPartial.Authority);}catch{return "";}
    }
    static bool SameAddress(string a,string b)=>IPAddress.TryParse(a,out var x)&&IPAddress.TryParse(b,out var y)&&x.MapToIPv6().Equals(y.MapToIPv6());
    static string S(JsonElement e,string key)=>e.TryGetProperty(key,out var p)?p.ToString():"";
    static int N(JsonElement e,string key)=>int.TryParse(S(e,key),out int n)?n:0;
    static bool Local(string address)=>IPAddress.TryParse(address,out var ip)&&(IPAddress.IsLoopback(ip)||ip.IsIPv4MappedToIPv6&&IPAddress.IsLoopback(ip.MapToIPv4()));
    public static bool LocalController(string value)=>value.Length==0||Uri.TryCreate(value,UriKind.Absolute,out var u)&&(u.Host.Equals("localhost",StringComparison.OrdinalIgnoreCase)||Local(u.Host.Trim('[',']')));

    // Only a parsed, complete terminal definition can merge differently named nodes.
    // Unresolved/provider/flow-style YAML stays distinct and explicitly uncertain.
    public static Dictionary<string,string> TerminalIdentities(string text)
    {
        var definitions=new List<Dictionary<string,string>>();Dictionary<string,string>? current=null;bool inside=false;int indent=-1;
        string Scalar(string raw){raw=raw.Trim();if(raw.Length>1&&raw[0]=='\'')return raw.Trim('\'').Replace("''","'");if(raw.StartsWith('"')){try{return JsonSerializer.Deserialize<string>(raw)??raw;}catch{return raw;}}return Regex.Replace(raw,@"\s+#.*$","");}
        foreach(var line in text.Split('\n'))
        {
            if(!inside){if(Regex.IsMatch(line,@"^proxies:\s*$"))inside=true;continue;}
            if(Regex.IsMatch(line,@"^[A-Za-z][\w-]*:"))break;
            var item=Regex.Match(line,@"^(\s*)-\s+([\w-]+):\s*(.*?)\s*$");
            if(item.Success&&(indent<0||item.Groups[1].Length==indent)){indent=item.Groups[1].Length;current=new();definitions.Add(current);current[item.Groups[2].Value]=Scalar(item.Groups[3].Value);continue;}
            var field=Regex.Match(line,@"^(\s+)([\w-]+):\s*(.*?)\s*$");if(current!=null&&field.Success&&field.Groups[1].Length==indent+2)current[field.Groups[2].Value]=Scalar(field.Groups[3].Value);
        }
        var map=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var d in definitions)
        {
            if(!d.TryGetValue("name",out var name)||!d.TryGetValue("server",out var server)||!d.TryGetValue("port",out var port)||!int.TryParse(port,out _))continue;
            var type=d.GetValueOrDefault("type","").ToLowerInvariant();
            if(type is not ("http" or "socks5"))continue;
            var identity=JsonSerializer.Serialize(new[]{server.ToLowerInvariant(),port,d.GetValueOrDefault("tls","false"),d.GetValueOrDefault("username",""),d.GetValueOrDefault("password","")});
            // Digests are transient equality keys; never put them in reports/history.
            if(!map.TryAdd(name,Hash(identity)))map.Remove(name);
        }
        return map;
    }
    public static (string Verdict,string Tone) Judge(int tcp,int unknown,int terminals,bool unresolved,IEnumerable<RouteProbe> probes)
    {
        var p=probes.Where(p=>!p.ReferenceOnly).ToList();bool Multiple(IEnumerable<string> ips)=>ips.Where(x=>IPAddress.TryParse(x,out _)).Select(IPAddress.Parse).GroupBy(x=>x.AddressFamily).Any(g=>g.Distinct().Count()>1);
        var current=p.SelectMany(x=>x.Ips).ToList();
        if(p.Count>1&&Multiple(p.SelectMany(x=>x.Ips.TakeLast(1))))return ("检测到多个出口 IP","bad");
        if(p.Any(x=>Multiple(x.Ips)))return ("短时间内出口 IP 有切换","warn");
        if(terminals>1)return (unresolved?"多个代理末端 · 公网 IP 待确认":"请求线路不一致 · 多个末端","warn");
        if(p.Any(x=>x.ChangedBefore))return ("出口 IP 较之前有变化","warn");
        if(unknown>0)return ("存在未确认的 TCP 路径","wait");
        if(tcp==0)return (current.Count>0?"出口采样稳定 · 请求路径待确认":"尚未采到可核验的请求","wait");
        if(p.Count==0||p.Any(x=>x.Failed>0))return ("请求路径已采到 · 出口采样不完整","wait");
        if(terminals==1)return ("当前观测一致","good");
        return ("请求末端尚未确认","wait");
    }
    public async Task<AutoReport> Scan(Settings settings,CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var cacheKey=Hash(settings.ProbeUrl+settings.Controller+settings.SecretProtected);if(last!=null&&lastKey==cacheKey&&last.Time>DateTimeOffset.Now.AddSeconds(-5))return last;
            if(!loaded){try{if(File.Exists(HistoryPath)&&new FileInfo(HistoryPath).Length<5*1024*1024)history=JsonSerializer.Deserialize<Dictionary<string,List<IpObservation>>>(File.ReadAllText(HistoryPath),Store.Json)??[];}catch{}loaded=true;}PruneHistory();
            var report=new AutoReport();var factsTask=Snapshot(ct);CoreInfo? core=null;JsonDocument? connections=null;Dictionary<string,string> identities=[];
            try{core=await NetworkService.DetectCore(ct);report.Core=settings.Controller.Length==0?core.Name:"手动控制接口";connections=await NetworkService.CoreRequest(settings,"/connections",ct,core);if(settings.Controller.Length==0&&File.Exists(core.Config))identities=TerminalIdentities(await File.ReadAllTextAsync(core.Config,ct));}
            catch{try{if(settings.Controller.Length>0){connections=await NetworkService.CoreRequest(settings,"/connections",ct);report.Core="手动控制接口";}}catch{}report.Notes.Add("Clash 连接证据暂不可用时，会继续识别实例和代理，相关结论保持待确认。");}
            SystemFacts facts;try{facts=await factsTask;facts.Processes=(facts.Processes??[]).Where(p=>p!=null&&p.Pid>0).ToList();foreach(var p in facts.Processes){p.Name??="";p.Path??="";p.Scope??="";p.Proxy??="";}facts.Sockets??=[];facts.Listeners??=[];}catch{connections?.Dispose();report.DiscoveryAvailable=false;report.Notes.Add("进程 / TCP 读取失败，请重试；未使用手动配置假装自动发现。");return report;}
            try
            {
                var roots=FindRoots(facts.Processes);var corePids=facts.Processes.Where(p=>Regex.IsMatch(p.Name,@"^(?:verge-(?:quality-)?mihomo|mihomo|clash-meta|clash)\.exe$",RegexOptions.IgnoreCase)).Select(p=>p.Pid).ToHashSet();
                var corePorts=facts.Listeners.Where(s=>corePids.Contains(s.Pid)).Select(s=>s.LocalPort).ToHashSet();
                var rows=connections?.RootElement.TryGetProperty("connections",out var cs)==true?cs.EnumerateArray().Select(x=>x.Clone()).ToList():[];
                var probeCache=new Dictionary<string,Task<RouteProbe>>(StringComparer.OrdinalIgnoreCase);using var probeGate=new SemaphoreSlim(4);var pending=new List<(InstanceReport Instance,int Terminals,bool Unresolved,List<Task<RouteProbe>> Tasks)>();
                foreach(var root in roots)
                {
                    var owned=new HashSet<int>{root.Pid};bool expanded;do{expanded=false;foreach(var p in facts.Processes)if(owned.Contains(p.Parent)&&owned.Add(p.Pid))expanded=true;}while(expanded);
                    var sockets=facts.Sockets.Where(s=>owned.Contains(s.Pid)).ToList();var ownConnections=new List<JsonElement>();
                    foreach(var c in rows)
                    {
                        var m=c.GetProperty("metadata");if(!S(m,"network").Equals("tcp",StringComparison.OrdinalIgnoreCase))continue;
                        var port=N(m,"sourcePort");var address=S(m,"sourceIP");int pid=N(m,"processID");if(pid==0)pid=N(m,"processId");
                        if(LocalController(settings.Controller)&&owned.Contains(pid)||sockets.Any(s=>s.LocalPort==port&&SameAddress(s.LocalAddress,address)))ownConnections.Add(c);
                    }
                    var nodeKeys=new HashSet<string>();var labels=new HashSet<string>();bool unresolved=false;
                    foreach(var c in ownConnections)
                    {
                        var chains=c.TryGetProperty("chains",out var ch)?ch.EnumerateArray().Select(x=>x.GetString()??"").ToArray():[];
                        var terminal=chains.FirstOrDefault(n=>identities.ContainsKey(n));
                        if(terminal!=null){nodeKeys.Add(identities[terminal]);labels.Add(terminal);}
                        else if(chains.Length>0){var leaf=chains[0];nodeKeys.Add("unresolved:"+leaf);labels.Add(leaf);unresolved=true;}
                    }
                    var proxies=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var p in facts.Processes.Where(p=>owned.Contains(p.Pid))){var proxy=NormalizeProxy(p.Proxy);if(proxy.Length>0)proxies.TryAdd(proxy,p.Pid==root.Pid?"界面启动参数":"子进程启动参数");}
                    foreach(var socket in sockets.Where(s=>Local(s.RemoteAddress)&&corePorts.Contains(s.RemotePort)))
                    {
                        var matched=ownConnections.FirstOrDefault(c=>N(c.GetProperty("metadata"),"sourcePort")==socket.LocalPort&&SameAddress(S(c.GetProperty("metadata"),"sourceIP"),socket.LocalAddress));
                        var kind=matched.ValueKind!=JsonValueKind.Undefined?S(matched.GetProperty("metadata"),"type"):"";
                        var scheme=kind.Contains("socks",StringComparison.OrdinalIgnoreCase)?"socks5":"http";
                        proxies.TryAdd(new UriBuilder(scheme,socket.RemoteAddress,socket.RemotePort).Uri.GetLeftPart(UriPartial.Authority),"进程已连接的代理内核端口");
                    }
                    // Avoid duplicating the same numeric listener as HTTP when a SOCKS URL is explicitly known.
                    foreach(var uri in proxies.Keys.Select(p=>new Uri(p)).ToArray())if(uri.Scheme=="socks5"&&Local(uri.Host))foreach(var key in proxies.Keys.Where(k=>new Uri(k).Scheme=="http"&&new Uri(k).Port==uri.Port&&SameAddress(new Uri(k).Host.Trim('[',']'),uri.Host.Trim('[',']'))).ToArray())proxies.Remove(key);
                    var instance=new InstanceReport{Name=IsDesktop(root)?"Codex 桌面实例":"独立 Codex CLI / 后台",Pid=root.Pid,Desktop=IsDesktop(root),Routes=labels.ToList(),TcpCount=ownConnections.Count};
                    var mapped=ownConnections.Select(c=>c.GetProperty("metadata")).Select(m=>(Port:N(m,"sourcePort"),Ip:S(m,"sourceIP"))).ToList();
                    var proxyPorts=proxies.Keys.Select(k=>new Uri(k).Port).ToHashSet();
                    instance.UnknownTcp=sockets.Count(s=>(!Local(s.RemoteAddress)||corePorts.Contains(s.RemotePort)||proxyPorts.Contains(s.RemotePort))&&!mapped.Any(m=>m.Port==s.LocalPort&&SameAddress(m.Ip,s.LocalAddress)));
                    var tasks=new List<Task<RouteProbe>>();foreach(var proxy in proxies.Take(8))
                    {
                        if(!probeCache.TryGetValue(proxy.Key,out var task)){if(probeCache.Count>=12){instance.UnknownTcp++;continue;}task=ProbeRoute(proxy.Key,proxy.Value,settings.ProbeUrl,probeGate,ct);probeCache[proxy.Key]=task;}
                        tasks.Add(task);
                    }
                    instance.Evidence.Add($"已匹配 {instance.TcpCount} 条实际 TCP 连接；{nodeKeys.Count} 个末端身份，{instance.UnknownTcp} 条外连未映射。");
                    if(!instance.Desktop)instance.Evidence.Add("未能通过进程父子关系归属某个桌面窗口，单独列出，不猜测账号归属。");
                    if(tasks.Count==0)instance.Evidence.Add("未发现明确代理地址；可能使用 TUN、环境变量或远程代理，出口 IP 暂不能自动确认。");
                    pending.Add((instance,nodeKeys.Count,unresolved,tasks));
                    report.Instances.Add(instance);
                }
                await Task.WhenAll(probeCache.Values);
                foreach(var work in pending){work.Instance.Probes=work.Tasks.Select(t=>t.Result).ToList();var verdict=Judge(work.Instance.TcpCount,work.Instance.UnknownTcp,work.Terminals,work.Unresolved,work.Instance.Probes);work.Instance.Verdict=verdict.Verdict;work.Instance.Tone=verdict.Tone;if(work.Instance.Probes.Any(p=>p.ChangedBefore))work.Instance.Evidence.Add("历史变化与本轮多出口分开显示；动态 IP 变化本身不判为故障。");}
                report.Notes.Add("代理采样访问检测地址，不等于逐条模型请求的公网 IP；同一末端也可能由服务商轮换出口。只核验已采到的 TCP，不能保证未来流量。");
                PruneHistory();Directory.CreateDirectory(Store.DirectoryPath);var tmp=HistoryPath+"."+Guid.NewGuid().ToString("N")+".tmp";await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(history,Store.Json),ct);File.Move(tmp,HistoryPath,true);
                last=report;lastKey=cacheKey;return report;
            }
            finally{connections?.Dispose();}
        }
        finally{gate.Release();}
    }
    async Task<RouteProbe> ProbeRoute(string proxy,string source,string url,SemaphoreSlim limiter,CancellationToken ct)
    {
        var result=new RouteProbe{Proxy=proxy,Source=source};var key=Hash(proxy+"|"+url);List<IpObservation> old;lock(history)old=history.GetValueOrDefault(key,[]).Where(x=>x.Time>DateTimeOffset.Now.AddMinutes(-30)).ToList();
        await limiter.WaitAsync(ct);
        try
        {
            for(int i=0;i<3;i++)
            {
                ct.ThrowIfCancellationRequested();result.Attempts++;
                var p=await NetworkService.Probe(new Profile{Proxy=proxy},url,ct,4);
                if(!p.Good){result.Failed++;continue;}
                result.Ips.Add(p.Ip);result.History.Add(new(){Ip=p.Ip});
            }
            result.ChangedBefore=old.Any(previous=>result.Ips.Any(ip=>IPAddress.Parse(previous.Ip).AddressFamily==IPAddress.Parse(ip).AddressFamily&&!IPAddress.Parse(previous.Ip).Equals(IPAddress.Parse(ip))));
            lock(history){history[key]=old.Concat(result.History).TakeLast(120).ToList();result.History=history[key].ToList();}return result;
        }
        finally{limiter.Release();}
    }
    void PruneHistory()
    {
        foreach(var key in history.Keys.ToArray()){history[key]=history[key].Where(x=>x.Time>DateTimeOffset.Now.AddMinutes(-30)&&IPAddress.TryParse(x.Ip,out _)).TakeLast(120).ToList();if(history[key].Count==0)history.Remove(key);}
        foreach(var key in history.Keys.Skip(200).ToArray())history.Remove(key);
    }
    public static List<ResultRow> Rows(AutoReport r)
    {
        var rows=new List<ResultRow>{new("自动发现",r.Summary),new("运行内核",r.Core)};
        foreach(var i in r.Instances)
        {
            var label=i.Name+" · PID "+i.Pid;rows.Add(new(label,i.Verdict,"本轮观测"));
            foreach(var e in i.Evidence)rows.Add(new(label,e,"依据"));
            foreach(var route in i.Routes)rows.Add(new(label,route,"实际请求末端"));
            foreach(var p in i.Probes){rows.Add(new(label,p.Proxy+" → "+string.Join(" / ",p.Ips.Distinct())+$" · {p.Attempts} 次，{p.Failed} 次失败",p.ChangedBefore?"历史有变化":"代理采样"));foreach(var h in p.History.TakeLast(12))rows.Add(new(label,$"{h.Time:HH:mm:ss}  {h.Ip}","最近 30 分钟"));}
        }
        foreach(var n in r.Notes)rows.Add(new("观测范围",n,"说明"));return rows;
    }
}
