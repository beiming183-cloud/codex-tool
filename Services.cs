using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexToolbox;

public record ResultRow(string Name,string Value,string State="已读取");
public record ProbeResult(string Ip,string State,string Detail,long Milliseconds,bool Good);
public record CoreInfo(string Name,string Config,string Pipe,string Http,string Secret,int Pid=0,string Executable="");
public static class Runner
{
    public static async Task<string> Run(string exe,IEnumerable<string> args,int seconds=20,CancellationToken ct=default)
    {
        var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        foreach(var a in args)info.ArgumentList.Add(a);
        using var p=Process.Start(info)??throw new IOException("无法启动检查程序。");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
        try{await p.WaitForExitAsync(timeout.Token);}catch{try{p.Kill(true);}catch{}throw new TimeoutException("检查超时或已取消，请重试。");}
        var o=await output;var e=await error;
        if(p.ExitCode!=0)throw new IOException(string.IsNullOrWhiteSpace(e)?"检查程序返回错误。":e[..Math.Min(e.Length,400)]);
        return o.Trim();
    }
    public static Task<string> PowerShell(string code,int seconds=20,CancellationToken ct=default)=> Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),["-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';try{"+code+"}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}"))],seconds,ct);
    public static string Quote(string s)=>"'"+s.Replace("'","''")+"'";
}
public static class NetworkService
{
    public static HttpClient Client(string proxy)
    {
        var handler=new SocketsHttpHandler{UseProxy=!string.IsNullOrWhiteSpace(proxy),ConnectTimeout=TimeSpan.FromSeconds(4),AllowAutoRedirect=false};
        if(handler.UseProxy)handler.Proxy=new WebProxy(proxy);
        return new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10)};
    }
    public static async Task<ProbeResult> Probe(Profile p,string url,CancellationToken ct=default,int timeoutSeconds=10)
    {
        var watch=Stopwatch.StartNew();
        using var bounded=CancellationTokenSource.CreateLinkedTokenSource(ct);bounded.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));var requestToken=bounded.Token;
        try
        {
            using var client=Client(p.Proxy);client.Timeout=TimeSpan.FromSeconds(timeoutSeconds);using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,requestToken);response.EnsureSuccessStatusCode();
            await using var body=await response.Content.ReadAsStreamAsync(requestToken);var buffer=new byte[16385];int used=0,n;
            while(used<buffer.Length && (n=await body.ReadAsync(buffer.AsMemory(used),requestToken))>0)used+=n;
            if(used==buffer.Length)throw new InvalidDataException("检测接口响应过大。");
            var text=Encoding.UTF8.GetString(buffer,0,used).Trim();
            var ip=Regex.Match(text,@"(?m)^ip=([^\r\n]+)").Groups[1].Value.Trim();
            if(ip.Length==0 && IPAddress.TryParse(text,out _))ip=text;
            if(ip.Length==0){try{using var j=JsonDocument.Parse(text);ip=j.RootElement.GetProperty("ip").GetString()??"";}catch{}}
            if(!IPAddress.TryParse(ip,out var address))throw new InvalidDataException("接口未返回 IP；请使用 Cloudflare trace、纯 IP 或 {ip:...} 接口。");
            bool match=p.ExpectedIp.Length==0 || address.Equals(IPAddress.Parse(p.ExpectedIp));
            return new(ip,match?(p.ExpectedIp.Length>0?"出口符合预期":"已实测") : "出口不符",match?$"{watch.ElapsedMilliseconds} ms · 仅验证此代理出口":$"预期 {p.ExpectedIp} · 请检查线路选择",watch.ElapsedMilliseconds,match);
        }
        catch(Exception e) when(e is not OutOfMemoryException)
        {return new("—","连接失败",e is TaskCanceledException?"请求超时，请检查代理地址和 Clash 是否运行。":e is HttpRequestException?"无法通过此代理完成请求，请检查端口、网络和 Clash。":e.Message,watch.ElapsedMilliseconds,false);}
    }
    public static async Task<List<string>> NetworkNames(CancellationToken ct=default)
    {
        var code="@((Get-NetConnectionProfile -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name); (netsh wlan show interfaces | ForEach-Object {if($_ -match '^\\s*SSID\\s*:\\s*(.+)$'){$Matches[1].Trim()}})) | Sort-Object -Unique | ConvertTo-Json -Compress";
        var raw=await Runner.PowerShell(code,12,ct);if(string.IsNullOrWhiteSpace(raw))return [];
        using var j=JsonDocument.Parse(raw);return j.RootElement.ValueKind==JsonValueKind.Array?j.RootElement.EnumerateArray().Select(x=>x.GetString()??"").Where(x=>x.Length>0).ToList():[j.RootElement.GetString()??""];
    }
    public static async Task<CoreInfo> DetectCore(CancellationToken ct=default)
    {
        // Read the configuration of the live core, not a remembered installation directory.
        var code="""
        function Arg($c,$n){$m=[regex]::Match($c,'(?:^|\s)'+[regex]::Escape($n)+'(?:\s+|=)(?:"([^"]+)"|''([^'']+)''|(\S+))');if($m.Success){foreach($i in 1..3){if($m.Groups[$i].Success){return $m.Groups[$i].Value}}}}
        $cores=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -match '^(verge-(quality-)?mihomo|mihomo|clash-meta|clash)\.exe$'})
        $desktop=@($cores | Where-Object {$_.Name -match '^verge-(quality-)?mihomo\.exe$'})
        if($desktop.Count -eq 1){$cores=$desktop}
        if($cores.Count -ne 1){throw '无法自动确定唯一的运行内核，请在设置中填写控制接口。'}
        $c=$cores[0];$f=Arg $c.CommandLine '-f';$d=Arg $c.CommandLine '-d';if(-not $f){throw '内核没有公开配置文件位置，请手动填写控制接口。'}
        if(-not [IO.Path]::IsPathRooted($f)){$f=Join-Path $d $f}
        $pipe=Arg $c.CommandLine '-ext-ctl-pipe';$http='';$secret=''
        foreach($line in Get-Content -LiteralPath $f -Encoding UTF8){if($line -match '^secret:\s*(.*?)\s*$'){$secret=$Matches[1].Trim('"',"'")};if(-not $pipe -and $line -match '^external-controller-pipe:\s*(.*?)\s*$'){$pipe=$Matches[1].Trim('"',"'")};if($line -match '^external-controller:\s*(.*?)\s*$'){$http=$Matches[1].Trim('"',"'")}}
        @{Name=$c.Name;Config=$f;Pipe=$pipe;Http=$http;Secret=$secret;Pid=[int]$c.ProcessId;Executable=[string]$c.ExecutablePath}|ConvertTo-Json -Compress
        """;
        return JsonSerializer.Deserialize<CoreInfo>(await Runner.PowerShell(code,15,ct),Store.Json)??throw new IOException("没有读取到内核。");
    }
    public static async Task<JsonDocument> CoreRequest(Settings s,string path,CancellationToken ct=default,CoreInfo? detected=null)
    {
        if(path is not ("/version" or "/proxies" or "/connections" or "/configs"))throw new InvalidOperationException();
        CoreInfo core=s.Controller.Length>0?new("手动控制接口","","",s.Controller,LocalSecret.Unprotect(s.SecretProtected)):detected??await DetectCore(ct);
        if(core.Pipe.Length>0)
        {
            var name=core.Pipe.Replace(@"\\.\pipe\", "");using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(8000);await pipe.ConnectAsync(timeout.Token);
            var request=$"GET {path} HTTP/1.0\r\nHost: localhost\r\nConnection: close\r\n"+(core.Secret.Length>0?$"Authorization: Bearer {core.Secret}\r\n":"")+"\r\n";
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(request),timeout.Token);await pipe.FlushAsync(timeout.Token);
            using var mem=new MemoryStream();var buffer=new byte[16384];int n;
            while((n=await pipe.ReadAsync(buffer,timeout.Token))>0){mem.Write(buffer,0,n);if(mem.Length>12*1024*1024)throw new IOException("内核状态过大。");}
            var raw=Encoding.UTF8.GetString(mem.ToArray());var split=raw.IndexOf("\r\n\r\n",StringComparison.Ordinal);
            if(split<0 || !raw.StartsWith("HTTP/1.0 200") && !raw.StartsWith("HTTP/1.1 200"))throw new IOException("控制接口认证失败，请检查设置中的密钥。");
            return JsonDocument.Parse(raw[(split+4)..]);
        }
        var http=core.Http;if(!http.Contains("://"))http="http://"+http;http=http.Replace("0.0.0.0:","127.0.0.1:");
        using var client=Client("");using var msg=new HttpRequestMessage(HttpMethod.Get,http.TrimEnd('/')+path);
        if(core.Secret.Length>0)msg.Headers.Authorization=new("Bearer",core.Secret);
        using var response=await client.SendAsync(msg,ct);if(!response.IsSuccessStatusCode)throw new IOException($"控制接口返回 {(int)response.StatusCode}，请检查接口地址和密钥。");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public static async Task<List<ResultRow>> CoreStatus(Settings s,CancellationToken ct=default)
    {
        using var version=await CoreRequest(s,"/version",ct);using var proxies=await CoreRequest(s,"/proxies",ct);
        var rows=new List<ResultRow>{new("Clash 控制接口","已连接","正常"),new("内核版本",version.RootElement.TryGetProperty("version",out var v)?v.GetString()??"未知":"未知")};
        foreach(var p in s.Profiles.Where(p=>p.Group.Length>0))
        {
            if(proxies.RootElement.GetProperty("proxies").TryGetProperty(p.Group,out var group))rows.Add(new(p.Name+" · "+p.Group,group.TryGetProperty("now",out var now)?now.GetString()??"未知":group.GetProperty("type").GetString()??"未知","已读取"));
            else rows.Add(new(p.Name,"未找到分组："+p.Group,"待配置"));
        }
        return rows;
    }
    public static async Task<List<ResultRow>> Connections(Settings s,CancellationToken ct=default)
    {
        using var doc=await CoreRequest(s,"/connections",ct);var rows=new List<ResultRow>();
        foreach(var c in doc.RootElement.GetProperty("connections").EnumerateArray())
        {
            var m=c.GetProperty("metadata");var process=m.TryGetProperty("process",out var pr)?pr.GetString()??"":"";
            var host=m.TryGetProperty("host",out var h)?h.GetString()??"":"";
            var network=m.TryGetProperty("network",out var n)?n.GetString()??"":"";
            if(network.ToLowerInvariant()!="tcp")continue;
            if(process.Contains("codex",StringComparison.OrdinalIgnoreCase) || process.Contains("ChatGPT",StringComparison.OrdinalIgnoreCase) || host.Contains("chatgpt",StringComparison.OrdinalIgnoreCase) || host.Contains("openai",StringComparison.OrdinalIgnoreCase))
                rows.Add(new(string.IsNullOrEmpty(process)?host:process,host+"  ·  "+string.Join(" → ",c.GetProperty("chains").EnumerateArray().Select(x=>x.GetString())),"TCP 观测"));
        }
        if(rows.Count==0)rows.Add(new("当前连接","尚未采到 Codex 的 TCP 请求。发送一条消息后重新检查。","尚未确认"));
        return rows;
    }
    public static async Task<List<ResultRow>> Legacy(Settings s,string action,CancellationToken ct=default)
    {
        var script=Path.Combine(s.LegacyRoot,"ToolWorker.ps1");if(!File.Exists(script))throw new IOException("没有配置原有工具目录，请在设置中检查本机集成。");
        var raw=await Runner.PowerShell("& "+Runner.Quote(script)+" -Action "+Runner.Quote(action),45,ct);
        using var j=JsonDocument.Parse(raw);return j.RootElement.GetProperty("Rows").EnumerateArray().Select(r=>new ResultRow(r.GetProperty("Name").GetString()??"",r.GetProperty("Value").GetString()??"",r.GetProperty("State").GetString()??"")).ToList();
    }
}
