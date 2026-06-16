$outputs = az deployment sub show -n honeygrid-dev --query properties.outputs | ConvertFrom-Json

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "[+] YOUR AZURE RESOURCES ARE READY!" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

Write-Host "`n[+] HONEYPOT ADDRESSES:" -ForegroundColor Yellow
Write-Host "   Cowrie (SSH/Telnet) : " -NoNewline; Write-Host $outputs.cowrieAppFqdn.value -ForegroundColor Green
Write-Host "   Web (HTTP/FakeLogin): " -NoNewline; Write-Host $outputs.webHoneypotAppFqdn.value -ForegroundColor Green
Write-Host "   TCP Listener (RDP)  : " -NoNewline; Write-Host $outputs.tcpListenerAppFqdn.value -ForegroundColor Green

Write-Host "`n[+] BACKEND API:" -ForegroundColor Yellow
Write-Host "   API URL             : " -NoNewline; Write-Host $outputs.apiAppFqdn.value -ForegroundColor Green
Write-Host "   Function App Name   : " -NoNewline; Write-Host $outputs.functionAppName.value -ForegroundColor Green

Write-Host "`n=========================================" -ForegroundColor Cyan
Write-Host "How to test the attack in the real world?" -ForegroundColor Cyan
Write-Host "1. Login via SSH: ssh root@$($outputs.cowrieAppFqdn.value)"
Write-Host "2. Make a Web request: curl https://$($outputs.webHoneypotAppFqdn.value)/wp-admin"
Write-Host "=========================================" -ForegroundColor Cyan
