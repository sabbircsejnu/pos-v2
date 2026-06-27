# =====================================================
# PowerShell Script to Apply Islamic Fashion Product Seed
# Run from repo root:  .\database\apply-islamic-seed.ps1
# Prerequisites: apply-seed.ps1 must have been run first.
# =====================================================

param(
    [string]$DbHost     = "127.0.0.1",
    [string]$DbPort     = "5435",
    [string]$DbName     = "retailpos_db",
    [string]$DbUser     = "admin",
    [string]$DbPassword = "589123Qwe"
)

Write-Host "============================================" -ForegroundColor Cyan
Write-Host " Retail POS – Islamic Fashion Product Seed" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ── 1. Connection check ─────────────────────────────────────────────────────
Write-Host "Checking database connection..." -ForegroundColor Yellow

$test = docker exec -e PGPASSWORD=$DbPassword retailpos-postgres `
    psql -U $DbUser -d $DbName -c "SELECT 1;" 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Cannot connect to database." -ForegroundColor Red
    Write-Host "  Make sure the container is running:  docker-compose up -d" -ForegroundColor Red
    exit 1
}

Write-Host "Database connection OK" -ForegroundColor Green
Write-Host ""

# ── 2. Check base seed ──────────────────────────────────────────────────────
Write-Host "Checking base seed status..." -ForegroundColor Yellow

$productCount = docker exec -e PGPASSWORD=$DbPassword retailpos-postgres `
    psql -U $DbUser -d $DbName -t -c "SELECT COUNT(*) FROM products;" 2>&1

if ($LASTEXITCODE -ne 0 -or [int]($productCount.Trim()) -eq 0) {
    Write-Host "WARNING: products table is empty or unreachable." -ForegroundColor Yellow
    Write-Host "  Run .\database\apply-seed.ps1 first to populate base data." -ForegroundColor Yellow
    $proceed = Read-Host "  Continue anyway? (y/N)"
    if ($proceed -ne 'y' -and $proceed -ne 'Y') { exit 0 }
} else {
    Write-Host "Base seed OK ($($productCount.Trim()) existing products)" -ForegroundColor Green
}
Write-Host ""

# ── 3. Apply Islamic fashion seed ──────────────────────────────────────────
$scriptPath = Join-Path $PSScriptRoot "seed-islamic-products.sql"

if (-not (Test-Path $scriptPath)) {
    Write-Host "ERROR: seed-islamic-products.sql not found at: $scriptPath" -ForegroundColor Red
    exit 1
}

Write-Host "Applying Islamic fashion product seed..." -ForegroundColor Yellow

$result = Get-Content $scriptPath |
    docker exec -i -e PGPASSWORD=$DbPassword retailpos-postgres `
        psql -U $DbUser -d $DbName 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR applying seed:" -ForegroundColor Red
    Write-Host $result -ForegroundColor Red
    exit 1
}

# Surface warnings
$warnings = $result | Where-Object { $_ -match "^(ERROR|WARNING)" }
if ($warnings) {
    Write-Host "" ; Write-Host "psql notices:" -ForegroundColor Yellow
    $warnings | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}

# ── 4. Summary ──────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " Islamic Fashion Seed Applied!"             -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "What was added:" -ForegroundColor Cyan
Write-Host "  Categories   : 2 root + ~15 sub-categories (Women's/Men's Islamic Wear)" -ForegroundColor White
Write-Host "  Suppliers    : 3 new  (IBB Wholesale BD, Nidha Fabrics, Panjabi Mart BD)" -ForegroundColor White
Write-Host "  Variations   : Fabric (NEW) + 19 Color options + 9 numeric Sizes added"  -ForegroundColor White
Write-Host "  Products     : 43  (13 Borka + 10 Abaya + 10 Hijab + 10 Panjabi)"        -ForegroundColor White
Write-Host "  Variants     : 172 (42 BRK + 32 ABY + 32 HJB + 66 PNJ)"                 -ForegroundColor White
Write-Host "  Images       : ~56 product images (picsum placeholders)"                 -ForegroundColor White
Write-Host "  Inventory    : ~450 records across Main Store, Downtown, Central WH"      -ForegroundColor White
Write-Host "  POs          : 4  (3 fully_received + 1 approved/pending)"               -ForegroundColor White
Write-Host "  GRNs         : 3  (full receipt for each received PO)"                   -ForegroundColor White
Write-Host "  Sample Sales : 2  (borka+hijab sale & abaya+panjabi sale)"               -ForegroundColor White
Write-Host ""
Write-Host "Module coverage:" -ForegroundColor Cyan
Write-Host "  Product Management  : searchable by name, SKU, category, color, fabric, size" -ForegroundColor White
Write-Host "  Purchase Orders     : 4 POs with items (1 open for GRN testing)"              -ForegroundColor White
Write-Host "  GRN                 : 3 completed GRNs for stock-in validation"               -ForegroundColor White
Write-Host "  Sales Orders        : 2 completed sales for cash/card flow testing"           -ForegroundColor White
Write-Host "  Stock Reports       : stock across 3 locations per variant"                   -ForegroundColor White
Write-Host "  Inventory Movements : purchase + sales deltas visible in stock ledger"        -ForegroundColor White
Write-Host ""
Write-Host "All inserts are idempotent – safe to re-run." -ForegroundColor DarkGray
Write-Host ""
