# DEV helper for runs from a build folder (the installer registers the host since step 2.6): registers the shim as the Native Messaging host
# "com.ytdlppro.host" for the CURRENT USER in Chrome, Edge and Brave, so the extension can talk to the running app.
#   .\tools\register-native-host-dev.ps1 -Shim .\shim-publish\ytdlp-shim.exe       register
#   .\tools\register-native-host-dev.ps1 -Remove                                   unregister
# What it writes: native-host-manifest.json and allowed-origins.txt NEXT TO the shim exe, and one registry value per browser under
# HKCU\Software\<browser>\NativeMessagingHosts\com.ytdlppro.host (no administrator rights needed).
# Put the shim next to YTDLPHost.exe (copy both into one folder) if you want the shim to start the app by itself.
param(
    [string]$Shim = "",
    [switch]$Remove,
    # The extension id comes from the "key" in extension/manifest.json (stable across machines and "Load unpacked" folders).
    [string]$ExtensionId = "onlglcidnadbeilfhbahjodnhonhiieb"
)

$hostName = "com.ytdlppro.host"
$keys = @(
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName",
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$hostName",
    "HKCU:\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\$hostName"
)

if ($Remove) {
    foreach ($key in $keys) { if (Test-Path $key) { Remove-Item $key -Force; Write-Host "Removed $key" } }
    return
}

if ($Shim -eq "" -or -not (Test-Path $Shim)) { Write-Error "Give the path of ytdlp-shim.exe with -Shim"; exit 1 }
$shimPath = (Resolve-Path $Shim).Path
$dir = Split-Path $shimPath
$origin = "chrome-extension://$ExtensionId/"

$manifest = [ordered]@{
    name            = $hostName
    description     = "YT Downloader Pro"
    path            = $shimPath
    type            = "stdio"
    allowed_origins = @($origin)
}
$manifestPath = Join-Path $dir "native-host-manifest.json"
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding UTF8
Set-Content -Path (Join-Path $dir "allowed-origins.txt") -Value $origin -Encoding ASCII

foreach ($key in $keys) {
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name "(default)" -Value $manifestPath
    Write-Host "Registered $key"
}
Write-Host "Manifest: $manifestPath"
Write-Host "Allowed origin: $origin"
Write-Host "Restart the browser, reload the extension, and keep YT Downloader Pro running (or put the shim next to YTDLPHost.exe)."
