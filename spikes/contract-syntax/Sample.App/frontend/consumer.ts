// Type-checks the generated packages the way an application would use them.
import { configureTransport } from "@spar/sdk";
import { planning, type PlanError, type Money as PlanningMoney } from "@spar/planning";
import { billing, type Money as BillingMoney, type PaymentError } from "@spar/billing";
import { inventory, type LookupError } from "@spar/inventory";
import { pricing } from "@spar/pricing";
import { notes } from "@spar/notes";

configureTransport({
  invoke: async () => null,
  stream: async function* () {},
});

function describePlanError(error: PlanError): string {
  switch (error.code) {
    case "connectionNotFound":
      return `no connection ${error.connectionId}`;
    case "tableMissing":
      return `no table ${error.connection}.${error.table}`;
    case "forbidden":
      return "forbidden";
    default: {
      const unreachable: never = error;
      return unreachable;
    }
  }
}

function describeLookup(error: LookupError): string {
  return error.code === "discontinued" ? `use ${error.replacement ?? "nothing"}` : `missing ${error.sku}`;
}

function describePayment(error: PaymentError): string {
  return error.code === "accountLocked" ? error.account : String(error.missing.amount);
}

export async function run(): Promise<string[]> {
  const lines: string[] = [];
  const built = await planning.build({ connection: "main", table: "orders", force: false });
  lines.push(built.ok ? built.value.name : describePlanError(built.error));

  // One shared Money type, re-exported by both packages.
  const total: PlanningMoney = await planning.total("6f9619ff-8b86-d011-b42d-00c04fc964ff");
  const charged: BillingMoney = await billing.charge(total);
  lines.push(charged.currency);

  const paid = await billing.pay({ amount: charged, account: "a" });
  lines.push(paid.ok ? paid.value.id : describePayment(paid.error));

  const found = await inventory.find("A-1");
  lines.push(found.ok ? found.value.sku : describeLookup(found.error));

  for await (const step of planning.steps(3)) {
    lines.push(step.title);
  }

  const items = await planning.items();
  lines.push(items.map((i) => i.label).join(","));
  lines.push(String((await pricing.quote({ sku: "A-1", quantity: 1 })).amount));
  lines.push(await notes.echo("hi"));
  lines.push(String(await planning.count()));
  return lines;
}
