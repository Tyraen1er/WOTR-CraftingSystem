param(
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"
Set-Location $ProjectRoot

Write-Host "== Crafting System preflight checks ==" -ForegroundColor Cyan

Write-Host "[1/4] Build (Release)..." -ForegroundColor Yellow
dotnet build "CraftingSystem.csproj" -c Release | Out-Host

Write-Host "[2/4] Validate ModConfig/CustomEnchants.json..." -ForegroundColor Yellow
Get-Content "ModConfig/CustomEnchants.json" -Raw | ConvertFrom-Json | Out-Null

Write-Host "[3/4] Validate ModConfig/CustomEnchants.example.json..." -ForegroundColor Yellow
Get-Content "ModConfig/CustomEnchants.example.json" -Raw | ConvertFrom-Json | Out-Null

Write-Host "[4/4] Validate ModConfig/Localization.json..." -ForegroundColor Yellow
Get-Content "ModConfig/Localization.json" -Raw | ConvertFrom-Json | Out-Null

Write-Host "Preflight checks: PASS" -ForegroundColor Green
