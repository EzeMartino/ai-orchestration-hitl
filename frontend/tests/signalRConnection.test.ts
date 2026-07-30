import assert from "node:assert/strict";
import test from "node:test";
import { createActivityHubConnectionOptions } from "../src/services/signalR.ts";

test("uses the opaque access token unchanged", () => {
  const opaqueToken = "not-a.jwt--opaque_token+/with=characters";

  const options = createActivityHubConnectionOptions(opaqueToken);

  assert.equal(options.accessTokenFactory?.(), opaqueToken);
});
