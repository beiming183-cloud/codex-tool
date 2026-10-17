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
Click $main 'Nav3'
$button=Id $main 'CheckUpdates'
for($wait=0;$wait -lt 100;$wait++){if($button.Current.IsEnabled){break};Start-Sleep -Milliseconds 200}
Click $main 'CheckUpdates'
$values=Wait-Text $main '已是最新版本|暂未发布可安装|新版 v'
Capture 100
$status=Id $main 'UpdateStatus';$zoom=Id $main 'ZoomReset'
$checks=@{CheckButtonResponded=$button.Current.IsEnabled;VisibleStatus=$status.Current.Name;ZoomControlVisible=(-not $zoom.Current.IsOffscreen)}
$checks|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'native-updates.json') -Encoding UTF8
$checks|ConvertTo-Json
Click $main 'Nav0'