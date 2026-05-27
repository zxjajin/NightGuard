param(
    [switch]$Elevated
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-Elevated"
    )

    Start-Process -FilePath "powershell.exe" `
        -ArgumentList $arguments `
        -WorkingDirectory $projectDir `
        -Verb RunAs
    exit
}

Set-Location $projectDir

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet -and (Test-Path "C:\Program Files\dotnet\dotnet.exe")) {
    $dotnetPath = "C:\Program Files\dotnet\dotnet.exe"
}
elseif ($null -ne $dotnet) {
    $dotnetPath = $dotnet.Source
}
else {
    Write-Host ".NET 8 SDK not found. Please install it first." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Starting NightGuard as administrator..." -ForegroundColor Cyan
& $dotnetPath run --project "$projectDir\NightGuard.csproj"

if ($LASTEXITCODE -ne 0) {
    Write-Host "NightGuard exited with code $LASTEXITCODE." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
}
