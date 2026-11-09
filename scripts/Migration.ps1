function Assert-QingDuPlainTree {
    param([Parameter(Mandatory=$true)][string]$Path,[Parameter(Mandatory=$true)][string]$Boundary)
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\');$root=[IO.Path]::GetFullPath($Boundary).TrimEnd('\')
    if(-not $full.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup path is outside the installation boundary'}
    $cursor=$full
    while($cursor.Length -ge $root.Length){
        if(Test-Path -LiteralPath $cursor){$item=Get-Item -LiteralPath $cursor -Force;if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw ('Refusing directory link: '+$cursor)}}
        if($cursor -eq $root){break};$cursor=[IO.Path]::GetDirectoryName($cursor)
    }
    if(Test-Path -LiteralPath $full){
        $queue=New-Object 'Collections.Generic.Queue[string]';$queue.Enqueue($full)
        while($queue.Count -gt 0){$current=$queue.Dequeue();$item=Get-Item -LiteralPath $current -Force;if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw ('Refusing directory link: '+$current)};if($item.PSIsContainer){foreach($child in Get-ChildItem -LiteralPath $current -Force){$queue.Enqueue($child.FullName)}}}
    }
    return $full
}
function Initialize-QingDuData {
    param([string]$InstallDir,[string]$UserDataDir,[string]$DefaultsFile,[switch]$ResetSettings)
    Add-Type -AssemblyName System.Security
    New-Item -ItemType Directory -Path $UserDataDir -Force | Out-Null
    $legacy=Join-Path $InstallDir 'data';$settings=Join-Path $UserDataDir 'settings.json';$backup=$null
    if(Test-Path -LiteralPath $legacy){
        Assert-QingDuPlainTree -Path $legacy -Boundary $InstallDir | Out-Null
        $backup=Join-Path $UserDataDir ('migration-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
        New-Item -ItemType Directory -Path $backup -Force | Out-Null
        if(Test-Path -LiteralPath $settings){Copy-Item -LiteralPath $settings -Destination (Join-Path $backup 'previous-user-settings.json')}
        $oldSettings=Join-Path $legacy 'settings.json';if(Test-Path -LiteralPath $oldSettings){Copy-Item -LiteralPath $oldSettings -Destination (Join-Path $backup 'legacy-settings.json')}
        if(Test-Path -LiteralPath (Join-Path $legacy 'logs')){Copy-Item -LiteralPath (Join-Path $legacy 'logs') -Destination $backup -Recurse}
        foreach($name in @('fish-key.bin','mimo-key.bin')){
            $source=Join-Path $legacy $name;$target=Join-Path $UserDataDir $name
            if(Test-Path -LiteralPath $source){
                $original=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($source),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
                try{
                    Copy-Item -LiteralPath $source -Destination (Join-Path $backup $name)
                    if(Test-Path -LiteralPath $target){Copy-Item -LiteralPath $target -Destination (Join-Path $backup ('previous-'+$name))}
                    Copy-Item -LiteralPath $source -Destination $target -Force
                    $verified=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($target),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
                    try{if([Convert]::ToBase64String($original) -cne [Convert]::ToBase64String($verified)){throw 'Encrypted key migration verification failed'}}finally{[Array]::Clear($verified,0,$verified.Length)}
                }finally{[Array]::Clear($original,0,$original.Length)}
            }
        }
    }
    if($ResetSettings -or -not (Test-Path -LiteralPath $settings)){
        if($ResetSettings -and (Test-Path -LiteralPath $settings) -and -not $backup){$backup=Join-Path $UserDataDir ('settings-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'));New-Item -ItemType Directory -Path $backup -Force | Out-Null;Copy-Item -LiteralPath $settings -Destination $backup}
        Copy-Item -LiteralPath $DefaultsFile -Destination $settings -Force
    }
    # Validate retained credentials without printing their plaintext or hashes.
    foreach($name in @('fish-key.bin','mimo-key.bin')){$file=Join-Path $UserDataDir $name;if(Test-Path -LiteralPath $file){$plain=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($file),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser);[Array]::Clear($plain,0,$plain.Length)}}
    return $backup
}
function Remove-QingDuLegacy {
    param([string]$InstallDir)
    $names=@('engine','python','voices','__pycache__','diagnostics','data','backend.py','speech_plan.py','environment.json','install_runtime.py','QingDu.cs','Audio.cs','Fish.cs','FishReader.cs','Mimo.cs','MimoPlan.cs','Build.ps1','使用说明.txt','sha256.json')
    $targets=@();foreach($name in $names){$path=Join-Path $InstallDir $name;if(Test-Path -LiteralPath $path){$targets+=Assert-QingDuPlainTree -Path $path -Boundary $InstallDir}}
    foreach($path in $targets){Remove-Item -LiteralPath $path -Recurse -Force}
    return $targets.Count
}
