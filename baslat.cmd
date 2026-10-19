@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo.
echo  Pick-to-Light - tum servisler baslatiliyor
echo  ==========================================
echo.

rem --- 1) Gerekli araclar ---
where dotnet >nul 2>&1 || (echo HATA: .NET SDK bulunamadi - https://dotnet.microsoft.com/download/dotnet/8.0 & goto :son)
where node   >nul 2>&1 || (echo HATA: Node.js bulunamadi - https://nodejs.org & goto :son)

rem --- 2) Altyapi: PostgreSQL + Mosquitto (zaten ayaktaysa atlanir) ---
echo [1/5] Altyapi kontrol ediliyor...
rem Windows PowerShell 5.1 ile de calisir (ternary operator kullanilmaz)
powershell -NoProfile -Command "$c=New-Object Net.Sockets.TcpClient; try { $c.Connect('localhost',5432); exit 0 } catch { exit 1 } finally { $c.Dispose() }" >nul 2>&1
if errorlevel 1 (
  where docker >nul 2>&1 || (echo HATA: PostgreSQL kapali ve Docker yok - https://www.docker.com/products/docker-desktop/ & goto :son)
  echo        Docker ile PostgreSQL ve Mosquitto baslatiliyor...
  docker compose up -d postgres mosquitto || (echo HATA: Docker servisleri baslatilamadi. Docker Desktop acik mi? & goto :son)
  timeout /t 8 /nobreak >nul
) else (
  echo        PostgreSQL zaten calisiyor.
)

rem --- 3) Arayuz bagimliliklari (yalnizca ilk calistirmada) ---
if not exist "web\node_modules" (
  echo [2/5] Arayuz bagimliliklari yukleniyor ^(birkac dakika surebilir^)...
  pushd web
  call npm install --no-audit --no-fund
  popd
) else (
  echo [2/5] Arayuz bagimliliklari hazir.
)

rem --- 4) Servisler: her biri kendi penceresinde ---
echo [3/5] API baslatiliyor      ^(http://localhost:5080^)
start "Pick-to-Light API" cmd /k dotnet run --project src\PickToLight.Api

echo [4/5] Arayuz baslatiliyor   ^(http://localhost:3000^)
start "Pick-to-Light Arayuz" cmd /k "cd web && npm run dev"

echo [5/5] Kutu simulatoru baslatiliyor
start "Pick-to-Light Simulator" cmd /k dotnet run --project tools\KutuSimulatoru -- --kutu 1,2,3,4,5

rem --- 5) API hazir olunca tarayiciyi ac ---
echo.
echo  API'nin hazir olmasi bekleniyor...
powershell -NoProfile -Command "$s=0; while($s -lt 90){ try { if((Invoke-WebRequest 'http://localhost:5080/health' -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200){ Write-Host '  API hazir.'; exit 0 } } catch {}; Start-Sleep -Seconds 2; $s+=2 }; exit 1"
if errorlevel 1 (
  echo  API 90 saniyede acilmadi. "Pick-to-Light API" penceresindeki hata mesajina bakin.
) else (
  start http://localhost:3000
)

echo.
echo  Giris: admin / admin123  ^(yonetici^)   operator / operator123  ^(operator^)
echo  Durdurmak icin: durdur.cmd
echo.

:son
pause
