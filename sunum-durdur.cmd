@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo Pick-to-Light sunum servisleri durduruluyor...

taskkill /FI "WINDOWTITLE eq Pick-to-Light API*"       /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq Pick-to-Light Arayuz*"    /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq Pick-to-Light Simulator*" /T /F >nul 2>&1

rem Container'lar yalnizca durdurulur, silinmez: veritabani verisi korunur.
where docker >nul 2>&1 && docker stop ptl-postgres ptl-mosquitto >nul 2>&1

echo Tamamdir. ^(Veri korunur.^)
pause
