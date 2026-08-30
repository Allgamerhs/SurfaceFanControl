[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot 'src\SurfaceProFanControl\bin\Release\net48'
$application = Join-Path $outputDirectory 'SurfaceProFanControl.exe'
$guard = Join-Path $outputDirectory 'SurfaceProFanGuard.exe'
$pipeName = 'SurfaceProFanControl.Guard.v1'
$existingService = Get-Service -Name SurfaceProFanGuard -ErrorAction SilentlyContinue
if ($existingService) {
    throw 'Rogue-pipe regression testi kurulu yerel fan hizmeti varken güvenle çalıştırılamaz.'
}
foreach ($path in @($application, $guard)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release çıktısı eksik: $path"
    }
}

function Start-RoguePipeServer {
    $readyPath = Join-Path ([IO.Path]::GetTempPath()) (
        'surface-pro-pipe-test-' + [Guid]::NewGuid().ToString('N') + '.ready')
    $readyLiteral = $readyPath.Replace("'", "''")
    $pipeLiteral = $pipeName.Replace("'", "''")
    $childCode = @"
`$ErrorActionPreference = 'Stop'
`$pipe = [IO.Pipes.NamedPipeServerStream]::new(
    '$pipeLiteral',
    [IO.Pipes.PipeDirection]::InOut,
    1,
    [IO.Pipes.PipeTransmissionMode]::Byte,
    ([IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::WriteThrough))
try {
    [IO.File]::WriteAllText('$readyLiteral', 'READY')
    `$pipe.WaitForConnection()
    `$reader = [IO.StreamReader]::new(`$pipe, [Text.Encoding]::UTF8, `$false, 1024, `$true)
    `$writer = [IO.StreamWriter]::new(`$pipe, [Text.UTF8Encoding]::new(`$false), 1024, `$true)
    try {
        `$writer.AutoFlush = `$true
        [void]`$reader.ReadLine()
        `$writer.WriteLine('OK|AUTO|5000|5000|183|183|45.0|100.0|00|00')
    }
    finally {
        `$writer.Dispose()
        `$reader.Dispose()
    }
}
catch {
}
finally {
    `$pipe.Dispose()
}
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCode))
    $powerShellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $process = Start-Process -FilePath $powerShellExe -WindowStyle Hidden -PassThru `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    while (-not (Test-Path -LiteralPath $readyPath -PathType Leaf) -and
        -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 50
        $process.Refresh()
    }
    if (-not (Test-Path -LiteralPath $readyPath -PathType Leaf)) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        throw 'Rogue pipe sunucusu sekiz saniyede hazır olmadı.'
    }
    [pscustomobject]@{ Process = $process; ReadyPath = $readyPath }
}

function Stop-RoguePipeServer($server) {
    if ($server) {
        $server.Process.Refresh()
        if (-not $server.Process.HasExited) {
            Stop-Process -Id $server.Process.Id -Force -ErrorAction SilentlyContinue
            [void]$server.Process.WaitForExit(3000)
        }
        $server.Process.Dispose()
        Remove-Item -LiteralPath $server.ReadyPath -Force -ErrorAction SilentlyContinue
    }
}

$uiRejected = $false
$server = Start-RoguePipeServer
try {
    [void][Reflection.Assembly]::LoadFrom($application)
    $client = [SurfaceProFanControl.Infrastructure.FanGuardClient]::new()
    try {
        [void]$client.ReadStatus()
    }
    catch {
        $uiRejected = $true
    }
}
finally {
    Stop-RoguePipeServer $server
}
if (-not $uiRejected) {
    throw 'Release UI istemcisi rogue pipe yanıtını kabul etti.'
}

$guardRejected = $false
$server = Start-RoguePipeServer
try {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $guard
    $startInfo.Arguments = '--authenticated-pipe-command AUTO'
    $startInfo.WorkingDirectory = $outputDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $guardProcess = [Diagnostics.Process]::Start($startInfo)
    $guardStdout = $guardProcess.StandardOutput.ReadToEnd()
    $guardStderr = $guardProcess.StandardError.ReadToEnd()
    if (-not $guardProcess.WaitForExit(10000)) {
        $guardProcess.Kill()
        throw 'Guard rogue-pipe testi on saniyede bitmedi.'
    }
    $guardRejected = $guardProcess.ExitCode -ne 0 -and $guardStdout -notmatch '(?m)^OK\|'
    $guardProcess.Dispose()
}
finally {
    Stop-RoguePipeServer $server
}
if (-not $guardRejected) {
    throw 'Dağıtım doğrulayıcısı rogue pipe yanıtını kabul etti.'
}

$preclaimedNameRejected = $false
$server = Start-RoguePipeServer
try {
    $options = [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::WriteThrough
    try {
        $listener = [IO.Pipes.NamedPipeServerStream]::new(
            $pipeName,
            [IO.Pipes.PipeDirection]::InOut,
            1,
            [IO.Pipes.PipeTransmissionMode]::Byte,
            $options,
            4096,
            4096)
        $listener.Dispose()
    }
    catch [UnauthorizedAccessException] {
        $preclaimedNameRejected = $true
    }
    catch [IO.IOException] {
        $preclaimedNameRejected = $true
    }
}
finally {
    Stop-RoguePipeServer $server
}
if (-not $preclaimedNameRejected) {
    throw 'Tek-instance dinleyici önceden kapılmış pipe adını reddetmedi.'
}

$guardAssembly = [Reflection.Assembly]::LoadFrom($guard)
$verifierType = $guardAssembly.GetType('SurfaceProI2EcFanTest.FanGuardDeploymentVerifier', $true)
$responseMethod = $verifierType.GetMethod(
    'ReadBoundedResponseAsync',
    [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
if (-not $responseMethod) {
    throw 'Guard toplu yanıt okuma metodu release binary içinde bulunamadı.'
}
$bulkResponseReadCount = 0
$expectedResponse = 'OK|AUTO|5000|5000|183|183|45.0|100.0|00|00'
$responseBytes = [Text.UTF8Encoding]::new($false).GetBytes($expectedResponse + "`r`n")
foreach ($attempt in 1..12) {
    $responsePipeName = 'SurfaceProFanControl.PipeResponseTest.' + [Guid]::NewGuid().ToString('N')
    $responseListener = [IO.Pipes.NamedPipeServerStream]::new(
        $responsePipeName,
        [IO.Pipes.PipeDirection]::InOut,
        1,
        [IO.Pipes.PipeTransmissionMode]::Byte,
        ([IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::WriteThrough),
        4096,
        4096)
    $responseClient = [IO.Pipes.NamedPipeClientStream]::new(
        '.', $responsePipeName, [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $connectTask = $responseClient.ConnectAsync(2000)
        $responseListener.WaitForConnection()
        $connectTask.GetAwaiter().GetResult()
        $readTask = $responseMethod.Invoke($null, [object[]]@($responseClient))
        $responseListener.Write($responseBytes, 0, $responseBytes.Length)
        $responseListener.Flush()
        $responseListener.Disconnect()
        if ($readTask.GetAwaiter().GetResult() -ne $expectedResponse) {
            throw "Guard doğrulayıcısı toplu yanıtı $attempt. denemede eksik okudu."
        }
        $bulkResponseReadCount++
    }
    finally {
        $responseClient.Dispose()
        $responseListener.Dispose()
    }
}
if ($bulkResponseReadCount -ne 12) {
    throw "Guard doğrulayıcısının toplu yanıt tekrar sayısı geçersiz: $bulkResponseReadCount"
}

$runtimeType = $guardAssembly.GetType('SurfaceProI2EcFanTest.FanGuardRuntime', $true)
$deadlineMethod = $runtimeType.GetMethod(
    'ReadRequestWithDeadline',
    [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
if (-not $deadlineMethod) {
    throw 'Guard request-deadline metodu release binary içinde bulunamadı.'
}
$deadlinePipeName = 'SurfaceProFanControl.PipeDeadlineTest.' + [Guid]::NewGuid().ToString('N')
$deadlineListener = [IO.Pipes.NamedPipeServerStream]::new(
    $deadlinePipeName,
    [IO.Pipes.PipeDirection]::InOut,
    1,
    [IO.Pipes.PipeTransmissionMode]::Byte,
    ([IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::WriteThrough),
    4096,
    4096)
$stalledClient = [IO.Pipes.NamedPipeClientStream]::new(
    '.', $deadlinePipeName, [IO.Pipes.PipeDirection]::InOut,
    [IO.Pipes.PipeOptions]::Asynchronous)
$deadlineTimedOut = $false
$listenerReused = $false
try {
    $connectTask = $stalledClient.ConnectAsync(2000)
    $deadlineListener.WaitForConnection()
    $connectTask.GetAwaiter().GetResult()
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        [void]$deadlineMethod.Invoke($null, [object[]]@($deadlineListener))
    }
    catch {
        $failure = $_.Exception
        while ($failure.InnerException) { $failure = $failure.InnerException }
        $deadlineTimedOut = $failure -is [TimeoutException] -and
            $watch.ElapsedMilliseconds -ge 2500 -and $watch.ElapsedMilliseconds -le 5000
    }
    if (-not $deadlineTimedOut) {
        throw "Guard request deadline beklenen üç saniyelik timeout'u üretmedi: $($watch.ElapsedMilliseconds) ms"
    }
    $stalledClient.Dispose()
    if ($deadlineListener.IsConnected) { $deadlineListener.Disconnect() }

    $validClient = [IO.Pipes.NamedPipeClientStream]::new(
        '.', $deadlinePipeName, [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $connectTask = $validClient.ConnectAsync(2000)
        $deadlineListener.WaitForConnection()
        $connectTask.GetAwaiter().GetResult()
        $writer = [IO.StreamWriter]::new(
            $validClient, [Text.UTF8Encoding]::new($false), 1024, $true)
        try {
            $writer.AutoFlush = $true
            $writer.WriteLine('STATUS')
            $readBack = [string]$deadlineMethod.Invoke(
                $null, [object[]]@($deadlineListener))
            $listenerReused = $readBack -eq 'STATUS'
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $validClient.Dispose()
    }
}
finally {
    $stalledClient.Dispose()
    $deadlineListener.Dispose()
}
if (-not $listenerReused) {
    throw 'Guard aynı pipe listener handle ile timeout sonrası geçerli isteği kabul etmedi.'
}

[pscustomobject]@{
    Result = 'PASS'
    UiRejectedRogueServer = $uiRejected
    GuardVerifierRejectedRogueServer = $guardRejected
    SingleInstanceRejectedPreclaim = $preclaimedNameRejected
    BulkResponseReadsBeforeDisconnect = $bulkResponseReadCount
    RequestDeadlineTimedOut = $deadlineTimedOut
    ListenerReusedAfterTimeout = $listenerReused
}
