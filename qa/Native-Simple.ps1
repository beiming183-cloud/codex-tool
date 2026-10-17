param([int]$TargetProcess,[string]$DataDirectory,[string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;using System.Text;public class AutoQa{public delegate bool CB(IntPtr h,IntPtr p);[DllImport("user32.dll")]static extern bool EnumWindows(CB c,IntPtr p);[DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);[DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder b,int n);[DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);[DllImport("user32.dll")]public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int t,bool r);[DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr v);public static IntPtr Find(uint id,string title){IntPtr r=IntPtr.Zero;EnumWindows((h,p)=>{uint pid;GetWindowThreadProcessId(h,out pid);var b=new StringBuilder(256);GetWindowText(h,b,256);if(pid==id&&b.ToString()==title){r=h;return false;}return true;},IntPtr.Zero);return r;}}
'@
[AutoQa]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$handle=[AutoQa]::Find($TargetProcess,'Codex 工具箱');$main=[Windows.Automation.AutomationElement]::FromHandle($handle)
function Id($Root,[string]$Name){for($i=0;$i -lt 15;$i++){$el=$Root.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$Name));if($el){return $el};Start-Sleep -Milliseconds 100};throw "Missing control $Name"}
function Click($Root,[string]$Name){$el=Id $Root $Name;([Windows.Automation.InvokePattern]$el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke();Start-Sleep -Milliseconds 350}
function Names($Root){@($Root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object {$_.Current.Name})}
function Wait-Text($Root,[string]$Pattern){for($i=0;$i -lt 180;$i++){$values=Names $Root;if($values -match '检查未完成：'){throw 'Tool showed an error'};if($values -match $Pattern){return $values};Start-Sleep -Milliseconds 250};throw "UI state did not arrive: $Pattern"}
function Capture([int]$Tag){[AutoQa]::SendMessage($handle,32776,[IntPtr]$Tag,[IntPtr]::Zero)|Out-Null}
function Settings{if(Test-Path -LiteralPath (Join-Path $DataDirectory 'settings.json')){Get-Content -LiteralPath (Join-Path $DataDirectory 'settings.json') -Raw|ConvertFrom-Json}else{[pscustomobject]@{Profiles=@()}}}
function Expand([string]$Name){$all=$main.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$Name));foreach($e in $all){$pattern=$null;if($e.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$pattern)){$pattern.Expand();Start-Sleep -Milliseconds 300;return}};throw "Missing expander $Name"}
$checks=@{}
Click $main 'Nav0';$homeNames=Wait-Text $main '自动核验结论：';Capture 40
$checks.HomeAutomatic=($homeNames -match '自动核验结论：').Count -gt 0
$checks.HomeAdvancedCollapsed= -not ($homeNames -contains '添加自定义线路')
Click $main 'Nav1';$quota=Wait-Text $main '已找到登录状态';Capture 41
$buttons=@($main.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object {$_.Current.AutomationId -like 'AttachQuota-*'})
$checks.AutomaticQuotaWindows=$buttons.Count
foreach($b in $buttons){([Windows.Automation.InvokePattern]$b.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke();Start-Sleep -Milliseconds 400}
for($q=0;$q -lt 80;$q++){$quota=Names $main;if(($quota -match '额度浮层已在此窗口显示').Count -eq $buttons.Count){break};Start-Sleep -Milliseconds 250};Capture 42
$checks.ExistingOverlayRecognized=($quota -match '额度浮层已在此窗口显示').Count -eq $buttons.Count
Click $main 'Nav2';$campus=Wait-Text $main '当前网络：';Capture 43
$checks.CampusRecognized=($campus -match '当前网络：').Count -gt 0
$checks.CampusAdvancedCollapsed= -not ($campus -contains '受限代理程序 · 每行一个完整 .exe 路径')
Click $main 'Nav3';Capture 44
$checks.SettingsAdvancedCollapsed= -not ((Names $main) -contains 'Clash 控制接口 · 留空自动识别')
Expand '诊断工具'
$checks.Tools=@()
foreach($key in @('auto','clash','connections','ips')){
    Click $main ('Tool-'+$key)
    $title=switch($key){'auto' {'自动线路核验'} 'clash' {'Clash 状态'} 'connections' {'应用连接'} 'ips' {'公网 IP'}}
    $toolHandle=[AutoQa]::Find($TargetProcess,($title+' · Codex 工具箱'));$tool=[Windows.Automation.AutomationElement]::FromHandle($toolHandle)
    $rows=Wait-Text $tool '已完成';$checks.Tools+=@{Key=$key;Completed=(($rows -match '已完成').Count -gt 0)}
    if($key -eq 'clash'){Capture 45}
    [AutoQa]::SendMessage($toolHandle,16,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null;Start-Sleep -Milliseconds 350
}
[AutoQa]::MoveWindow($handle,300,100,1320,915,$true)|Out-Null
for($i=0;$i -lt 4;$i++){Click $main ('Nav'+$i);Capture (50+$i)}
[AutoQa]::MoveWindow($handle,300,100,1560,1128,$true)|Out-Null;Click $main 'Nav0'
$checks|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'native-simple.json') -Encoding UTF8
$checks|ConvertTo-Json -Depth 5