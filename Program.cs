using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace CodexToolbox;
public static class Program
{
    internal static string InstanceKey="";
    [DllImport("shell32.dll")] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [STAThread] public static int Main(string[] args)
    {
        if(args.Length>0 && args[0] is "--install-guard" or "--remove-guard" or "--guard-loop")return Guard.Handle(args).GetAwaiter().GetResult();
        if(!args.Contains("--standalone-host")&&DesktopProcess.HasPackageIdentity())
        {DesktopProcess.LaunchOutsidePackage(Environment.ProcessPath!,args.Concat(["--standalone-host"]).ToArray());return 0;}
        if(args.Length>=2&&args[0]=="--apply-update")return UpdateInstaller.Apply(args[1]).GetAwaiter().GetResult();
        int defaultIndex=Array.IndexOf(args,"--set-default-data-dir");
        if(defaultIndex>=0)
        {
            if(defaultIndex+1>=args.Length||!Path.IsPathFullyQualified(args[defaultIndex+1]))return 2;
            var directory=Path.GetFullPath(args[defaultIndex+1]);Directory.CreateDirectory(directory);
            Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\CodexToolbox","DataDirectory",directory,Microsoft.Win32.RegistryValueKind.String);return 0;
        }
        if(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\CodexToolbox","DataDirectory",null) is string data&&Path.IsPathFullyQualified(data))Store.DirectoryPath=data;
        for(int i=0;i<args.Length-1;i++)if(args[i]=="--data-dir")Store.DirectoryPath=Path.GetFullPath(args[i+1]);
        if(args.Contains("--self-test"))return SelfTest.Run();
        int updateIndex=Array.IndexOf(args,"--check-updates");
        if(updateIndex>=0&&updateIndex+1<args.Length)
        {
            try{var release=Updates.Check(true,CancellationToken.None).GetAwaiter().GetResult();File.WriteAllText(args[updateIndex+1],System.Text.Json.JsonSerializer.Serialize(new{Current=Updates.CurrentLabel,Latest=release?.Version.ToString(3),IsNewer=release?.Version>Updates.CurrentVersion,HasPackage=release!=null},Store.Json));return 0;}
            catch(Exception e){File.WriteAllText(args[updateIndex+1],System.Text.Json.JsonSerializer.Serialize(new{Error=e.Message},Store.Json));return 1;}
        }
        int discoveryIndex=Array.IndexOf(args,"--diagnose-codex");
        if(discoveryIndex>=0&&discoveryIndex+1<args.Length)
        {
            try{var result=CodexDiscovery.Find(Store.Load(),CancellationToken.None).GetAwaiter().GetResult();File.WriteAllText(args[discoveryIndex+1],System.Text.Json.JsonSerializer.Serialize(result,Store.Json));return 0;}
            catch(Exception e){File.WriteAllText(args[discoveryIndex+1],System.Text.Json.JsonSerializer.Serialize(new{Error=e.Message},Store.Json));return 1;}
        }
        int diagnosticIndex=Array.IndexOf(args,"--diagnose-auto");
        if(diagnosticIndex>=0&&diagnosticIndex+1<args.Length)
        {
            try{var s=Store.Load();var result=new AutoInspector().Scan(s).GetAwaiter().GetResult();var campus=CampusDiscovery.Inspect(s.Campus).GetAwaiter().GetResult();File.WriteAllText(args[diagnosticIndex+1],System.Text.Json.JsonSerializer.Serialize(new{Routes=result,Campus=campus},Store.Json));return 0;}
            catch(Exception e){File.WriteAllText(args[diagnosticIndex+1],System.Text.Json.JsonSerializer.Serialize(new{Error=e.GetType().Name,Message=e.Message,Stack=e.StackTrace},Store.Json));return 1;}
        }
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Store.DirectoryPath.ToLowerInvariant())))[..16];
        using var mutex=new Mutex(true,"Local\\CodexToolbox-"+key,out bool first);
        if(!first){UpdateInstaller.Acknowledge(args);Native.ActivateExisting(key);return 0;}
        InstanceKey=key;
        SetCurrentProcessExplicitAppUserModelID("CodexToolbox.Desktop");
        var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/CodexToolbox;component/Theme.xaml",UriKind.Relative)});
        app.DispatcherUnhandledException+=(_,e)=>{System.Windows.MessageBox.Show("操作未完成："+e.Exception.Message,"Codex 工具箱",MessageBoxButton.OK,MessageBoxImage.Warning);e.Handled=true;};
        try{return app.Run(new MainWindow(Store.Load(),args));}
        catch(Exception e){System.Windows.MessageBox.Show(e.Message,"Codex 工具箱",MessageBoxButton.OK,MessageBoxImage.Error);return 1;}
    }
}
static class Native
{
    delegate bool EnumCallback(IntPtr hwnd,IntPtr arg);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback,IntPtr arg);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr hwnd,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool SetProp(IntPtr hwnd,string name,IntPtr value);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int mode);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    public static void ActivateExisting(string key)
    {
        EnumWindows((h,_)=>{if(GetProp(h,"CodexToolbox."+key)==IntPtr.Zero)return true;if(IsIconic(h))ShowWindow(h,9);SetForegroundWindow(h);return false;},IntPtr.Zero);
    }
}
