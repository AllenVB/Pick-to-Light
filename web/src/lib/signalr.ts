"use client";

import { HubConnection, HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { API_URL, oturumOku } from "./api";

let baglanti: HubConnection | null = null;

/**
 * Tüm sayfaların paylaştığı tek SignalR bağlantısı (/hubs/depo).
 * Kopma durumunda otomatik yeniden bağlanır.
 */
export function hubBaglantisi(): HubConnection {
  if (baglanti) return baglanti;

  baglanti = new HubConnectionBuilder()
    .withUrl(`${API_URL}/hubs/depo`, {
      accessTokenFactory: () => oturumOku()?.token ?? "",
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
    .configureLogging(LogLevel.Warning)
    .build();

  return baglanti;
}

export async function hubBaslat(): Promise<HubConnection> {
  const c = hubBaglantisi();
  if (c.state === "Disconnected") await c.start();
  return c;
}
