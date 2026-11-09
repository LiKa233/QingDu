param([string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version=(Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid VERSION'}
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root 'build\app'}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$generated=Join-Path $OutputDirectory 'Version.g.cs'
$code=@"
using System.Reflection;
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
[assembly: AssemblyInformationalVersion("$version")]
namespace QingDu { public static class AppVersion { public const string Value = "$version"; } }
"@
[IO.File]::WriteAllText($generated,$code,(New-Object Text.UTF8Encoding($true)))
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object {$_.FullName})
$arguments=@('/nologo','/target:winexe','/platform:x64','/optimize+',('/out:'+(Join-Path $OutputDirectory 'QingDu.exe')),('/win32icon:'+(Join-Path $root 'assets\QingDu.ico')),'/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Security.dll','/reference:System.Core.dll')+$sources+@($generated)
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Application build failed'}
Copy-Item -LiteralPath (Join-Path $root 'QingDu.exe.config') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $root 'assets') -Destination $OutputDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'VERSION') -Destination $OutputDirectory -Force
Write-Output "Built QingDu $version (Windows x64)"
