param([switch]$RemoveUserData)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Migration.ps1')
$installDir=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$expected=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\QingDu')).TrimEnd('\')
if($installDir -ne $expected){throw 'Uninstall directory check failed'}
$exe=Join-Path $installDir 'QingDu.exe';if(Test-Path -LiteralPath $exe){& $exe --exit;foreach($p in @(Get-Process QingDu -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe})){if(-not $p.WaitForExit(8000)){throw 'QingDu is still running'}}}
foreach($name in @('轻读.lnk','轻读 v0.1.lnk')){$path=Join-Path ([Environment]::GetFolderPath('Desktop')) $name;if(Test-Path -LiteralPath $path){$shell=New-Object -ComObject WScript.Shell;if($shell.CreateShortcut($path).TargetPath -eq $exe){Remove-Item -LiteralPath $path -Force}}}
$menu=Join-Path ([Environment]::GetFolderPath('Programs')) '轻读'
if(Test-Path -LiteralPath $menu){Assert-QingDuPlainTree -Path $menu -Boundary ([Environment]::GetFolderPath('Programs')) | Out-Null;Remove-Item -LiteralPath $menu -Recurse -Force}
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QingDu' -Recurse -Force -ErrorAction SilentlyContinue
Assert-QingDuPlainTree -Path $installDir -Boundary (Split-Path $installDir) | Out-Null
Set-Location -LiteralPath $env:TEMP;Remove-Item -LiteralPath $installDir -Recurse -Force
if($RemoveUserData){$data=Join-Path $env:LOCALAPPDATA 'QingDu';if(Test-Path -LiteralPath $data){Assert-QingDuPlainTree -Path $data -Boundary $env:LOCALAPPDATA | Out-Null;Remove-Item -LiteralPath $data -Recurse -Force}}
Write-Output 'QingDu uninstalled. User data is retained unless -RemoveUserData was specified.'
