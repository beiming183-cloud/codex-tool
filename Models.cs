using System.IO;
using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace CodexToolbox;

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新线路";
    public string Proxy { get; set; } = "http://127.0.0.1:7890";
    public string ExpectedIp { get; set; } = "";
    public string Group { get; set; } = "";
    public string AppProxy { get; set; } = "";
    public string AuthHome { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string QuotaIdentity { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
public sealed class CampusPolicy
{
    public bool AutoDetect { get; set; } = true;
    public bool AutoDetectPrograms { get; set; } = true;
    public List<string> OrdinaryNetworks { get; set; } = [];
    public List<string> Networks { get; set; } = [];
    public List<string> Programs { get; set; } = [];
    public bool BlockOnUnknown { get; set; } = true;
}
public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;
    public List<Profile> Profiles { get; set; } = [];
    public CampusPolicy Campus { get; set; } = new();
    public int RefreshSeconds { get; set; } = 30;
    public string Controller { get; set; } = "";
    public string SecretProtected { get; set; } = "";
    public string ProbeUrl { get; set; } = "https://www.cloudflare.com/cdn-cgi/trace";
    public string LegacyRoot { get; set; } = "";
    public int ZoomPercent { get; set; } = 100;
    public bool SidebarCollapsed { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = true;
}
public static class Store
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string DirectoryPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexToolbox");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new();
        try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? throw new InvalidDataException(); Validate(s); return s; }
        catch (Exception e) { throw new InvalidDataException("配置文件无法读取，原文件已保留。请修复配置或从备份恢复。" , e); }
    }
    public static void Save(Settings s)
    {
        Validate(s); Directory.CreateDirectory(DirectoryPath);
        var tmp = FilePath + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
        File.Move(tmp, FilePath, true);
    }
    public static void Validate(Settings s)
    {
        if (s.SchemaVersion != 1) throw new InvalidDataException("不支持此配置版本。");
        if(s.ZoomPercent is <75 or >150)throw new InvalidDataException("界面缩放范围为 75%–150%。");
        if (s.Profiles is null || s.Campus is null || s.Campus.Networks is null || s.Campus.Programs is null) throw new InvalidDataException("配置缺少必要字段。");
        if(s.Profiles.Count > 200) throw new InvalidDataException("一次最多导入 200 条线路。");
        if(s.Profiles.Select(p=>p.Id).Distinct().Count()!=s.Profiles.Count) throw new InvalidDataException("线路标识重复。");
        foreach (var p in s.Profiles)
        {
            if(string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 80) throw new InvalidDataException("线路名称需为 1–80 个字符。");
            ValidateProxy(p.Proxy, false); ValidateProxy(p.AppProxy, true);
            if(p.ExpectedIp.Length>0 && !IPAddress.TryParse(p.ExpectedIp,out _)) throw new InvalidDataException("预期 IP 不是有效的 IPv4 或 IPv6 地址。");
        }
        if(!Uri.TryCreate(s.ProbeUrl,UriKind.Absolute,out var probe) || probe.Scheme!="https" || !string.IsNullOrEmpty(probe.UserInfo)) throw new InvalidDataException("检测地址需要使用 HTTPS。");
        if(s.RefreshSeconds < 10 || s.RefreshSeconds > 3600) throw new InvalidDataException("刷新间隔应在 10–3600 秒之间。");
        if(!string.IsNullOrEmpty(s.Controller) && (!Uri.TryCreate(s.Controller,UriKind.Absolute,out var api) || api.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(api.UserInfo))) throw new InvalidDataException("控制接口地址应为 HTTP 或 HTTPS，凭据请填写在密钥栏。");
        Guard.Validate(s.Campus, false);
    }
    public static void ValidateProxy(string value,bool optional)
    {
        if(optional && string.IsNullOrWhiteSpace(value)) return;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var u) || u.Scheme is not ("http" or "https" or "socks5") || u.Port<1 || u.AbsolutePath!="/" || !string.IsNullOrEmpty(u.UserInfo) || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment))
            throw new InvalidDataException("代理地址格式如 http://127.0.0.1:7890 或 socks5://127.0.0.1:7890，不包含用户名和密码。");
    }
    public static Settings ShareCopy(Settings s)
    {
        var copy=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s,Json),Json)!;
        copy.SecretProtected=""; copy.LegacyRoot="";
        foreach(var p in copy.Profiles){ p.AuthHome=""; }
        return copy;
    }
}
public static class LocalSecret
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true)] static extern bool CryptProtectData(ref Blob input,IntPtr desc,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] static extern bool CryptUnprotectData(ref Blob input,IntPtr desc,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);
    public static string Protect(string s)=> s.Length==0?"":System.Convert.ToBase64String(Convert(System.Text.Encoding.UTF8.GetBytes(s),true));
    public static string Unprotect(string s)=> s.Length==0?"":System.Text.Encoding.UTF8.GetString(Convert(System.Convert.FromBase64String(s),false));
    static byte[] Convert(byte[] bytes,bool encrypt)
    {
        var input=new Blob{Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};
        try {Marshal.Copy(bytes,0,input.Data,bytes.Length); Blob output; bool ok=encrypt?CryptProtectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output); if(!ok) throw new CryptographicException("无法读取此 Windows 用户的密钥，请重新输入。"); try {var b=new byte[output.Length];Marshal.Copy(output.Data,b,0,b.Length);return b;} finally {LocalFree(output.Data);} }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}
