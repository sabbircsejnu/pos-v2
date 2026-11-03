<#
.SYNOPSIS
  Points the RetailPOS.IntegrationTests user-secrets at the standardized Test Compose stack (docker/compose.test.yml).

.DESCRIPTION
  Reads docker/.env.test and writes the RETAILPOS_TEST_* user-secrets through stdin (secrets never appear on the
  command line or in output). Run ONLY at the approved cutover, after the iranimart-test stack is healthy.
  Without -Apply it only validates that the env file is complete and prints the key names it would set.
#>
[CmdletBinding()]
param(
    [string]$EnvFile,
    [string]$Project,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $EnvFile) { $EnvFile = Join-Path $root '.env.test' }
if (-not $Project) { $Project = Join-Path $root '..\tests\RetailPOS.IntegrationTests\RetailPOS.IntegrationTests.csproj' }

$values = @{}
foreach ($line in Get-Content $EnvFile) {
    if ($line -match '^\s*([A-Z0-9_]+)=(.*)$') {
        $values[$Matches[1]] = $Matches[2].Trim().Trim("'")
    }
}

$required = 'TEST_POSTGRES_DB', 'TEST_POSTGRES_USER', 'TEST_POSTGRES_PASSWORD', 'TEST_POSTGRES_PORT',
    'TEST_RABBITMQ_USER', 'TEST_RABBITMQ_PASSWORD', 'TEST_RABBITMQ_VHOST', 'TEST_RABBITMQ_PORT',
    'TEST_REDIS_PASSWORD', 'TEST_REDIS_PORT', 'TEST_ELASTIC_PASSWORD', 'TEST_ELASTIC_PORT'
$missing = $required | Where-Object { -not $values[$_] -or $values[$_] -eq 'CHANGE_ME' }
if ($missing) { throw "Missing or placeholder values in ${EnvFile}: $($missing -join ', ')" }

$user = [Uri]::EscapeDataString($values.TEST_RABBITMQ_USER)
$pass = [Uri]::EscapeDataString($values.TEST_RABBITMQ_PASSWORD)
$secrets = [ordered]@{
    RETAILPOS_TEST_POSTGRES               = "Host=127.0.0.1;Port=$($values.TEST_POSTGRES_PORT);Database=$($values.TEST_POSTGRES_DB);Username=$($values.TEST_POSTGRES_USER);Password=$($values.TEST_POSTGRES_PASSWORD)"
    RETAILPOS_TEST_RABBITMQ_URI           = "amqp://${user}:${pass}@127.0.0.1:$($values.TEST_RABBITMQ_PORT)/$($values.TEST_RABBITMQ_VHOST)"
    RETAILPOS_TEST_REDIS                  = "127.0.0.1:$($values.TEST_REDIS_PORT),password=$($values.TEST_REDIS_PASSWORD),abortConnect=false,connectTimeout=3000,syncTimeout=3000"
    RETAILPOS_TEST_ELASTICSEARCH_URI      = "http://127.0.0.1:$($values.TEST_ELASTIC_PORT)"
    RETAILPOS_TEST_ELASTICSEARCH_USER     = 'elastic'
    RETAILPOS_TEST_ELASTICSEARCH_PASSWORD = $values.TEST_ELASTIC_PASSWORD
}

if (-not $Apply) {
    Write-Host "Validated $EnvFile. Would set: $($secrets.Keys -join ', '). Re-run with -Apply at the approved cutover."
    return
}

$secrets | ConvertTo-Json | dotnet user-secrets set --project $Project
if ($LASTEXITCODE -ne 0) { throw 'dotnet user-secrets set failed.' }
Write-Host "Updated: $($secrets.Keys -join ', ')"
