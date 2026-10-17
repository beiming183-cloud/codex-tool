using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexToolbox;

public static class Guard
{
    public const string RuleGroup="CodexToolbox Campus Guard";
    public const string TaskName="CodexToolbox-CampusGuard";
    public static string MachineDir=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"CodexToolboxGuard");
    public static string InstallDir=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"CodexToolboxGuard");
    public static string PolicyPath=>Path.Combine(MachineDir,"policy.json");
    public static string StatusPath=>Path.Combine(MachineDir,"status.json");
    public static bool Installed=>File.Exists(PolicyPath)&&File.Exists(Path.Combine(InstallDir,"CodexToolbox.exe"));
    public static bool Matches(string name,string pattern)=>Regex.IsMatch(name,"^"+Regex.Escape(pattern).Replace("\\*",".*").Replace("\\?",".")+"$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public static void Validate(CampusPolicy p,bool install)
    {
        if(p.Networks is null||p.Programs is null||p.OrdinaryNetworks is null)throw new InvalidDataException("校园策略缺少必要字段。");
        if(p.Networks.Count>100 || p.Programs.Count>100 || p.OrdinaryNetworks.Count>100)throw new InvalidDataException("每类校园规则最多 100 项。");
        foreach(var n in p.Networks.Concat(p.OrdinaryNetworks))if(string.IsNullOrWhiteSpace(n)||n.Length>128||n.Trim() is "*" or "?")throw new InvalidDataException("请填写具体网络名或带前缀的匹配式，不能匹配所有网络。");
        foreach(var f in p.Programs)
        {
            if(!Path.IsPathFullyQualified(f)||!f.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||f.Contains('\n')||f.Contains('\r'))throw new InvalidDataException("受限程序必须是完整的 .exe 路径。");
            if(install && !File.Exists(f))throw new InvalidDataException("找不到受限程序："+f);
        }
        if(install && ((!p.AutoDetect&&p.Networks.Count==0)||(!p.AutoDetectPrograms&&p.Programs.Count==0)))throw new InvalidDataException("请开启自动识别，或填写网络规则和受限代理程序。");
    }
    static string RuleName(string file)=>"CodexToolbox-Campus-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToLowerInvariant())))[..20];
    static void ConfigureRules(CampusPolicy p,bool enabled)
    {
        dynamic policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        foreach(int profile in new[]{1,2,4})if(!(bool)policy.FirewallEnabled[profile])throw new InvalidOperationException("Windows 防火墙有配置文件未启用。");
        var desired=p.Programs.Select(RuleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remove=new List<string>();
        foreach(dynamic existing in policy.Rules)
            if((string)existing.Grouping==RuleGroup && !desired.Contains((string)existing.Name))remove.Add((string)existing.Name);
        foreach(var name in remove)policy.Rules.Remove(name);
        foreach(var file in p.Programs)
        {
            var name=RuleName(file);dynamic? rule=null;
            try{rule=policy.Rules.Item(name);}catch{}
            if(rule is null)
            {
                rule=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
                rule.Name=name;rule.Description="校园网匹配时限制该代理程序的出站连接。由 Codex 工具箱管理。";rule.Grouping=RuleGroup;
                rule.ApplicationName=Path.GetFullPath(file);rule.Direction=2;rule.Action=0;rule.Profiles=int.MaxValue;rule.Protocol=256;rule.Enabled=enabled;
                policy.Rules.Add(rule);
            }
            else{if((string)rule.Grouping!=RuleGroup)throw new InvalidOperationException("同名规则由其他程序管理。");rule.Enabled=enabled;}
        }
    }
    static void RemoveRules()
    {
        dynamic policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
        var names=new List<string>();foreach(dynamic r in policy.Rules)if((string)r.Grouping==RuleGroup)names.Add((string)r.Name);
        foreach(var name in names)policy.Rules.Remove(name);
    }
    static void ProtectFolder(string path)
    {
        Directory.CreateDirectory(path);
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("保护目录不能是符号链接或目录联接。");
        // Replace the DACL and owner; an existing user-created folder must not retain write access.
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        var admin=new SecurityIdentifier("S-1-5-32-544");acl.SetOwner(admin);
        foreach(var entry in new[]{("S-1-5-18",FileSystemRights.FullControl),("S-1-5-32-544",FileSystemRights.FullControl),("S-1-5-32-545",FileSystemRights.ReadAndExecute)})
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(entry.Item1),entry.Item2,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
    }
    internal static FileSecurity InstalledFileSecurity()
    {
        var acl=new FileSecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
        foreach(var entry in new[]{("S-1-5-18",FileSystemRights.FullControl),("S-1-5-32-544",FileSystemRights.FullControl),("S-1-5-32-545",FileSystemRights.ReadAndExecute)})
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(entry.Item1),entry.Item2,AccessControlType.Allow));
        return acl;
    }
    static async Task StopOwnedTask()
    {
        var target=Path.Combine(InstallDir,"CodexToolbox.exe");
        await Runner.PowerShell("$workers=@(Get-CimInstance Win32_Process -Filter \"Name='CodexToolbox.exe'\" | Where-Object {$_.ExecutablePath -eq "+Runner.Quote(target)+" -and $_.CommandLine -match '(?:^|\\s)--guard-loop(?:\\s|$)'} | Select-Object -ExpandProperty ProcessId);$t=Get-ScheduledTask -TaskName '"+TaskName+"' -ErrorAction SilentlyContinue;if($t){Stop-ScheduledTask -InputObject $t};foreach($workerId in $workers){$worker=Get-Process -Id $workerId -ErrorAction SilentlyContinue;if($worker){if(-not $worker.WaitForExit(10000)){throw '保护进程尚未停止，请稍后重试。'}}}",25);
    }
    public static void Elevated(string mode,string? policy=null)
    {
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};info.ArgumentList.Add(mode);if(policy!=null)info.ArgumentList.Add(policy);
        using var p=Process.Start(info)??throw new IOException("未启动管理员授权。");p.WaitForExit();if(p.ExitCode!=0)throw new IOException(p.ExitCode==1223?"已取消 Windows 授权，设置未启用。":"操作未完成，请确认 Windows 防火墙和任务计划服务可用。");
    }
    public static async Task<int> Handle(string[] args)
    {
        if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))return 5;
        try
        {
            if(args[0]=="--install-guard")
            {
                if(args.Length<2)throw new InvalidDataException();var draft=args[1];if(new FileInfo(draft).Length>65536)throw new InvalidDataException();
                var p=JsonSerializer.Deserialize<CampusPolicy>(File.ReadAllText(draft),Store.Json)??throw new InvalidDataException();Validate(p,true);
                await StopOwnedTask();
                ProtectFolder(InstallDir);ProtectFolder(MachineDir);
                var target=Path.Combine(InstallDir,"CodexToolbox.exe");
                if(!string.Equals(Path.GetFullPath(Environment.ProcessPath!),Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))
                    for(int attempt=0;;attempt++){try{File.Copy(Environment.ProcessPath!,target,true);break;}catch(IOException)when(attempt<8){await Task.Delay(200);}}
                new FileInfo(target).SetAccessControl(InstalledFileSecurity());
                var policyTmp=Path.Combine(MachineDir,Guid.NewGuid().ToString("N")+".tmp");File.WriteAllText(policyTmp,JsonSerializer.Serialize(p,Store.Json));File.Move(policyTmp,PolicyPath,true);
                var command="$a=New-ScheduledTaskAction -Execute "+Runner.Quote(target)+" -Argument '--guard-loop';$t=New-ScheduledTaskTrigger -AtStartup;$s=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 99 -RestartInterval (New-TimeSpan -Minutes 1);Register-ScheduledTask -TaskName '"+TaskName+"' -Action $a -Trigger $t -Settings $s -User SYSTEM -RunLevel Highest -Force|Out-Null;Start-ScheduledTask -TaskName '"+TaskName+"'";
                await Runner.PowerShell(command,25);return 0;
            }
            if(args[0]=="--remove-guard")
            {
                await StopOwnedTask();
                await Runner.PowerShell("$t=Get-ScheduledTask -TaskName '"+TaskName+"' -ErrorAction SilentlyContinue;if($t){Stop-ScheduledTask -InputObject $t;Unregister-ScheduledTask -InputObject $t -Confirm:$false}");
                RemoveRules();if(File.Exists(PolicyPath))File.Delete(PolicyPath);if(File.Exists(StatusPath))File.Delete(StatusPath);return 0;
            }
            if(args[0]=="--guard-loop")
            {
                var p=JsonSerializer.Deserialize<CampusPolicy>(File.ReadAllText(PolicyPath),Store.Json)!;Validate(p,true);
                var seenPrograms=new HashSet<string>(p.Programs,StringComparer.OrdinalIgnoreCase);
                while(true)
                {
                    bool blocked=p.BlockOnUnknown;List<string> names=[];string error="";string recognition="Unknown";int programCount=p.Programs.Count;
                    try{var a=await CampusDiscovery.Inspect(p);names=a.Names;recognition=a.Kind;blocked=a.ShouldBlock(p);}
                    catch{error="网络识别暂不可用";}
                    try{var active=JsonSerializer.Deserialize<CampusPolicy>(JsonSerializer.Serialize(p),Store.Json)!;if(p.AutoDetectPrograms){foreach(var file in await CampusDiscovery.DetectPrograms())seenPrograms.Add(file);active.Programs=seenPrograms.ToList();}programCount=active.Programs.Count;ConfigureRules(active,blocked);if(blocked&&active.Programs.Count==0)error="已要求限制，但尚未发现代理内核程序。";}
                    catch{error="防火墙规则更新失败";}
                    var status=new{Time=DateTimeOffset.Now,Networks=names,Recognition=recognition,Blocked=blocked,Programs=programCount,Error=error};
                    var tmp=Path.Combine(MachineDir,Guid.NewGuid().ToString("N")+".tmp");File.WriteAllText(tmp,JsonSerializer.Serialize(status,Store.Json));File.Move(tmp,StatusPath,true);
                    await Task.Delay(5000);
                }
            }
            return 1;
        }
        catch(Exception e)
        {
            if(args[0]!="--guard-loop")System.Windows.MessageBox.Show(e is InvalidDataException?e.Message:"校园网保护操作失败。请确认程序路径、任务计划服务和 Windows 防火墙可用。","Codex 工具箱");return 1;
        }
    }
    public static List<ResultRow> Status()
    {
        var rows=new List<ResultRow>{new("本工具自动保护",Installed?"已安装；关闭工具箱后继续运行":"尚未启用","本机状态")};
        if(File.Exists(StatusPath))
        {
            try
            {
                using var j=JsonDocument.Parse(File.ReadAllText(StatusPath));var r=j.RootElement;
                bool fresh=DateTimeOffset.Now-r.GetProperty("Time").GetDateTimeOffset()<TimeSpan.FromSeconds(30);
                rows.Add(new("保护心跳",fresh?"正常":"已过期，请重新启用保护",fresh?"正常":"待处理"));
                bool error=r.GetProperty("Error").GetString() is {Length:>0};
                rows.Add(new("当前限制",fresh&&!error?(r.GetProperty("Blocked").GetBoolean()?"正在限制指定代理程序联网":"未匹配校园网；已放行指定程序"):"状态暂不可确认",fresh&&!error?"已读取":"尚未确认"));
                if(r.GetProperty("Error").GetString() is {Length:>0} e)rows.Add(new("需要处理",e,"异常"));
            }
            catch{rows.Add(new("保护心跳","暂不可读取，请重新检查。","尚未确认"));}
        }
        return rows;
    }
}
