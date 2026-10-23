# Kutu simülatörü: bulut demosunda gerçek ESP32 yerine 5 sanal kutu çalıştırır.
# Donanım bağlandığında bu servis kapatılır.
# Projenin başka bir projeye referansı yok; yalnız kendi klasörü kopyalanır.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY tools/KutuSimulatoru/ ./
RUN dotnet publish KutuSimulatoru.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
# Broker adresi MQTT_SUNUCU ile verilir (Railway: mosquitto.railway.internal).
ENTRYPOINT ["sh", "-c", "dotnet KutuSimulatoru.dll --kutu ${KUTULAR:-1,2,3,4,5} --gecikme ${TEYIT_GECIKMESI:-2000}"]
