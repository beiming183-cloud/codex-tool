[CmdletBinding()]
param([string]$DotnetPath='dotnet',[string]$OutputDirectory='', [string]$PackageDirectory='', [switch]$NoRestore)
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
if(-not $OutputDirectory){$OutputDirectory=Join-Path $projectRoot 'dist\release'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
[xml]$project=Get-Content -LiteralPath (Join-Path $projectRoot 'CodexToolbox.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Project version must be a stable three-part version.'}
$release=Join-Path $OutputDirectory $version
$publish=Join-Path $release 'publish'
$stage=Join-Path $release ([guid]::NewGuid().ToString('N'))
$portable=Join-Path $stage 'CodexToolbox-Windows';$source=Join-Path $stage 'CodexToolbox-source'
New-Item -ItemType Directory -Path $release,$portable,$source -Force | Out-Null
$arguments=@('publish',(Join-Path $projectRoot 'CodexToolbox.csproj'),'-c','Release','-o',$publish,'-p:UseSharedCompilation=false')
if($NoRestore){$arguments+='--no-restore'}
if($PackageDirectory){$arguments+=('-p:RestorePackagesPath='+[IO.Path]::GetFullPath($PackageDirectory))}
& $DotnetPath @arguments
if($LASTEXITCODE -ne 0){throw 'Publish failed.'}
$exe=Join-Path $publish 'CodexToolbox.exe'
$testResult=Join-Path ([IO.Path]::GetTempPath()) 'CodexToolbox-selftest.json'
if(Test-Path -LiteralPath $testResult){Remove-Item -LiteralPath $testResult}
$test=Start-Process -FilePath $exe -ArgumentList @('--self-test') -Wait -PassThru -WindowStyle Hidden
$result=$null
for($attempt=0;$attempt -lt 200;$attempt++){try{if(Test-Path -LiteralPath $testResult){$result=Get-Content -LiteralPath $testResult -Raw|ConvertFrom-Json;break}}catch{};Start-Sleep -Milliseconds 100}
if($test.ExitCode -ne 0 -or -not $result -or -not $result.Passed){throw 'Self-test failed; release was not packaged.'}
Copy-Item -LiteralPath $testResult -Destination (Join-Path $release 'selftest.json')
Copy-Item -LiteralPath $exe -Destination (Join-Path $portable 'Codex工具箱.exe')
foreach($name in @('README.md','LICENSE')){Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $portable}
$assets=Get-Content -LiteralPath (Join-Path $projectRoot 'obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoot=$assets.packageFolders.PSObject.Properties.Name|Select-Object -First 1
$dependencies=@($assets.project.frameworks.PSObject.Properties|ForEach-Object {$_.Value.downloadDependencies})
$notices=Join-Path $portable 'runtime-notices';New-Item -ItemType Directory -Path $notices -Force|Out-Null
foreach($id in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')){
    $dependency=$dependencies|Where-Object {$_.name -eq $id}|Select-Object -First 1
    if(-not $dependency -or $dependency.version -notmatch '\d+\.\d+\.\d+'){throw "Runtime license version not found for $id"}
    $runtime=Join-Path (Join-Path $packageRoot $id.ToLowerInvariant()) $Matches[0]
    $license=Join-Path $runtime 'LICENSE.TXT';if(-not(Test-Path -LiteralPath $license)){$license=Join-Path $runtime 'LICENSE'};if(-not(Test-Path -LiteralPath $license)){throw "Runtime license missing for $id"}
    Copy-Item -LiteralPath $license -Destination (Join-Path $notices ($id+'-LICENSE.txt'))
    $thirdParty=Join-Path $runtime 'THIRD-PARTY-NOTICES.TXT';if(Test-Path -LiteralPath $thirdParty){Copy-Item -LiteralPath $thirdParty -Destination (Join-Path $notices ($id+'-THIRD-PARTY-NOTICES.txt'))}
}
Get-ChildItem -LiteralPath $projectRoot -File | Where-Object {$_.Extension -in @('.cs','.csproj','.xaml','.manifest','.ico','.md') -or $_.Name -in @('LICENSE','.gitignore','build.ps1','global.json')} | Copy-Item -Destination $source
New-Item -ItemType Directory -Path (Join-Path $source 'qa'),(Join-Path $source '.impeccable') -Force | Out-Null
foreach($name in @('Native-Simple.ps1','Native-Responsive.ps1','Native-Updates.ps1')){if(Test-Path -LiteralPath (Join-Path $projectRoot ('qa\'+$name))){Copy-Item -LiteralPath (Join-Path $projectRoot ('qa\'+$name)) -Destination (Join-Path $source 'qa')}}
foreach($name in @('design.json','surface.md')){Copy-Item -LiteralPath (Join-Path $projectRoot ('.impeccable\'+$name)) -Destination (Join-Path $source '.impeccable')}
if(Test-Path -LiteralPath (Join-Path $projectRoot 'docs')){Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $source -Recurse}
if(Test-Path -LiteralPath (Join-Path $projectRoot '.github')){Copy-Item -LiteralPath (Join-Path $projectRoot '.github') -Destination $source -Recurse}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$portableZip=Join-Path $release "CodexToolbox-$version-win-x64.zip";$sourceZip=Join-Path $release "CodexToolbox-$version-source.zip"
foreach($archive in @($portableZip,$sourceZip)){if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive}}
[IO.Compression.ZipFile]::CreateFromDirectory($portable,$portableZip,[IO.Compression.CompressionLevel]::Optimal,$true)
[IO.Compression.ZipFile]::CreateFromDirectory($source,$sourceZip,[IO.Compression.CompressionLevel]::Optimal,$true)
$hashes=@($portableZip,$sourceZip)|ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($_)}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
Write-Output "Release $version is ready: $release ($($result.Checks.Count) checks passed)."
