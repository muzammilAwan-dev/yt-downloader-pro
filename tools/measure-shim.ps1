# Step 2.4 gate: how long does the shim take from start to its first reply?
#   1. Start YT Downloader Pro (the shim talks to the RUNNING app).
#   2. .\tools\measure-shim.ps1 -Shim .\shim-publish\ytdlp-shim.exe
# Publishes nothing and changes nothing: it copies the shim to a temp folder with its own allowed-origins.txt and sends "hello" 10 times.
# Rule of thumb from the plan: median over about 150 ms means "use the self-contained single-file build instead of Native AOT".
param(
    [Parameter(Mandatory = $true)][string]$Shim,
    [int]$Runs = 10
)

$origin = "chrome-extension://measureshimmeasureshimmeasureshim/"
$dir = Join-Path ([IO.Path]::GetTempPath()) ("shim-measure-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $dir | Out-Null
Copy-Item $Shim $dir
Set-Content -Path (Join-Path $dir "allowed-origins.txt") -Value $origin -Encoding ASCII
$exe = Join-Path $dir (Split-Path $Shim -Leaf)

function Read-Exactly($stream, [int]$count) {
    $buffer = New-Object byte[] $count
    $got = 0
    while ($got -lt $count) {
        $n = $stream.Read($buffer, $got, $count - $got)
        if ($n -le 0) { return $null }
        $got += $n
    }
    return $buffer
}

$hello = [Text.Encoding]::UTF8.GetBytes('{"v":1,"id":"measure","type":"hello"}')
$header = [BitConverter]::GetBytes([uint32]$hello.Length)
$times = @()

for ($i = 0; $i -lt $Runs; $i++) {
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = $origin
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.CreateNoWindow = $true

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    $in = $p.StandardInput.BaseStream
    $in.Write($header, 0, 4)
    $in.Write($hello, 0, $hello.Length)
    $in.Flush()

    $replyHeader = Read-Exactly $p.StandardOutput.BaseStream 4
    if ($null -eq $replyHeader) { Write-Error "No reply. Is the app running? See %LOCALAPPDATA%\YTDownloaderProEngine\shim.log"; $p.Kill(); break }
    $body = Read-Exactly $p.StandardOutput.BaseStream ([int][BitConverter]::ToUInt32($replyHeader, 0))
    $watch.Stop()

    $times += $watch.Elapsed.TotalMilliseconds
    if ($i -eq 0) { Write-Host ("First reply: " + [Text.Encoding]::UTF8.GetString($body)) }
    $in.Close()
    $p.WaitForExit(5000) | Out-Null
    if (-not $p.HasExited) { $p.Kill() }
}

Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
if ($times.Count -gt 0) {
    $sorted = $times | Sort-Object
    $median = $sorted[[int][Math]::Floor($sorted.Count / 2)]
    Write-Host ("Runs: {0}   min: {1:N0} ms   median: {2:N0} ms   max: {3:N0} ms" -f $times.Count, $sorted[0], $median, $sorted[-1])
}
