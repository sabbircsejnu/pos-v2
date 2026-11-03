<#
.SYNOPSIS
  Rotates the Redis password of the Development and/or Test Compose stack and updates the matching local secrets.

.DESCRIPTION
  Dry-run by default (prints the impact only). With -Apply, for each selected target:
    1. generates a new alphanumeric password in memory (never printed),
    2. recreates ONLY the redis service with the new password (process environment overrides the env file),
    3. verifies unauthenticated access is refused and the new password authenticates,
    4. only then persists it: docker/.env.dev|.env.test and user-secrets
       (Dev:  ConnectionStrings:Redis of RetailPOS.API;  Test: RETAILPOS_TEST_REDIS of RetailPOS.IntegrationTests).
  If verification fails the previous password is restored on the container and nothing is persisted.

  Impact: the Redis container is recreated (a few seconds of downtime; named volume kept). Dev Redis holds cache data only.
  Running API processes must be restarted to pick up the new Development secret. No other service is touched.
#>
[CmdletBinding()]
param(
    [ValidateSet('Dev', 'Test', 'Both')][string]$Target = 'Both',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $root

$targets = @{
    Dev  = @{
        EnvFile = Join-Path $root '.env.dev'; Compose = Join-Path $root 'compose.dev.yml'; Var = 'DEV_REDIS_PASSWORD'; Port = 6382
        Project = Join-Path $repo 'src\RetailPOS.API\RetailPOS.API.csproj'; SecretKey = 'ConnectionStrings:Redis'
    }
    Test = @{
        EnvFile = Join-Path $root '.env.test'; Compose = Join-Path $root 'compose.test.yml'; Var = 'TEST_REDIS_PASSWORD'; Port = 6383
        Project = Join-Path $repo 'tests\RetailPOS.IntegrationTests\RetailPOS.IntegrationTests.csproj'; SecretKey = 'RETAILPOS_TEST_REDIS'
    }
}
$selected = if ($Target -eq 'Both') { 'Dev', 'Test' } else { @($Target) }

function New-Password {
    $bytes = New-Object byte[] 48
    (New-Object Security.Cryptography.RNGCryptoServiceProvider).GetBytes($bytes)
    ([Convert]::ToBase64String($bytes) -replace '[^A-Za-z0-9]', '').Substring(0, 32)
}

function Send-Raw($port, $command) {
    $client = New-Object Net.Sockets.TcpClient('127.0.0.1', $port)
    try {
        $stream = $client.GetStream()
        $bytes = [Text.Encoding]::ASCII.GetBytes($command + "`r`n")
        $stream.Write($bytes, 0, $bytes.Length)
        Start-Sleep -Milliseconds 400
        $buffer = New-Object byte[] 512
        [Text.Encoding]::ASCII.GetString($buffer, 0, $stream.Read($buffer, 0, 512))
    }
    finally { $client.Close() }
}

function Get-EnvValue($file, $name) {
    foreach ($line in Get-Content $file) {
        if ($line -match "^\s*$name=(.*)$") { return $Matches[1].Trim().Trim("'") }
    }
    throw "$name not found in $file"
}

function Set-Container($t, $password) {
    $env:REDIS_ROTATION_PASSWORD = $password
    $previous = [Environment]::GetEnvironmentVariable($t.Var)
    [Environment]::SetEnvironmentVariable($t.Var, $password)
    try {
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        docker compose --env-file $t.EnvFile -f $t.Compose up -d --no-deps --force-recreate redis 2>&1 | Out-Null
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = $previousPreference
        if ($exitCode -ne 0) { throw 'docker compose up redis failed' }
    }
    finally {
        $ErrorActionPreference = 'Stop'
        [Environment]::SetEnvironmentVariable($t.Var, $previous)
        Remove-Item Env:\REDIS_ROTATION_PASSWORD -ErrorAction SilentlyContinue
    }
}

function Test-Redis($port, $password) {
    for ($i = 0; $i -lt 15; $i++) {
        try {
            $unauth = Send-Raw $port 'PING'
            $auth = Send-Raw $port "AUTH $password`r`nPING"
            return ($unauth -match 'NOAUTH') -and ($auth -match '\+OK') -and ($auth -match '\+PONG')
        }
        catch { Start-Sleep 2 }
    }
    return $false
}

foreach ($name in $selected) {
    $t = $targets[$name]
    if (-not $Apply) {
        Write-Host "[$name] would recreate the redis service of $($t.Compose) (port $($t.Port)), update $($t.Var) in $($t.EnvFile) and user-secret $($t.SecretKey). Re-run with -Apply."
        continue
    }

    $old = Get-EnvValue $t.EnvFile $t.Var
    $new = New-Password
    Set-Container $t $new
    if (-not (Test-Redis $t.Port $new)) {
        Set-Container $t $old
        throw "[$name] verification failed; previous password restored and nothing persisted."
    }

    $lines = Get-Content $t.EnvFile | ForEach-Object { if ($_ -match "^\s*$($t.Var)=") { "$($t.Var)='$new'" } else { $_ } }
    $lines | Set-Content $t.EnvFile -Encoding ascii

    $current = $null
    dotnet user-secrets list --project $t.Project | ForEach-Object {
        if ($_.StartsWith("$($t.SecretKey) = ")) { $current = $_.Substring($t.SecretKey.Length + 3) }
    }
    if ($current) {
        $parts = $current -split ',' | ForEach-Object { if ($_ -like 'password=*') { "password=$new" } else { $_ } }
        $value = $parts -join ','
    }
    else {
        $value = "127.0.0.1:$($t.Port),password=$new,abortConnect=false,connectTimeout=3000,syncTimeout=3000"
    }
    @{ ($t.SecretKey) = $value } | ConvertTo-Json | dotnet user-secrets set --project $t.Project
    if ($LASTEXITCODE -ne 0) { throw "[$name] user-secrets update failed" }
    Write-Host "[$name] Redis password rotated and verified; $($t.EnvFile) and user-secret $($t.SecretKey) updated."
}
