@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo Pick-to-Light servisleri durduruluyor...

rem Servis pencerelerini kapat (baslik ile eslesenler)
taskkill /FI "WINDOWTITLE eq Pick-to-Light API*"       /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq Pick-to-Light Arayuz*"    /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq Pick-to-Light Simulator*" /T /F >nul 2>&1

where docker >nul 2>&1 && docker compose stop postgres mosquitto >nul 2>&1

echo Tamamdir. ^(Veritabani verisi korunur; silmek icin: docker compose down -v^)
pause
