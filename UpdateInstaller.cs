using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace CodexToolbox;

public sealed class UpdatePlan
{
    public int ParentPid {get;set;}public long ParentStartTicks {get;set;}
    public string Target {get;set;}="";public string Candidate {get;set;}="";public string DataDirectory {get;set;}="";
    public string OldHash {get;set;}="";public string NewHash {get;set;}="";public string Version {get;set;}="";
    public string Nonce {get;set;}=Guid.NewGuid().ToString("N");
}
public static class UpdateInstaller
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int MessageBox(IntPtr window,string message,string title,uint type);
    static string Hash(string file){using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream));}
    public static string StagePlan(string candidate,ReleaseUpdate release)
    {
        var target=Environment.ProcessPath??throw new IOException("找不到工具箱位置，请到项目页面下载新版。");var folder=Path.GetDirectoryName(target)!;
        if((File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new IOException("此工具箱是链接文件，请从项目页面下载新版替换。");
        var probe=Path.Combine(folder,".codex-toolbox-"+Guid.NewGuid().ToString("N")+".tmp");
        try{using(File.Create(probe)){}File.Delete(probe);}catch{throw new IOException("当前目录无法写入。请将工具箱移到桌面或下载目录，再更新。");}
        var staging=Path.GetDirectoryName(candidate)!;var helper=Path.Combine(staging,"helper.exe");
        if(!Path.GetFullPath(staging).StartsWith(Path.GetFullPath(Path.Combine(Store.DirectoryPath,"updates"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("更新缓存位置异常，请重新下载。");
        File.Copy(target,helper,false);
        using var current=Process.GetCurrentProcess();var plan=new UpdatePlan{ParentPid=current.Id,ParentStartTicks=current.StartTime.ToUniversalTime().Ticks,Target=target,Candidate=candidate,DataDirectory=Store.DirectoryPath,OldHash=Hash(target),NewHash=Hash(candidate),Version=release.Version.ToString(3)};
        var path=Path.Combine(staging,"plan.json");File.WriteAllText(path,JsonSerializer.Serialize(plan,Store.Json));return path;
    }
    public static void Launch(string plan)
    {
        var info=new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(plan)!,"helper.exe")){UseShellExecute=false,CreateNoWindow=true};info.ArgumentList.Add("--apply-update");info.ArgumentList.Add(plan);_ = Process.Start(info)??throw new IOException("无法启动更新程序，请重试。");
    }
    public static bool ValidPlan(UpdatePlan plan,string directory)
    {
        try{return Path.IsPathFullyQualified(plan.Target)&&Path.GetExtension(plan.Target).Equals(".exe",StringComparison.OrdinalIgnoreCase)&&Path.IsPathFullyQualified(plan.DataDirectory)&&Path.GetFullPath(plan.Candidate).Equals(Path.Combine(Path.GetFullPath(directory),"candidate.exe"),StringComparison.OrdinalIgnoreCase)&&Guid.TryParseExact(Path.GetFileName(directory),"N",out _)&&Path.GetFullPath(Path.GetDirectoryName(directory)!).Equals(Path.GetFullPath(Path.Combine(plan.DataDirectory,"updates")),StringComparison.OrdinalIgnoreCase)&&Updates.ParseVersion(plan.Version)!=null&&plan.Nonce.Length==32;}
        catch{return false;}
    }
    public static void Acknowledge(string[] args)
    {
        int i=Array.IndexOf(args,"--update-ack");if(i<0||i+1>=args.Length)return;
        try
        {
            var file=args[i+1];if(Path.GetFileName(file)!="plan.json"||new FileInfo(file).Length>16384)return;
            var plan=JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(file),Store.Json);if(plan==null||!ValidPlan(plan,Path.GetDirectoryName(file)!)||!Path.GetFullPath(plan.Target).Equals(Path.GetFullPath(Environment.ProcessPath!),StringComparison.OrdinalIgnoreCase)||plan.Version!=Updates.CurrentLabel||!Path.GetFullPath(plan.DataDirectory).Equals(Path.GetFullPath(Store.DirectoryPath),StringComparison.OrdinalIgnoreCase))return;
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(file)!,"ready.json"),JsonSerializer.Serialize(new{plan.Nonce,Version=Updates.CurrentLabel}));
        }
        catch{}
    }
    public static async Task<int> Apply(string file)
    {
        string? pending=null,backup=null;UpdatePlan? plan=null;bool replaced=false;Process? launched=null;
        try
        {
            if(new FileInfo(file).Length>16384)throw new IOException("更新信息异常。");
            plan=JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(file),Store.Json)??throw new IOException("更新信息为空。");var directory=Path.GetDirectoryName(Path.GetFullPath(file))!;
            if(!ValidPlan(plan,directory)||!Path.GetFullPath(Environment.ProcessPath!).Equals(Path.Combine(directory,"helper.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("更新位置不匹配。");
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0||Hash(plan.Candidate)!=plan.NewHash)throw new IOException("更新文件已变化，已取消更新。");
            if(Updates.ParseVersion((FileVersionInfo.GetVersionInfo(plan.Candidate).ProductVersion??"").Split('+')[0])?.ToString(3)!=plan.Version)throw new IOException("更新版本不匹配。");
            try{using var parent=Process.GetProcessById(plan.ParentPid);if(parent.StartTime.ToUniversalTime().Ticks!=plan.ParentStartTicks||!string.Equals(parent.MainModule?.FileName,plan.Target,StringComparison.OrdinalIgnoreCase))throw new IOException("原工具箱进程不匹配。");using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await parent.WaitForExitAsync(timeout.Token);}catch(ArgumentException){}
            if(Hash(plan.Target)!=plan.OldHash)throw new IOException("当前文件已被其他操作改变，已取消替换。");
            pending=Path.Combine(Path.GetDirectoryName(plan.Target)!,".codex-toolbox-"+plan.Nonce+".pending");backup=Path.Combine(Path.GetDirectoryName(plan.Target)!,".codex-toolbox-"+plan.Nonce+".previous");
            File.Copy(plan.Candidate,pending,false);
            // All three paths are on the target volume; Replace is atomic, including across data drives.
            File.Replace(pending,plan.Target,backup);replaced=true;File.Delete(plan.Candidate);
            var start=new ProcessStartInfo(plan.Target){UseShellExecute=false};start.ArgumentList.Add("--data-dir");start.ArgumentList.Add(plan.DataDirectory);start.ArgumentList.Add("--update-ack");start.ArgumentList.Add(file);launched=Process.Start(start)??throw new IOException("新版未能启动。");
            var ready=Path.Combine(directory,"ready.json");bool confirmed=false;
            for(int attempt=0;attempt<180;attempt++)
            {
                if(File.Exists(ready))try{using var j=JsonDocument.Parse(await File.ReadAllTextAsync(ready));confirmed=j.RootElement.GetProperty("Nonce").GetString()==plan.Nonce&&j.RootElement.GetProperty("Version").GetString()==plan.Version;}catch{}
                if(confirmed)break;if(launched.HasExited)break;await Task.Delay(250);
            }
            if(!confirmed)throw new IOException("新版未能正常打开，正在恢复上一版。");
            try{File.Copy(backup,Path.Combine(directory,"previous.exe"),true);}catch{}
            try{File.Delete(backup);}catch{}backup=null;
            Result(plan,"success","更新成功，个人配置已保留。");return 0;
        }
        catch(Exception)
        {
            if(plan!=null)
            {
                if(replaced&&backup!=null&&File.Exists(backup))
                {
                    try
                    {
                        if(launched is {HasExited:false}){launched.Kill();await launched.WaitForExitAsync();}
                        File.Copy(backup,plan.Target,true);File.Delete(backup);backup=null;
                        Result(plan,"rolled-back","新版未能正常启动，已恢复上一版。可到项目页面下载更新。");
                        var start=new ProcessStartInfo(plan.Target){UseShellExecute=false};start.ArgumentList.Add("--data-dir");start.ArgumentList.Add(plan.DataDirectory);Process.Start(start);MessageBox(IntPtr.Zero,"新版未能正常打开，已恢复上一版。个人配置未改动，可以稍后重试。","更新已恢复",0x40);return 1;
                    }
                    catch{Result(plan,"failed","自动恢复未完成。上一版保留在工具箱旁的 .previous 文件，可改名为 .exe 打开。");}
                }
                else {Result(plan,"failed","更新没有完成，当前版本未替换。请重新打开工具箱后重试。");MessageBox(IntPtr.Zero,"更新没有完成，当前版本未替换。请重新打开工具箱后重试。","更新未完成",0x30);}
            }
            return 1;
        }
        finally{launched?.Dispose();if(pending!=null)try{File.Delete(pending);}catch{}}
    }
    static void Result(UpdatePlan plan,string state,string message)
    {try{Directory.CreateDirectory(plan.DataDirectory);File.WriteAllText(Path.Combine(plan.DataDirectory,"update-result.json"),JsonSerializer.Serialize(new{State=state,Message=message,Time=DateTimeOffset.Now},Store.Json));}catch{}}
    public static void Cleanup()
    {
        try
        {
            var root=Path.Combine(Store.DirectoryPath,"updates");if(!Directory.Exists(root))return;
            var folders=new DirectoryInfo(root).GetDirectories().Where(d=>Guid.TryParseExact(d.Name,"N",out _)&&(d.Attributes&FileAttributes.ReparsePoint)==0).OrderByDescending(d=>d.LastWriteTimeUtc).ToArray();
            var keepBackup=folders.FirstOrDefault(d=>File.Exists(Path.Combine(d.FullName,"previous.exe")))?.FullName;
            for(int i=0;i<folders.Length;i++)
            {
                var folder=folders[i];if(folder.LastWriteTimeUtc>DateTime.UtcNow.AddMinutes(-10))continue;
                foreach(var name in new[]{"package.zip","candidate.exe","helper.exe","plan.json","ready.json","previous.exe"})if(folder.FullName!=keepBackup||name!="previous.exe")try{File.Delete(Path.Combine(folder.FullName,name));}catch{}
                try{folder.Delete(false);}catch{}
            }
        }
        catch{}
    }
}
