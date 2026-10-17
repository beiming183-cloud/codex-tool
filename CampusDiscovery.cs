using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexToolbox;

public sealed class NetworkFact
{
    public string Name { get; set; }="";
    public string Ssid { get; set; }="";
    public string DnsSuffix { get; set; }="";
    public string Adapter { get; set; }="";
}
public record CampusAssessment(string Kind,string Title,List<string> Names,List<string> Evidence)
{
    public List<string> RememberNames {get;init;}=[];
    public bool IsCampus=>Kind=="Campus";
    public bool IsUnknown=>Kind=="Unknown";
    public bool ShouldBlock(CampusPolicy policy)=>IsCampus||(IsUnknown&&policy.BlockOnUnknown);
}
public static class CampusDiscovery
{
    public static async Task<List<NetworkFact>> Read(CancellationToken ct=default)
    {
        var code="""
        $wireless=@();$interfaceGuid=''
        netsh wlan show interfaces | ForEach-Object {if($_ -match '^\s*GUID\s*:\s*(.+)$'){$interfaceGuid=$Matches[1].Trim().Trim('{}')}elseif($_ -match '^\s*SSID\s*:\s*(.+)$'){$wireless+=@{Guid=$interfaceGuid;Ssid=$Matches[1].Trim()}}}
        $adapters=@(Get-NetAdapter -IncludeHidden -ErrorAction SilentlyContinue)
        $dns=@(Get-DnsClient -ErrorAction SilentlyContinue)
        $facts=@(Get-NetConnectionProfile -ErrorAction SilentlyContinue | ForEach-Object {
            $p=$_;$suffix=($dns|Where-Object {$_.InterfaceIndex -eq $p.InterfaceIndex}|Select-Object -First 1).ConnectionSpecificSuffix
            $adapter=$adapters|Where-Object {$_.ifIndex -eq $p.InterfaceIndex}|Select-Object -First 1
            $ssid=($wireless|Where-Object {$_.Guid -and $_.Guid -eq ([string]$adapter.InterfaceGuid).Trim('{}')}|Select-Object -First 1).Ssid
            @{Name=[string]$p.Name;Ssid=[string]$ssid;DnsSuffix=[string]$suffix;Adapter=[string]$p.InterfaceAlias}
        })
        foreach($wifi in $wireless){if(-not @($facts|Where-Object {$_.Ssid -eq $wifi.Ssid}).Count){$facts+=@{Name=$wifi.Ssid;Ssid=$wifi.Ssid;DnsSuffix='';Adapter='Wi-Fi'}}}
        ConvertTo-Json -InputObject @($facts) -Compress
        """;
        return JsonSerializer.Deserialize<List<NetworkFact>>(await Runner.PowerShell(code,15,ct),Store.Json)??[];
    }
    public static CampusAssessment Evaluate(IEnumerable<NetworkFact> source,CampusPolicy policy)
    {
        var facts=source.ToList();var names=facts.SelectMany(f=>new[]{f.Ssid,f.Name}).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var wireless=facts.Where(f=>!string.IsNullOrWhiteSpace(f.Ssid)).SelectMany(f=>new[]{f.Ssid,f.Name}).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var remember=wireless.Count>0?wireless:names;
        CampusAssessment Result(string kind,string title,List<string> evidence)=>new(kind,title,names,evidence){RememberNames=remember};
        if(names.Count==0)return new("Unknown","无法识别当前网络",[],["没有可读取的网络配置或 Wi-Fi 名称。"]);
        var campus=new List<string>();var candidates=new List<string>();var ordinary=new List<string>();
        foreach(var f in facts)
        {
            var local=new[]{f.Name,f.Ssid}.Where(n=>!string.IsNullOrWhiteSpace(n)).ToArray();
            if(local.Any(n=>policy.OrdinaryNetworks.Any(p=>Guard.Matches(n,p)))){ordinary.Add("已记为普通网络："+f.Name);continue;}
            if(local.Any(n=>policy.Networks.Any(p=>Guard.Matches(n,p)))){campus.Add("命中已确认校园规则："+string.Join(" / ",local.Distinct()));continue;}
            if(!policy.AutoDetect)continue;
            if(local.Any(n=>string.Equals(n,"eduroam",StringComparison.OrdinalIgnoreCase)))campus.Add("识别到 eduroam 教育科研漫游网络（按网络名称判断）。");
            else if(Regex.IsMatch(f.DnsSuffix.TrimEnd('.'),@"(?:^|\.)(?:edu(?:\.[a-z]{2})?|ac\.(?:uk|jp|kr|nz|za))$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))campus.Add("网络 DNS 后缀属于教育域："+f.DnsSuffix);
            else if(local.Any(n=>Regex.IsMatch(n,@"校园|大学|学院|学校|(?:^|[-_\s])(?:campus|university|college|student|edu)(?:$|[-_\s])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)))candidates.Add("名称含校园特征，需确认："+f.Name);
        }
        if(campus.Count>0)return Result("Campus","已识别教育 / 校园网络",campus);
        if(facts.Any(f=>string.IsNullOrWhiteSpace(f.Ssid)&&Regex.IsMatch(f.Adapter??"",@"Wi-?Fi|WLAN|无线",RegexOptions.IgnoreCase)&&!policy.OrdinaryNetworks.Any(p=>Guard.Matches(f.Name,p))))return Result("Unknown","Wi-Fi 名称暂不可读取",["无法读取 SSID，不能当作已确认的普通网络；可检查 Windows 位置访问权限，或确认当前 Windows 网络名称。"]);
        if(candidates.Count>0)return Result("Candidate","发现疑似校园网",candidates.Concat(["仅名称特征不会自动阻断；确认一次后会记住此网络。 "]).ToList());
        return Result("Ordinary","未发现校园网特征",ordinary.Count>0?ordinary:[policy.AutoDetect?"当前名称和网络 DNS 后缀未命中教育网络特征；无法据此排除所有校园网。":"自动识别已关闭，当前未命中已确认的校园规则。"]);
    }
    public static async Task<CampusAssessment> Inspect(CampusPolicy policy,CancellationToken ct=default)=>Evaluate(await Read(ct),policy);
    public static async Task<List<string>> DetectPrograms(CancellationToken ct=default)
    {
        var code="""
        ConvertTo-Json -InputObject @((Get-CimInstance Win32_Process | Where-Object {$_.Name -match '^(?:verge-(?:quality-)?mihomo|mihomo|clash-meta|clash|sing-box|xray|v2ray)\.exe$'} | Select-Object -ExpandProperty ExecutablePath | Where-Object {$_ -and (Test-Path -LiteralPath $_ -PathType Leaf)} | Sort-Object -Unique)) -Compress
        """;
        return JsonSerializer.Deserialize<List<string>>(await Runner.PowerShell(code,15,ct),Store.Json)??[];
    }
}
