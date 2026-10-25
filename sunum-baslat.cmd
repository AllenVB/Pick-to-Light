@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

rem ---------------------------------------------------------------------------
rem  Sunum modu: baska projelerle cakismayan portlarda tum sistemi ayaga kaldirir.
rem    Arayuz  http://localhost:4001      API  http://localhost:4000
rem    PostgreSQL localhost:5440          MQTT localhost:1884
rem  Ilk kurulumda docker container'lari yoksa olusturur; varsa yalnizca baslatir.
rem ---------------------------------------------------------------------------

echo.
echo  Pick-to-Light SUNUM modu
echo  ========================
echo.

where docker >nul 2>&1 || (echo HATA: Docker bulunamadi. Docker Desktop acik mi? & goto :son)

echo [1/5] Altyapi (PostgreSQL 5440 + MQTT 1884)...
docker start ptl-postgres >nul 2>&1 || docker run -d --name ptl-postgres -e POSTGRES_DB=picktolight -e POSTGRES_USER=ptl -e POSTGRES_PASSWORD=ptl_parola -e TZ=Europe/Istanbul -p 5440:5432 -v ptl-pgdata:/var/lib/postgresql/data postgres:17-alpine >nul
docker start ptl-mosquitto >nul 2>&1 || docker run -d --name ptl-mosquitto -p 1884:1883 -v "%cd%\docker\mosquitto\mosquitto.conf:/mosquitto/config/mosquitto.conf:ro" -v ptl-mqtt-data:/mosquitto/data eclipse-mosquitto:2 >nul
powershell -NoProfile -Command "$s=0; while($s -lt 60){ try { $c=New-Object Net.Sockets.TcpClient; $c.Connect('localhost',5440); $c.Dispose(); exit 0 } catch { Start-Sleep 2; $s+=2 } }; exit 1"
if errorlevel 1 (echo HATA: PostgreSQL 60 sn icinde acilmadi. & goto :son)

rem --- Ortak ayarlar: tum servisler bu degiskenleri devralir ---
set ASPNETCORE_ENVIRONMENT=Development
set ConnectionStrings__Postgres=Host=localhost;Port=5440;Database=picktolight;Username=ptl;Password=ptl_parola
set Mqtt__Port=1884
set Cors__IzinliKaynaklar__0=http://localhost:4001
set Cors__IzinliKaynaklar__1=http://127.0.0.1:4001
set NEXT_PUBLIC_API_URL=http://localhost:4000
set PTL_API=http://localhost:4000

rem --- Arayuz: ilk calistirmada uretim derlemesi (dev sunucusundan hizli ve kararli) ---
if not exist "web\node_modules" (
  echo [2/5] Arayuz bagimliliklari yukleniyor...
  pushd web
  call npm install --no-audit --no-fund
  popd
) else (
  echo [2/5] Arayuz bagimliliklari hazir.
)
if not exist "web\.next\BUILD_ID" (
  echo        Arayuz derleniyor ^(bir kez, ~1 dk^)...
  pushd web
  call npx next build
  popd
)

echo [3/5] API baslatiliyor      ^(http://localhost:4000^)
start "Pick-to-Light API" cmd /k dotnet run --project src\PickToLight.Api --urls http://localhost:4000

echo [4/5] Arayuz baslatiliyor   ^(http://localhost:4001^)
start "Pick-to-Light Arayuz" cmd /k "cd web && npx next start -p 4001"

echo [5/5] Kutu simulatoru (baskullu) baslatiliyor
start "Pick-to-Light Simulator" cmd /k dotnet run --project tools\KutuSimulatoru -- --kutu 1,2,3,4,5 --port 1884 --api http://localhost:4000

echo.
echo  API'nin hazir olmasi bekleniyor...
powershell -NoProfile -Command "$s=0; while($s -lt 120){ try { if((Invoke-WebRequest 'http://localhost:4000/health' -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200){ Write-Host '  API hazir.'; exit 0 } } catch {}; Start-Sleep -Seconds 2; $s+=2 }; exit 1"
if errorlevel 1 (
  echo  API 120 saniyede acilmadi. "Pick-to-Light API" penceresindeki hata mesajina bakin.
) else (
  start http://localhost:4001
)

echo.
echo  Giris: admin / admin123 ^(yonetici^)   operator / operator123 ^(operator^)
echo  Demo senaryolari: bash tools/demo.sh hepsi   ^(PTL_API=http://localhost:4000^)
echo  Durdurmak icin: sunum-durdur.cmd
echo.

:son
pause
