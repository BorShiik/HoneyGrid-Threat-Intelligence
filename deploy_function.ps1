$resourceGroup = "hg-dev-rg"
$functionAppName = "hg-dev-func-jaugmcd2wlrx2"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "🚀 DEPLOYING AZURE FUNCTION (AI & SIGNALR)" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

Write-Host "1. Budowanie projektu HoneyGrid.Functions..."
cd src\HoneyGrid.Functions
dotnet publish -c Release -o publish

Write-Host "2. Pakowanie do pliku ZIP (używając tar.exe)..."
if (Test-Path functionapp.zip) { Remove-Item functionapp.zip -Force }
# Windows 10/11 posiada natywne tar.exe, które bezbłędnie pakuje WSZYSTKIE pliki (w tym ukryte)
tar.exe -a -c -f functionapp.zip -C publish .

Write-Host "3. Wysylanie do chmury (może zająć minutę)..."
az functionapp deployment source config-zip -g $resourceGroup -n $functionAppName --src functionapp.zip

Write-Host "GOTOWE! Funkcja dziala w Azure." -ForegroundColor Green
cd ..\..\..
