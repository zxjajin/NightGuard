$ErrorActionPreference = "SilentlyContinue"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Start-Process -FilePath "powershell.exe" `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"") `
        -Verb RunAs
    exit
}

Get-Process NightGuard | Stop-Process -Force

$hosts = "C:\Windows\System32\drivers\etc\hosts"
if (Test-Path $hosts) {
    $text = Get-Content -Raw -LiteralPath $hosts
    $pattern = "(?s)\r?\n?# NightGuard BEGIN\r?\n.*?# NightGuard END\r?\n?"
    $new = [regex]::Replace($text, $pattern, [Environment]::NewLine)
    if ($new -ne $text) {
        Set-Content -LiteralPath $hosts -Value $new -Encoding ASCII
    }
}

schtasks.exe /Change /TN NightGuard /DISABLE | Out-Null
schtasks.exe /Delete /TN NightGuard /F | Out-Null
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v NightGuard /f | Out-Null
ipconfig /flushdns | Out-Null

Write-Host "NightGuard emergency restore completed." -ForegroundColor Green
Read-Host "Press Enter to exit"
