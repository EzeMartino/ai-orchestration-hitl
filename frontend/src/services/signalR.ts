import type { IHttpConnectionOptions } from "@microsoft/signalr";

export function createActivityHubConnectionOptions(accessToken: string): IHttpConnectionOptions {
  return {
    accessTokenFactory: () => accessToken,
  };
}
