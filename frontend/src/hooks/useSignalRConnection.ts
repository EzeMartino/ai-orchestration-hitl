import { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";
import type { ActivityEvent } from "../types/domain.types";
import { apiBaseUrl } from "../services/api";

export function useSignalRConnection(onActivityEvent: (event: ActivityEvent) => void) {
  const [connectionStatus, setConnectionStatus] = useState("Disconnected");

  useEffect(() => {
    let isDisposed = false;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${apiBaseUrl}/hubs/activity`)
      .withAutomaticReconnect()
      .build();

    const handleActivityEvent = (event: ActivityEvent) => {
      onActivityEvent(event);
    };

    connection.on("activityEventReceived", handleActivityEvent);

    connection.onreconnecting(() => {
      if (!isDisposed) {
        setConnectionStatus("Reconnecting");
      }
    });

    connection.onreconnected(() => {
      if (!isDisposed) {
        setConnectionStatus("Connected");
      }
    });

    connection.onclose(() => {
      if (!isDisposed) {
        setConnectionStatus("Disconnected");
      }
    });

    connection
      .start()
      .then(() => {
        if (!isDisposed) {
          setConnectionStatus("Connected");
        }
      })
      .catch((error) => {
        if (isDisposed) {
          return;
        }
        console.error("SignalR connection failed:", error);
        setConnectionStatus("Failed");
      });

    return () => {
      isDisposed = true;
      connection.off("activityEventReceived", handleActivityEvent);
      void connection.stop();
    };
  }, [onActivityEvent]);

  return connectionStatus;
}
