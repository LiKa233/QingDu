param([string]$Ffmpeg='')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version=(Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid VERSION'}
if(-not $Ffmpeg){$Ffmpeg=Join-Path $root 'build\media\ffmpeg.exe'}
foreach($file in @('ffmpeg-9.0.2.tar.xz','rubberband-3.3.0.tar.bz2')){if(-not (Test-Path -LiteralPath (Join-Path $root ('third_party\sources\'+$file)))){throw 'Corresponding source missing'}}
if(-not (Test-Path -LiteralPath $Ffmpeg)){throw 'Audio component missing'}
if((Get-FileHash -LiteralPath (Join-Path $root 'third_party\sources\ffmpeg-9.0.2.tar.xz')).Hash -ne '8C3850283EB25FA026482078A04051E0BE17347B09EF81A0849BEC15A96E002E'){throw 'FFmpeg source hash mismatch'}
if((Get-FileHash -LiteralPath (Join-Path $root 'third_party\sources\rubberband-3.3.0.tar.bz2')).Hash -ne 'D9EF89E2B8EF9F85B13AC3C2FAEC30E20ACF2C9F3A9C8C45CE637F2BC95E576C'){throw 'Rubber Band source hash mismatch'}
& (Join-Path $PSScriptRoot 'Build.ps1')
$stage=Join-Path $root ('build\release-'+[Guid]::NewGuid().ToString('N'))
$runtime=Join-Path $stage 'runtime';$source=Join-Path $stage 'source';$corresponding=Join-Path $stage 'corresponding-source'
foreach($p in @($runtime,$source,$corresponding,(Join-Path $runtime 'bin'))){New-Item -ItemType Directory -Path $p -Force | Out-Null}
# Source is always assembled from this explicit allowlist, never the workspace.
foreach($name in @('src','assets','scripts','tests','licenses','third_party','.github','VERSION','QingDu.exe.config','settings.defaults.json','README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md','VALIDATION.md','PUBLISH.md','.gitignore')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $source $name) -Recurse -Force}
foreach($name in @('QingDu.exe','QingDu.exe.config')){Copy-Item -LiteralPath (Join-Path $root ('build\app\'+$name)) -Destination $runtime}
foreach($name in @('assets','licenses','VERSION','settings.defaults.json','README.md','LICENSE','THIRD-PARTY-NOTICES.md','VALIDATION.md')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $runtime $name) -Recurse -Force}
foreach($name in @('Install.ps1','Migration.ps1','Uninstall.ps1','Install.cmd')){Copy-Item -LiteralPath (Join-Path $root ('scripts\'+$name)) -Destination $runtime}
Copy-Item -LiteralPath $Ffmpeg -Destination (Join-Path $runtime 'bin\ffmpeg.exe')
foreach($name in @('third_party','licenses','LICENSE','THIRD-PARTY-NOTICES.md')){Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $corresponding $name) -Recurse -Force}
New-Item -ItemType Directory -Path (Join-Path $corresponding 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build-media.sh') -Destination (Join-Path $corresponding 'scripts')
$manifest=@();foreach($file in Get-ChildItem -LiteralPath $runtime -Recurse -File){$manifest+=((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLower()+'  '+$file.FullName.Substring($runtime.Length+1).Replace('\','/'))}
[IO.File]::WriteAllLines((Join-Path $runtime 'FILES.sha256'),$manifest,(New-Object Text.UTF8Encoding($false)))
& (Join-Path $PSScriptRoot 'Scan-Release.ps1') -Directory $stage
$dist=Join-Path $root 'dist';New-Item -ItemType Directory -Path $dist -Force | Out-Null
$files=@{('QingDu-v'+$version+'-windows-x64.zip')=$runtime;('QingDu-v'+$version+'-source.zip')=$source;('QingDu-v'+$version+'-corresponding-source.zip')=$corresponding}
# A published version is immutable. Refuse accidental replacement of existing files.
foreach($name in $files.Keys){$target=Join-Path $dist $name;if(Test-Path -LiteralPath $target){throw ('Artifact already exists: '+$name)};[IO.Compression.ZipFile]::CreateFromDirectory($files[$name],$target,[IO.Compression.CompressionLevel]::Optimal,$false)}
$sums=@();foreach($name in $files.Keys | Sort-Object){$sums+=((Get-FileHash -LiteralPath (Join-Path $dist $name)).Hash.ToLower()+'  '+$name)}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'),$sums,(New-Object Text.UTF8Encoding($false)))
& (Join-Path $PSScriptRoot 'Scan-Release.ps1') -Directory $dist
Write-Output ('Release staged at '+$stage)
Write-Output ('Release artifacts: '+$dist)
