param([switch]$ResetSettings)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Migration.ps1')
$package=$PSScriptRoot
$version=(Get-Content -LiteralPath (Join-Path $package 'VERSION') -Raw).Trim()
$installDir=Join-Path $env:LOCALAPPDATA 'Programs\QingDu'
$userData=Join-Path $env:LOCALAPPDATA 'QingDu'
$exe=Join-Path $installDir 'QingDu.exe'
foreach($file in @('QingDu.exe','QingDu.exe.config','settings.defaults.json','bin\ffmpeg.exe','assets\QingDu.ico','LICENSE','THIRD-PARTY-NOTICES.md')){if(-not (Test-Path -LiteralPath (Join-Path $package $file))){throw ('Incomplete release package: '+$file)}}
if(Test-Path -LiteralPath $exe){& $exe --exit;foreach($p in @(Get-Process QingDu -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe})){if(-not $p.WaitForExit(8000)){throw 'Please close QingDu before installing'}}}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
$backup=Initialize-QingDuData -InstallDir $installDir -UserDataDir $userData -DefaultsFile (Join-Path $package 'settings.defaults.json') -ResetSettings:$ResetSettings
$removed=Remove-QingDuLegacy -InstallDir $installDir
foreach($name in @('QingDu.exe','QingDu.exe.config','VERSION','settings.defaults.json','Install.ps1','Migration.ps1','Uninstall.ps1','Install.cmd','README.md','LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $installDir $name) -Force}
foreach($name in @('assets','bin','licenses')){Copy-Item -LiteralPath (Join-Path $package $name) -Destination $installDir -Recurse -Force}
$shell=New-Object -ComObject WScript.Shell
$desktop=[Environment]::GetFolderPath('Desktop');$menu=Join-Path ([Environment]::GetFolderPath('Programs')) '轻读';New-Item -ItemType Directory -Path $menu -Force | Out-Null
$icon=Join-Path $installDir 'assets\QingDu.ico'
foreach($path in @((Join-Path $desktop '轻读.lnk'),(Join-Path $menu '轻读.lnk'))){$link=$shell.CreateShortcut($path);$link.TargetPath=$exe;$link.WorkingDirectory=$installDir;$link.Description='选中文字，按自定义快捷键朗读';$link.IconLocation="$icon,0";$link.Save()}
$old=Join-Path $desktop '轻读 v0.1.lnk';if(Test-Path -LiteralPath $old){$link=$shell.CreateShortcut($old);if($link.TargetPath -eq $exe){Remove-Item -LiteralPath $old -Force}}
$pinDir=Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar';if(Test-Path -LiteralPath $pinDir){foreach($file in Get-ChildItem -LiteralPath $pinDir -Filter '*.lnk'){$link=$shell.CreateShortcut($file.FullName);if($link.TargetPath -eq $exe){$link.IconLocation="$icon,0";$link.Save()}}}
$ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe';$uninstall=Join-Path $installDir 'Uninstall.ps1'
$link=$shell.CreateShortcut((Join-Path $menu '卸载轻读.lnk'));$link.TargetPath=$ps;$link.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$uninstall+'"';$link.IconLocation="$icon,0";$link.Save()
$reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\QingDu';New-Item -Path $reg -Force | Out-Null
(@{DisplayName='轻读';DisplayVersion=$version;Publisher='QingDu contributors';InstallLocation=$installDir;DisplayIcon=$icon;UninstallString='"'+$ps+'" -NoProfile -ExecutionPolicy Bypass -File "'+$uninstall+'"'}).GetEnumerator() | ForEach-Object {New-ItemProperty -Path $reg -Name $_.Key -Value $_.Value -PropertyType String -Force | Out-Null}
Write-Output "Installed QingDu $version. Removed $removed legacy entries. User data: $userData"
if($backup){Write-Output 'Previous settings and encrypted credentials were backed up in the private user data directory.'}
