# =====================================================
# PowerShell Script to Apply Seed Data
# =====================================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Retail POS - Applying Seed Data" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Database connection parameters
$DB_HOST = "127.0.0.1"
$DB_PORT = "5435"
$DB_NAME = "retailpos_db"
$DB_USER = "omsadmin"
$DB_PASSWORD = "123qwe"

# Check if PostgreSQL is accessible
Write-Host "Checking database connection..." -ForegroundColor Yellow

$testConnection = docker exec -e PGPASSWORD=$DB_PASSWORD retailpos-postgres psql -U $DB_USER -d $DB_NAME -c "SELECT 1;" 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error: Cannot connect to database" -ForegroundColor Red
    Write-Host "Please ensure PostgreSQL is running (docker-compose up -d)" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Database connection successful" -ForegroundColor Green
Write-Host ""

# Apply seed data
Write-Host "Applying seed data..." -ForegroundColor Yellow
Write-Host ""

$scriptPath = Join-Path $PSScriptRoot "seed-data.sql"

if (-not (Test-Path $scriptPath)) {
    Write-Host "❌ Error: seed-data.sql not found at: $scriptPath" -ForegroundColor Red
    exit 1
}

$result = Get-Content $scriptPath | docker exec -i -e PGPASSWORD=$DB_PASSWORD retailpos-postgres psql -U $DB_USER -d $DB_NAME 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error applying seed data" -ForegroundColor Red
    Write-Host $result -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "✅ Seed Data Applied Successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "You can now login with:" -ForegroundColor Cyan
Write-Host "  Email: admin@retailpos.com" -ForegroundColor White
Write-Host "  Password: Admin@123" -ForegroundColor White
Write-Host ""
Write-Host "⚠️  Remember to change the password after first login!" -ForegroundColor Yellow
Write-Host ""
