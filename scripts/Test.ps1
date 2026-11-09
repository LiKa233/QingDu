param([string]$Ffmpeg='',[switch]$SkipPlayback)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(-not $Ffmpeg){$Ffmpeg=Join-Path $root 'build\media\ffmpeg.exe'}
& (Join-Path $PSScriptRoot 'Build.ps1')
$out=Join-Path $root 'build\app'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /platform:x64 ('/out:'+(Join-Path $out 'Tests.exe')) ('/reference:'+(Join-Path $out 'QingDu.exe')) /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll (Join-Path $root 'tests\Tests.cs')
if($LASTEXITCODE -ne 0){throw 'Test build failed'}
Copy-Item -LiteralPath (Join-Path $root 'tests\tone.mp3') -Destination $out -Force
$scratch=Join-Path $root ('build\test-data-'+[Guid]::NewGuid().ToString('N'))
& (Join-Path $out 'Tests.exe') $scratch $Ffmpeg
if($LASTEXITCODE -ne 0){throw 'Contract tests failed'}
& (Join-Path $PSScriptRoot 'Test-Migration.ps1')
& $csc /nologo /target:exe /platform:x64 ('/out:'+(Join-Path $out 'AudioChecks.exe')) ('/reference:'+(Join-Path $out 'QingDu.exe')) (Join-Path $root 'tests\AudioChecks.cs')
if($LASTEXITCODE -ne 0){throw 'Audio test build failed'}
if(-not $SkipPlayback){ & (Join-Path $out 'AudioChecks.exe')
if($LASTEXITCODE -ne 0){throw 'Audio playback checks failed'} }else{Write-Output 'SKIP hardware audio checks (explicit headless CI option)'}
