using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexToolbox;

public sealed record ReleaseUpdate(Version Version,string Tag,string PageUrl,string AssetName,string AssetUrl,long Size,string Sha256,string ChecksumUrl);
public sealed record ReleaseCache(DateTimeOffset CheckedUtc,ReleaseUpdate? Release);
public static class Updates
{
    public const string Repository="https://github.com/beiming183-cloud/codex-tool";
    public const string Api="https://api.github.com/repos/beiming183-cloud/codex-tool/releases/latest";
    public static Version CurrentVersion {get;}=Normalize(Assembly.GetExecutingAssembly().GetName().Version!);
    public static string CurrentLabel=>CurrentVersion.ToString(3);
    public static Version Normalize(Version value)=>new(value.Major,value.Minor,Math.Max(0,value.Build));
    public static Version? ParseVersion(string tag)=>Regex.IsMatch(tag,@"^v?\d{1,5}\.\d{1,5}\.\d{1,5}$")&&Version.TryParse(tag.TrimStart('v'),out var v)?Normalize(v):null;
    public static bool ValidAssetUrl(string value,string tag)
        =>Uri.TryCreate(value,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.Host=="github.com"&&u.Port==443&&u.UserInfo.Length==0&&u.Fragment.Length==0&&u.AbsolutePath.StartsWith("/beiming183-cloud/codex-tool/releases/download/"+tag+"/",StringComparison.Ordinal);
    public static bool ValidRelease(ReleaseUpdate release)=>ParseVersion(release.Tag)==release.Version&&release.AssetName=="CodexToolbox-"+release.Version.ToString(3)+"-win-x64.zip"&&ValidAssetUrl(release.AssetUrl,release.Tag)&&release.PageUrl==Repository+"/releases/tag/"+release.Tag&&release.Size is >0 and <=250L*1024*1024&&(Regex.IsMatch(release.Sha256,@"^[a-fA-F0-9]{64}$")||release.Sha256.Length==0&&ValidAssetUrl(release.ChecksumUrl,release.Tag));
    public static ReleaseUpdate? ParseRelease(string text)
    {
        using var doc=JsonDocument.Parse(text);var j=doc.RootElement;
        if(j.GetProperty("draft").GetBoolean()||j.GetProperty("prerelease").GetBoolean())return null;
        var tag=j.GetProperty("tag_name").GetString()??"";var version=ParseVersion(tag);if(version==null)return null;
        var name="CodexToolbox-"+version.ToString(3)+"-win-x64.zip";
        var assets=j.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()==name).ToArray();
        if(assets.Length!=1)return null;
        var asset=assets[0];var url=asset.GetProperty("browser_download_url").GetString()??"";if(!ValidAssetUrl(url,tag))throw new IOException("更新地址不属于本项目，请到仓库页面查看。");
        long size=asset.GetProperty("size").GetInt64();if(size<=0||size>250L*1024*1024)throw new IOException("更新包大小异常，请到仓库页面查看。");
        var digest=asset.TryGetProperty("digest",out var d)?d.GetString()??"":"";
        var hash=Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$")?digest[7..].ToUpperInvariant():"";
        var sums=j.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()=="SHA256SUMS.txt").ToArray();
        var checksum=sums.Length==1?sums[0].GetProperty("browser_download_url").GetString()??"":"";
        if(hash.Length==0&&!ValidAssetUrl(checksum,tag))throw new IOException("新版缺少文件校验信息，请稍后再试。");
        return new(version,tag,Repository+"/releases/tag/"+tag,name,url,size,hash,checksum);
    }
    public static string Checksum(string manifest,string file)
    {
        var matches=manifest.Split('\n').Select(line=>Regex.Match(line.Trim(),@"^([a-fA-F0-9]{64})\s+\*?(.+)$")).Where(m=>m.Success&&m.Groups[2].Value==file).ToArray();
        if(matches.Length!=1)throw new IOException("更新文件校验信息不完整，请稍后重试。");return matches[0].Groups[1].Value.ToUpperInvariant();
    }
    static HttpClient Client()
    {
        var key=@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        var enabled=Microsoft.Win32.Registry.GetValue(key,"ProxyEnable",0) is int n&&n==1;
        var proxy=enabled?CodexDiscovery.WindowsProxy(Microsoft.Win32.Registry.GetValue(key,"ProxyServer","") as string??""):"";
        var handler=new SocketsHttpHandler{AllowAutoRedirect=true,MaxAutomaticRedirections=5,ConnectTimeout=TimeSpan.FromSeconds(8),UseProxy=proxy.Length>0};
        if(proxy.Length>0)handler.Proxy=new WebProxy(proxy);
        var client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexToolbox/"+CurrentLabel);client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");return client;
    }
    static async Task<string> Text(HttpClient client,string url,int max,CancellationToken ct)
    {
        using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(ct);using var data=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await stream.ReadAsync(buffer,ct))>0){data.Write(buffer,0,read);if(data.Length>max)throw new IOException("更新服务返回的信息过大，请稍后重试。");}
        return System.Text.Encoding.UTF8.GetString(data.ToArray());
    }
    public static async Task<ReleaseUpdate?> Check(bool force,CancellationToken ct)
    {
        var cachePath=Path.Combine(Store.DirectoryPath,"update-check.json");
        if(!force)try{if(File.Exists(cachePath)&&new FileInfo(cachePath).Length<65536){var cached=JsonSerializer.Deserialize<ReleaseCache>(await File.ReadAllTextAsync(cachePath,ct),Store.Json);if(cached!=null&&(cached.Release==null||ValidRelease(cached.Release))&&cached.CheckedUtc>DateTimeOffset.UtcNow.AddHours(-6)&&cached.CheckedUtc<DateTimeOffset.UtcNow.AddMinutes(5))return cached.Release;}}catch{}
        using var bounded=CancellationTokenSource.CreateLinkedTokenSource(ct);bounded.CancelAfter(TimeSpan.FromSeconds(20));ct=bounded.Token;
        using var client=Client();ReleaseUpdate? release;
        try{release=ParseRelease(await Text(client,Api,2*1024*1024,ct));}
        catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.NotFound){release=null;}
        catch(HttpRequestException e)when(e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests){throw new IOException("GitHub 暂时限制了检查频率，请稍后重试。");}
        var tmp=cachePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{Directory.CreateDirectory(Store.DirectoryPath);await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(new ReleaseCache(DateTimeOffset.UtcNow,release),Store.Json),ct);File.Move(tmp,cachePath,true);}catch(IOException){}catch(UnauthorizedAccessException){}finally{try{if(File.Exists(tmp))File.Delete(tmp);}catch{}}
        return release;
    }
    public static async Task<string> Download(ReleaseUpdate release,IProgress<int> progress,CancellationToken ct)
    {
        if(!ValidRelease(release))throw new IOException("更新信息不完整，请重新检查版本或到项目主页下载。");
        var staging=Path.Combine(Store.DirectoryPath,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        var zip=Path.Combine(staging,"package.zip");var candidate=Path.Combine(staging,"candidate.exe");
        try
        {
            using var bounded=CancellationTokenSource.CreateLinkedTokenSource(ct);bounded.CancelAfter(TimeSpan.FromMinutes(15));ct=bounded.Token;
            using var client=Client();var hash=release.Sha256;
            if(hash.Length>0&&!Regex.IsMatch(hash,@"^[a-fA-F0-9]{64}$"))throw new IOException("更新校验信息异常，请重新检查版本。");
            if(hash.Length==0&&!ValidAssetUrl(release.ChecksumUrl,release.Tag))throw new IOException("更新校验地址异常，请重新检查版本。");
            if(hash.Length==0)hash=Checksum(await Text(client,release.ChecksumUrl,65536,ct),release.AssetName);
            using var response=await client.GetAsync(release.AssetUrl,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
            await using(var source=await response.Content.ReadAsStreamAsync(ct))await using(var file=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))
            {
                var buffer=new byte[81920];long total=0;int read;
                while((read=await source.ReadAsync(buffer,ct))>0){total+=read;if(total>release.Size)throw new IOException("更新包大小与发布信息不符，已停止更新。");await file.WriteAsync(buffer.AsMemory(0,read),ct);progress.Report((int)(total*100/release.Size));}
                if(total!=release.Size)throw new IOException("更新包未下载完整，请重新下载。");
            }
            await using(var stream=File.OpenRead(zip)){if(!Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new IOException("更新包校验未通过，已保留当前版本。");}
            using(var archive=ZipFile.OpenRead(zip))
            {
                var entries=archive.Entries.Where(e=>e.FullName=="CodexToolbox-Windows/Codex工具箱.exe").ToArray();
                if(entries.Length!=1||entries[0].Length is <1024 or >300L*1024*1024)throw new IOException("更新包内容不完整，已保留当前版本。");
                await using var input=entries[0].Open();await using var output=new FileStream(candidate,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                var buffer=new byte[81920];long total=0;int read;while((read=await input.ReadAsync(buffer,ct))>0){total+=read;if(total>entries[0].Length)throw new IOException("更新文件展开大小异常，已停止更新。");await output.WriteAsync(buffer.AsMemory(0,read),ct);}if(total!=entries[0].Length)throw new IOException("更新文件未完整展开，请重试。");
            }
            if(ParseVersion((FileVersionInfo.GetVersionInfo(candidate).ProductVersion??"").Split('+')[0])!=release.Version)throw new IOException("更新包版本与发布信息不一致，已停止更新。");
            File.Delete(zip);return candidate;
        }
        catch{foreach(var f in new[]{zip,candidate})try{File.Delete(f);}catch{}try{Directory.Delete(staging);}catch{}throw;}
    }
}
