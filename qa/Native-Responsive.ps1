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
function Capture-State([int]$Tag){Capture $Tag;Get-Content -LiteralPath (Join-Path $EvidenceDirectory "view-$Tag.json") -Raw|ConvertFrom-Json}
Click $main 'ZoomReset';Click $main 'Nav0';$current=Wait-Text $main '自动核验结论：';$normal=Capture-State 70
$checks.Default=@{Zoom=$normal.ZoomPercent;Columns=$normal.CardColumns;Compact=$normal.CompactSidebar;Width=$normal.ContentWidth}
Click $main 'Nav1';$null=Wait-Text $main '已找到登录状态';$null=Capture-State 71
Click $main 'Nav2';$null=Wait-Text $main '当前网络：';$null=Capture-State 72
Click $main 'Nav3';Expand '高级连接设置';$null=Id $main 'SaveSettings';$null=Capture-State 73
Click $main 'Nav0';Click $main 'ToggleSidebar';$rail=Capture-State 74
$checks.ManualSidebar=$rail.CompactSidebar
Click $main 'ToggleSidebar';Click $main 'ZoomIn';Click $main 'ZoomIn';Click $main 'ZoomIn';Click $main 'ZoomIn';Click $main 'ZoomIn';$zoomed=Capture-State 75
$checks.ZoomIn=@{Zoom=$zoomed.ZoomPercent;Compact=$zoomed.CompactSidebar;Columns=$zoomed.CardColumns};$checks.ZoomSaved=((Settings).ZoomPercent -eq $zoomed.ZoomPercent)
Click $main 'ZoomReset';$reset=Capture-State 76;$checks.ZoomReset=($reset.ZoomPercent -eq 100)
for($i=0;$i -lt 3;$i++){Click $main 'ZoomOut'};$small=Capture-State 77;$checks.ZoomOut=$small.ZoomPercent
Click $main 'ZoomReset';[AutoQa]::MoveWindow($handle,100,100,900,690,$true)|Out-Null;Start-Sleep -Milliseconds 500
for($i=0;$i -lt 4;$i++){Click $main ('Nav'+$i);$state=Capture-State (80+$i);$checks["Small$i"]=@{Width=$state.ContentWidth;Compact=$state.CompactSidebar;HeaderStacked=$state.HeaderStacked}}
Click $main 'Nav0';for($i=0;$i -lt 5;$i++){Click $main 'ZoomIn'};$maxSmall=Capture-State 84;$checks.SmallZoom150=@{Zoom=$maxSmall.ZoomPercent;Width=$maxSmall.ContentWidth;Columns=$maxSmall.CardColumns}
Click $main 'ZoomReset';[AutoQa]::MoveWindow($handle,100,100,1680,1200,$true)|Out-Null;Start-Sleep -Milliseconds 500
Click $main 'Nav0';Expand '自定义线路 · 可选';Click $main 'AddProfile';Capture 85
$dialogHandle=[AutoQa]::Find($TargetProcess,'添加线路');$dialog=[Windows.Automation.AutomationElement]::FromHandle($dialogHandle);Click $dialog 'CancelProfile'
$window=[Windows.Automation.WindowPattern]$main.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern);$window.SetWindowVisualState([Windows.Automation.WindowVisualState]::Maximized);Start-Sleep -Milliseconds 600;$max=Capture-State 86
$checks.Maximized=@{Width=$max.Width;Columns=$max.CardColumns;Zoom=$max.ZoomPercent}
$window.SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal);Start-Sleep -Milliseconds 500
Click $main 'Nav0'
$checks|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'native-responsive.json') -Encoding UTF8
$checks|ConvertTo-Json -Depth 5