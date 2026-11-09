param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath($Directory)
$issues=New-Object 'Collections.Generic.List[string]'
function Test-ReleaseBytes([string]$name,[byte[]]$bytes){
    if($name -match '(?i)(^|[/\\])(settings\.json|[^/\\]*-key\.bin|\.env(?:\..*)?|[^/\\]*\.log|appdata|diagnostics|migration-backup[^/\\]*|voices)([/\\]|$)'){$issues.Add('Forbidden private filename: '+$name)}
    foreach($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)){
        $content=$encoding.GetString($bytes)
        if($content -match '(?i)\bsk-[A-Za-z0-9_-]{20,}'){$issues.Add('Credential-shaped string: '+$name)}
        if($content -match '(?i)(?:[A-Z]:[/\\]|/c/)[Uu]sers[/\\][A-Za-z0-9_. -]+[/\\]'){$issues.Add('Machine user path: '+$name)}
        if($content -match '-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----'){$issues.Add('Private key: '+$name)}
    }
}
foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force){
    if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Release tree contains a link'}
    $name=$file.FullName.Substring($root.Length+1)
    if($file.Extension -eq '.zip'){
        $zip=[IO.Compression.ZipFile]::OpenRead($file.FullName)
        try{foreach($entry in $zip.Entries){if($entry.Length -gt 0){$stream=$entry.Open();$memory=New-Object IO.MemoryStream;try{$stream.CopyTo($memory);Test-ReleaseBytes ($name+'!'+$entry.FullName) $memory.ToArray()}finally{$memory.Dispose();$stream.Dispose()}}}}finally{$zip.Dispose()}
    }else{Test-ReleaseBytes $name ([IO.File]::ReadAllBytes($file.FullName))}
}
if($issues.Count -gt 0){$issues | Select-Object -Unique | Write-Output;throw 'Release privacy scan failed (matches intentionally not printed)'}
Write-Output 'PASS release directory and ZIP privacy scan: no credential-shaped strings, private-data filenames, private keys or machine user paths'
