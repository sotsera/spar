// Every line marked "expect error" must fail to type-check.
import { planning, type PlanError } from "@spar/planning";

export function missingCase(error: PlanError): string {
  switch (error.code) {
    case "connectionNotFound":
      return error.connectionId;
    case "tableMissing":
      return error.table;
    default: {
      const unreachable: never = error; // expect error: "forbidden" is not handled
      return unreachable;
    }
  }
}

export function wrongField(error: PlanError): string {
  return error.code === "connectionNotFound" ? error.connection : ""; // expect error: no such payload field
}

export async function readWithoutCheck(): Promise<string> {
  const result = await planning.build({ connection: "main", table: "t", force: false });
  return result.value.name; // expect error: value exists only when ok is true
}

export async function wrongRequest(): Promise<void> {
  await planning.get(42); // expect error: the request is a string
}
