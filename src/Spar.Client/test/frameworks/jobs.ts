// A contract module in the shape the client step writes (plan section 2, "Generated TypeScript"): one command whose
// function hands the SDK's invoke its encoder and decoders. The framework tests call it as an app's page would.
import { invoke, unknownErrorCase, type Call, type CallOptions, type Decoder } from "../../src/sdk.js";
import * as wire from "../../src/wire.js";

export interface Plan {
  readonly id: string;
  readonly tables: number;
}

export interface PlanRequest {
  readonly source: string;
}

export interface PlanErrors {
  readonly tableMissing: { readonly table: string };
}

function readPlan(value: unknown): Plan {
  const object = wire.readObject(value);
  return {
    id: wire.readString(wire.field(object, "id")),
    tables: wire.readInt32(wire.field(object, "tables")),
  };
}

function readPlanError(code: string, payload: Readonly<Record<string, unknown>>): unknown {
  switch (code) {
    case "tableMissing":
      return { table: wire.readString(wire.field(payload, "table")) };
    default:
      return unknownErrorCase;
  }
}

const decodePlan: Decoder<Plan> = { value: json => readPlan(JSON.parse(json)), error: readPlanError };

export const jobs = Object.freeze({
  plan: (request: PlanRequest, options?: CallOptions): Call<Plan, PlanErrors> =>
    invoke<Plan, PlanErrors>("jobs", "plan", () => JSON.stringify({ source: request.source }), decodePlan, options),
});
