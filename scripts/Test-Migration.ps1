$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Migration.ps1')
Add-Type -AssemblyName System.Security
$root=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) ('build\migration-test-'+[Guid]::NewGuid().ToString('N'))
$install=Join-Path $root 'install';$data=Join-Path $root 'user';$legacy=Join-Path $install 'data'
New-Item -ItemType Directory -Path $legacy -Force | Out-Null
$defaults=Join-Path $root 'defaults.json';[IO.File]::WriteAllText($defaults,'{"Provider":"mimo","MimoVoice":"冰糖"}')
[IO.File]::WriteAllText((Join-Path $legacy 'settings.json'),'{"FishVoices":[{"id":"fixture","name":"Test voice"}]}')
foreach($name in @('fish-key.bin','mimo-key.bin')){$raw=[Text.Encoding]::UTF8.GetBytes('migration-test-placeholder');$secret=[Security.Cryptography.ProtectedData]::Protect($raw,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);[IO.File]::WriteAllBytes((Join-Path $legacy $name),$secret)}
$backup=Initialize-QingDuData -InstallDir $install -UserDataDir $data -DefaultsFile $defaults -ResetSettings
if(-not (Test-Path -LiteralPath (Join-Path $backup 'legacy-settings.json'))){throw 'Missing migration backup'}
foreach($name in @('fish-key.bin','mimo-key.bin')){$a=[IO.File]::ReadAllBytes((Join-Path $data $name));$b=[IO.File]::ReadAllBytes((Join-Path $legacy $name));if([Convert]::ToBase64String($a) -cne [Convert]::ToBase64String($b)){throw 'Key migration mismatch'}}
if([IO.File]::ReadAllText((Join-Path $data 'settings.json')) -cne [IO.File]::ReadAllText($defaults)){throw 'Default reset failed'}
$rejected=$false;try{Assert-QingDuPlainTree -Path $root -Boundary $install | Out-Null}catch{$rejected=$true};if(-not $rejected){throw 'Boundary check failed'}
New-Item -ItemType Directory -Path (Join-Path $install 'engine') -Force | Out-Null
$external=Join-Path $root 'external';New-Item -ItemType Directory -Path $external -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $external 'keep.txt'),'keep')
$link=Join-Path $install 'link-test';New-Item -ItemType Junction -Path $link -Target $external | Out-Null
$rejected=$false;try{Assert-QingDuPlainTree -Path $link -Boundary $install | Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Directory link was not rejected'}
# Remove only the verified fixture junction itself, never recurse through it.
if(([IO.Path]::GetFullPath($link)).StartsWith([IO.Path]::GetFullPath($install)+'\',[StringComparison]::OrdinalIgnoreCase)){[IO.Directory]::Delete($link)}else{throw 'Fixture link boundary mismatch'}
if(-not (Test-Path -LiteralPath (Join-Path $external 'keep.txt'))){throw 'External fixture damaged'}
$removed=Remove-QingDuLegacy -InstallDir $install
if($removed -ne 2 -or (Test-Path -LiteralPath $legacy) -or -not (Test-Path -LiteralPath (Join-Path $data 'fish-key.bin'))){throw 'Cleanup failed'}
Write-Output 'PASS DPAPI migration, private backup, default reset, cleanup boundary and retained user data'
