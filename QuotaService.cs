using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace CodexToolbox;

public static class QuotaService
{
    public static string Identity(string home)
    {
        var file=Path.Combine(home,"auth.json");if(new FileInfo(file).Length>1024*1024)throw new IOException("登录文件异常。");
        using var j=JsonDocument.Parse(File.ReadAllText(file));return TokenIdentity(j.RootElement.GetProperty("tokens"));
    }
    static string TokenIdentity(JsonElement tokens)
    {
        var account=tokens.TryGetProperty("account_id",out var a)?a.GetString()??"":"";var subject="";
        if(tokens.TryGetProperty("id_token",out var id)&&id.GetString() is {Length:>0} jwt)
        {
            try{var payload=jwt.Split('.')[1].Replace('-','+').Replace('_','/');payload=payload.PadRight((payload.Length+3)/4*4,'=');using var j=JsonDocument.Parse(Convert.FromBase64String(payload));if(j.RootElement.TryGetProperty("sub",out var sub))subject=sub.GetString()??"";}catch{}
        }
        if(account.Length==0&&subject.Length==0)subject=tokens.TryGetProperty("access_token",out var access)?access.GetString()??"":"";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account+"\n"+subject)));
    }
    public static async Task<string> Read(Profile profile,string probeUrl,CancellationToken ct)
    {
        var result=await NetworkService.Probe(profile,probeUrl,ct);
        if(!result.Good)throw new IOException("线路暂不可用，请先在首页重新检查。");
        var file=Path.Combine(profile.AuthHome,"auth.json");
        if(!File.Exists(file))throw new IOException("尚未找到登录状态，请在 Codex 中登录后重新查找。");
        if(new FileInfo(file).Length>1024*1024)throw new IOException("登录文件异常，请在 Codex 中重新登录。");
        using var auth=JsonDocument.Parse(await File.ReadAllTextAsync(file,ct));
        if(!auth.RootElement.TryGetProperty("tokens",out var tokens)||!tokens.TryGetProperty("access_token",out var tokenElement)||tokenElement.GetString() is not {Length:>0} token)
            throw new IOException("此登录方式暂不支持额度查询，请使用 ChatGPT 账号登录 Codex。");
        if(profile.QuotaIdentity.Length>0&&profile.QuotaIdentity!=TokenIdentity(tokens))throw new IOException("Codex 登录账号已变化，请重新查找后添加浮层。");
        using var client=NetworkService.Client(profile.Proxy);
        using var req=new HttpRequestMessage(HttpMethod.Get,"https://chatgpt.com/backend-api/wham/usage");req.Headers.Authorization=new("Bearer",token);
        if(tokens.TryGetProperty("account_id",out var account)&&account.GetString() is {Length:>0} id)req.Headers.Add("ChatGPT-Account-Id",id);
        using var response=await client.SendAsync(req,ct);
        if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized)throw new IOException("登录已过期，请在 Codex 中重新登录后重试。");
        if(!response.IsSuccessStatusCode)throw new IOException($"额度服务暂不可用（{(int)response.StatusCode}），请稍后重试。");
        var body=await response.Content.ReadAsStringAsync(ct);if(body.Length>128*1024)throw new IOException("额度服务响应异常，请稍后重试。");
        using var j=JsonDocument.Parse(body);
        if(!j.RootElement.TryGetProperty("rate_limit",out var limits))throw new IOException("额度服务格式已变化，请更新工具箱后重试。");
        string Window(string name,string label){if(!limits.TryGetProperty(name,out var w)||w.ValueKind==JsonValueKind.Null||!w.TryGetProperty("used_percent",out var percent))return label+"：暂不可用";return label+"：剩余 "+Math.Clamp(100-percent.GetDouble(),0,100).ToString("0.#")+"%";}
        return Window("primary_window","5 小时")+"\n"+Window("secondary_window","每周");
    }
    public static async Task<string> Evaluate(int port,string expression,CancellationToken ct)
    {
        using var bounded=CancellationTokenSource.CreateLinkedTokenSource(ct);bounded.CancelAfter(TimeSpan.FromSeconds(6));ct=bounded.Token;
        using var client=NetworkService.Client("");
        var raw=await client.GetStringAsync($"http://127.0.0.1:{port}/json/list",ct);
        if(raw.Length>512*1024)throw new IOException("Codex 窗口信息过大，请重试。");
        using var targets=JsonDocument.Parse(raw);
        var pages=targets.RootElement.EnumerateArray().Where(t=>t.TryGetProperty("type",out var type)&&type.GetString() is "page" or "webview"&&t.TryGetProperty("url",out var u)&&u.GetString() is string url&&url.StartsWith("app://-/index.html",StringComparison.Ordinal)&&!url.Contains("avatar-overlay")).ToArray();
        if(pages.Length!=1)throw new IOException("尚未找到唯一的 Codex 主界面，请完成启动后重试。");
        var address=pages[0].GetProperty("webSocketDebuggerUrl").GetString();
        if(!Uri.TryCreate(address,UriKind.Absolute,out var uri)||uri.Scheme!="ws"||uri.Host!="127.0.0.1"&&uri.Host!="localhost"||uri.Port!=port)throw new IOException("Codex 窗口连接信息不匹配，请重新查找。");
        using var socket=new ClientWebSocket();socket.Options.Proxy=null;await socket.ConnectAsync(uri,ct);
        var command=JsonSerializer.SerializeToUtf8Bytes(new{id=1,method="Runtime.evaluate",@params=new{expression,returnByValue=true}});
        await socket.SendAsync(command,WebSocketMessageType.Text,true,ct);
        var buffer=new byte[16384];using var message=new MemoryStream();
        while(true)
        {
            var part=await socket.ReceiveAsync(buffer,ct);if(part.MessageType==WebSocketMessageType.Close)throw new IOException("Codex 窗口连接已关闭，请重试。");
            message.Write(buffer,0,part.Count);if(message.Length>1024*1024)throw new IOException("Codex 窗口响应异常，请重试。");
            if(!part.EndOfMessage)continue;
            using var j=JsonDocument.Parse(message.ToArray());message.SetLength(0);
            if(!j.RootElement.TryGetProperty("id",out var id)||id.GetInt32()!=1)continue;
            if(j.RootElement.TryGetProperty("error",out _)||j.RootElement.GetProperty("result").TryGetProperty("exceptionDetails",out _))throw new IOException("浮层未能添加，请重试或使用独立浮窗。");
            return j.RootElement.GetProperty("result").GetProperty("result").TryGetProperty("value",out var v)?v.ToString():"";
        }
    }
    public static string Script(string text,bool remove=false)=>"""
        (()=>{
          const key='codex-toolbox-quota';let el=document.getElementById(key);
          if(REMOVE){el?.remove();return 'removed';}
          if(window.__codexQuotaOverlay)return 'existing';
          if(window.__codexToolboxQuotaDismissed)return 'dismissed';
          if(!el){el=document.createElement('aside');el.id=key;el.setAttribute('aria-label','Codex 额度');
            el.style.cssText='position:fixed;right:20px;top:52px;z-index:2147483000;padding:12px 16px;border:1px solid #c9cbd1;border-radius:12px;background:#fff;color:#202124;font:13px/1.65 "Segoe UI","Microsoft YaHei UI",sans-serif;box-shadow:0 6px 20px #0002;max-width:300px;white-space:pre-line';
            const label=document.createElement('div');label.dataset.quota='value';el.append(label);
            const close=document.createElement('button');close.textContent='隐藏';close.style.cssText='border:0;background:none;color:#0067d9;padding:6px 0 0;cursor:pointer;font:inherit';close.onclick=()=>{window.__codexToolboxQuotaDismissed=true;el.remove()};el.append(close);document.body.append(el);}
          el.querySelector('[data-quota="value"]').textContent=TEXT;
          return 'attached';
        })()
        """.Replace("REMOVE",remove?"true":"false").Replace("TEXT",JsonSerializer.Serialize(text));
}
