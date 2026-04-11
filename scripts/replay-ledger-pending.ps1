param(
    [string]$ComposeProjectDir = "./infrastructure",
    [string]$Stream = "ledger:events",
    [string]$Group = "financial-ledger",
    [string]$RedisService = "redis",
    [string]$ConsumerService = "financialledger",
    [int]$MaxCount = 1000,
    [switch]$SkipStopStart,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Compose {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Args
    )

    $composeArgs = @("compose") + $Args
    if ([string]::IsNullOrWhiteSpace($ComposeProjectDir)) {
        return docker @composeArgs
    }

    $projectPath = Resolve-Path -Path $ComposeProjectDir
    Push-Location $projectPath
    try {
        return docker @composeArgs
    }
    finally {
        Pop-Location
    }
}

function Get-PendingIds {
    param(
        [string]$StreamKey,
        [string]$GroupName,
        [int]$Limit
    )

    $raw = Invoke-Compose -Args @("exec", "-T", $RedisService, "redis-cli", "--raw", "XPENDING", $StreamKey, $GroupName, "-", "+", "$Limit")
    return $raw | Where-Object { $_ -match "^[0-9]+-[0-9]+$" }
}

function Get-PendingSummary {
    param(
        [string]$StreamKey,
        [string]$GroupName
    )

    $summary = Invoke-Compose -Args @("exec", "-T", $RedisService, "redis-cli", "--raw", "XPENDING", $StreamKey, $GroupName)
    return $summary
}

function Replay-PendingId {
    param(
        [string]$EntryId,
        [string]$StreamKey,
        [string]$GroupName,
        [switch]$PreviewOnly
    )

    $raw = Invoke-Compose -Args @("exec", "-T", $RedisService, "redis-cli", "--raw", "XRANGE", $StreamKey, $EntryId, $EntryId)
    $flat = @($raw | Where-Object { $_ -ne "" })

    if ($flat.Count -lt 3) {
        return @{ Replayed = $false; Acked = $false; Reason = "EmptyOrMalformed" }
    }

    $xaddArgs = @("exec", "-T", $RedisService, "redis-cli", "XADD", $StreamKey, "*")
    for ($i = 1; $i -lt $flat.Count; $i++) {
        $xaddArgs += $flat[$i]
    }

    if ($PreviewOnly) {
        return @{ Replayed = $true; Acked = $false; Reason = "DryRun" }
    }

    $newId = Invoke-Compose -Args $xaddArgs
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($newId -join ""))) {
        return @{ Replayed = $false; Acked = $false; Reason = "XAddFailed" }
    }

    Invoke-Compose -Args @("exec", "-T", $RedisService, "redis-cli", "XACK", $StreamKey, $GroupName, $EntryId) | Out-Null
    if ($LASTEXITCODE -ne 0) {
        return @{ Replayed = $true; Acked = $false; Reason = "XAckFailed" }
    }

    return @{ Replayed = $true; Acked = $true; Reason = "OK" }
}

Write-Host "[ledger-replay] ComposeProjectDir=$ComposeProjectDir Stream=$Stream Group=$Group RedisService=$RedisService ConsumerService=$ConsumerService DryRun=$DryRun"

$before = Get-PendingSummary -StreamKey $Stream -GroupName $Group
Write-Host "[ledger-replay] Pending before:"
$before | ForEach-Object { Write-Host "  $_" }

$pendingIds = Get-PendingIds -StreamKey $Stream -GroupName $Group -Limit $MaxCount
if (-not $pendingIds -or $pendingIds.Count -eq 0) {
    Write-Host "[ledger-replay] No pending entries found. Nothing to replay."
    exit 0
}

if (-not $SkipStopStart) {
    Write-Host "[ledger-replay] Stopping consumer service: $ConsumerService"
    Invoke-Compose -Args @("stop", $ConsumerService) | Out-Null
}

$replayed = 0
$acked = 0
$failed = 0

foreach ($id in $pendingIds) {
    $result = Replay-PendingId -EntryId $id -StreamKey $Stream -GroupName $Group -PreviewOnly:$DryRun
    if ($result.Replayed) { $replayed++ }
    if ($result.Acked) { $acked++ }
    if (-not $result.Replayed -or (-not $DryRun -and -not $result.Acked)) {
        $failed++
        Write-Warning "[ledger-replay] id=$id failed reason=$($result.Reason)"
    }
}

if (-not $SkipStopStart) {
    Write-Host "[ledger-replay] Starting consumer service: $ConsumerService"
    Invoke-Compose -Args @("up", "-d", $ConsumerService) | Out-Null
}

$after = Get-PendingSummary -StreamKey $Stream -GroupName $Group
Write-Host "[ledger-replay] Pending after:"
$after | ForEach-Object { Write-Host "  $_" }
Write-Host "[ledger-replay] Done replayed=$replayed acked=$acked failed=$failed"

if ($failed -gt 0) {
    exit 2
}

exit 0
