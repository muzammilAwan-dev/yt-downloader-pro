# Manual check for step 2.3 (no shim exists yet): talks to the RUNNING app over its Native Messaging pipe, the way the shim will.
#   .\tools\native-pipe-probe.ps1                       -> sends "hello" and prints the reply
#   .\tools\native-pipe-probe.ps1 -Url <video link>      -> also starts a download and prints every message until done/error
# Needs the app to be running (as the same user, not elevated if the app is not elevated).
param(
    [string]$Url = "",
    [string]$Quality = "720",
    [int]$TimeoutSeconds = 120
)

$user = ($env:USERNAME -replace '[^A-Za-z0-9_.-]', '')
if ($user.Length -gt 32) { $user = $user.Substring(0, 32) }
if ($user.Length -eq 0) { $user = 'user' }
$name = "YTDLPHost_Native_$user"

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $name, [System.IO.Pipes.PipeDirection]::InOut)
try { $pipe.Connect(3000) } catch { Write-Error "Could not connect to pipe '$name'. Is YT Downloader Pro running?"; exit 1 }

function Send-Frame([string]$json) {
    $body = [System.Text.Encoding]::UTF8.GetBytes($json)
    $header = [BitConverter]::GetBytes([uint32]$body.Length)   # native byte order, as in Chrome's protocol
    $pipe.Write($header, 0, 4)
    $pipe.Write($body, 0, $body.Length)
    $pipe.Flush()
}

function Read-Exactly([int]$count) {
    $buffer = New-Object byte[] $count
    $got = 0
    while ($got -lt $count) {
        $n = $pipe.Read($buffer, $got, $count - $got)
        if ($n -le 0) { return $null }
        $got += $n
    }
    return $buffer
}

function Read-Frame() {
    $header = Read-Exactly 4
    if ($null -eq $header) { return $null }
    $length = [BitConverter]::ToUInt32($header, 0)
    $body = Read-Exactly ([int]$length)
    if ($null -eq $body) { return $null }
    return [System.Text.Encoding]::UTF8.GetString($body)
}

Send-Frame '{"v":1,"id":"probe-hello","type":"hello"}'
Write-Host (Read-Frame)

if ($Url -ne "") {
    $request = @{ v = 1; id = "probe-1"; type = "download"; urls = @($Url); options = @{ quality = $Quality } } | ConvertTo-Json -Compress -Depth 5
    Send-Frame $request
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $frame = Read-Frame
        if ($null -eq $frame) { Write-Host "(connection closed)"; break }
        Write-Host $frame
        if ($frame -match '"type":"(done|error|rejected)"') { break }
    }
}
$pipe.Dispose()
