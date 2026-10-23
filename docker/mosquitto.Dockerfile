# Railway'de ayrı bir servis olarak çalışan MQTT broker.
FROM eclipse-mosquitto:2
COPY docker/mosquitto/bulut.conf /mosquitto/config/mosquitto.conf
EXPOSE 1883
