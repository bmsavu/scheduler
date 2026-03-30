#Requires -Version 5.1
<#
.SYNOPSIS
    Deploy AbsolutNewUI to IIS on 10.151.1.129
.DESCRIPTION
    1. dotnet publish (Release)
    2. Connect to server via PS Remoting
    3. Stop app pool
    4. Copy files
    5. Start app pool
.USAGE
    .\deploy.ps1
#>

$ErrorActionPreference = 'Stop'
$server = '10.151.1.129'
$deployUser = "$server\deployer"
$deployPass = 'Deploy2026ASEE'
$solutionDir = $PSScriptRoot
$publishDir = "$solutionDir\publish"
$remoteWebPath = 'C:\_bogdans\AbsolutNewUI'

$totalSw = [System.Diagnostics.Stopwatch]::StartNew()
$stepTimes = @{}

# --- Credentials ---
try {
    $secPass = ConvertTo-SecureString $deployPass -AsPlainText -Force
} catch {
    $secPass = New-Object System.Security.SecureString
    foreach ($c in $deployPass.ToCharArray()) { $secPass.AppendChar($c) }
    $secPass.MakeReadOnly()
}
$cred = New-Object System.Management.Automation.PSCredential($deployUser, $secPass)

# --- Step 1: Publish ---
$stepSw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " STEP 1: Publishing Absolut.Web" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

Write-Host "`n>> Bumping version..." -ForegroundColor Yellow
$versionStr = & "$solutionDir\bump-version.ps1"
Write-Host "   Version: v$versionStr" -ForegroundColor Green

Write-Host "`n>> Publishing Absolut.Web..." -ForegroundColor Yellow
dotnet publish "$solutionDir\src\Absolut.Web\Absolut.Web.csproj" -c Release -o "$publishDir\web" --no-restore
if ($LASTEXITCODE -ne 0) { throw "Web publish failed" }
Write-Host "   Web published OK" -ForegroundColor Green

$stepTimes['Publish'] = $stepSw.Elapsed.ToString('mm\:ss')

# --- Step 2: Stop App Pool ---
$stepSw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " STEP 2: Stopping app pool on server" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

Invoke-Command -ComputerName $server -Credential $cred -ScriptBlock {
    Import-Module WebAdministration
    Write-Host "   Stopping AbsolutNewUI..." -ForegroundColor Yellow
    Stop-WebAppPool -Name 'AbsolutNewUI' -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    # Kill any remaining w3wp.exe workers holding file locks
    Get-Process w3wp -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $env = ($_.Modules | Where-Object { $_.FileName -like '*AbsolutNewUI*' })
            if ($env -or $_.CommandLine -like '*AbsolutNewUI*') { $_.Kill() }
        } catch {}
    }
    Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Host "   App pool stopped and processes killed" -ForegroundColor Green
}

$stepTimes['Stop pool'] = $stepSw.Elapsed.ToString('mm\:ss')

# --- Step 3: Copy files ---
$stepSw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " STEP 3: Copying files to server" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$session = New-PSSession -ComputerName $server -Credential $cred

function Copy-WithProgress {
    param($Label, $SourceDir, $RemotePath, $Session)

    $files = Get-ChildItem $SourceDir -Recurse -File
    $total = $files.Count
    if ($total -eq 0) { Write-Host "   No files to copy." -ForegroundColor Yellow; return }

    # Pre-create all remote directories in one call
    $dirs = $files | ForEach-Object {
        $rel = $_.FullName.Substring("$SourceDir\".Length)
        Split-Path (Join-Path $RemotePath $rel) -Parent
    } | Sort-Object -Unique
    Invoke-Command -Session $Session -ScriptBlock {
        param($dirs)
        foreach ($d in $dirs) {
            if (-not (Test-Path $d)) { New-Item -Path $d -ItemType Directory -Force | Out-Null }
        }
    } -ArgumentList (,$dirs)

    # Copy files with inline progress
    $copied = 0
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $consoleWidth = [math]::Max(($Host.UI.RawUI.WindowSize.Width - 1), 60)
    $barWidth = 30
    $oldProgress = $global:ProgressPreference
    $global:ProgressPreference = 'SilentlyContinue'
    Write-Host ""
    foreach ($file in $files) {
        $copied++
        $relativePath = $file.FullName.Substring("$SourceDir\".Length)
        $remotePath2 = Join-Path $RemotePath $relativePath
        Copy-Item -Path $file.FullName -Destination $remotePath2 -ToSession $Session -Force *>$null
        $pct     = [math]::Round($copied / $total * 100)
        $filled  = [math]::Floor($pct * $barWidth / 100)
        $empty   = $barWidth - $filled
        $bar     = ([char]0x2588).ToString() * $filled + ([char]0x2591).ToString() * $empty
        $elapsed = $sw.Elapsed.ToString('mm\:ss')
        $name    = $file.Name
        $prefix  = "   $bar $($pct.ToString().PadLeft(3))% ($copied/$total) $elapsed "
        $maxName = $consoleWidth - $prefix.Length
        if ($maxName -gt 0 -and $name.Length -gt $maxName) { $name = $name.Substring(0, $maxName) }
        $line    = "$prefix$name".PadRight($consoleWidth)
        if ($line.Length -gt $consoleWidth) { $line = $line.Substring(0, $consoleWidth) }
        [Console]::Write("`r$line")
    }
    $global:ProgressPreference = $oldProgress
    $sw.Stop()
    [Console]::Write("`r" + (' ' * $consoleWidth) + "`r")
    Write-Host "   $Label files copied: $total files in $($sw.Elapsed.ToString('mm\:ss'))" -ForegroundColor Green
}

Write-Host "`n>> Copying Web files..." -ForegroundColor Yellow
Copy-WithProgress -Label "Web" -SourceDir "$publishDir\web" -RemotePath $remoteWebPath -Session $session

Remove-PSSession $session
$stepTimes['Copy files'] = $stepSw.Elapsed.ToString('mm\:ss')

# --- Step 4: Start App Pool ---
$stepSw = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " STEP 4: Starting app pool on server" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

Invoke-Command -ComputerName $server -Credential $cred -ScriptBlock {
    Import-Module WebAdministration
    Write-Host "   Starting AbsolutNewUI..." -ForegroundColor Yellow
    Start-WebAppPool -Name 'AbsolutNewUI'
    Start-Sleep -Seconds 1
    Get-ChildItem IIS:\AppPools | Where-Object { $_.Name -like 'AbsolutNewUI*' } |
        Select-Object Name, State | Format-Table -AutoSize
}

$stepTimes['Start pool'] = $stepSw.Elapsed.ToString('mm\:ss')
$totalSw.Stop()

Write-Host "`n========================================" -ForegroundColor Green
Write-Host " DEPLOY COMPLETE!  v$versionStr" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Step Summary:" -ForegroundColor Cyan
foreach ($k in @('Publish','Stop pool','Copy files','Start pool')) {
    Write-Host ("    {0,-14} {1}" -f "$($k):", $stepTimes[$k]) -ForegroundColor White
}
Write-Host ("    {0,-14} {1}" -f "TOTAL:", $totalSw.Elapsed.ToString('mm\:ss')) -ForegroundColor Yellow
Write-Host ""
Write-Host "  https://10.151.1.129/AbsolutNewUI/" -ForegroundColor Green
Write-Host "========================================`n" -ForegroundColor Green
