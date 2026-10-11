$ErrorActionPreference = "Stop"
$url = "https://github.com/akrram0/noti/releases/download/latest/Noti-Setup-win-x64.exe"
$dest = Join-Path $env:TEMP "Noti-Setup-win-x64.exe"

Write-Host "Downloading Noti Setup..." -ForegroundColor Cyan
$client = New-Object System.Net.Http.HttpClient
$bytes = $client.GetByteArrayAsync($url).GetAwaiter().GetResult()
[System.IO.File]::WriteAllBytes($dest, $bytes)
Unblock-File -Path $dest -ErrorAction SilentlyContinue

Write-Host "Launching Noti Setup..." -ForegroundColor Green
Start-Process -FilePath $dest -Wait
Remove-Item -Path $dest -Force -ErrorAction SilentlyContinue
