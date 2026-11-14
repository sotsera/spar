// The payload: a row of six fields as the generated client would decode it, field by field, from a JSON batch.
// On the wire `version` (a C# long) is a canonical decimal string; the client turns it into a bigint.

export interface Row {
  readonly id: number;
  readonly name: string;
  readonly qty: number;
  readonly price: number;
  readonly status: string;
  readonly version: bigint;
}

const STATUSES = ["new", "open", "done", "late"];

/** The JSON text of a batch of `size` rows starting at `start`, as the host would send it. */
export function batchText(start: number, size: number): string {
  const parts: string[] = [];
  for (let i = start; i < start + size; i++) {
    parts.push(`{"id":${i},"name":"row-${i % 9973}","qty":${i % 17},"price":${(i % 1000) / 4},"status":"${STATUSES[i % 4]}","version":"${9007199254740993n + BigInt(i)}"}`);
  }
  return `[${parts.join(",")}]`;
}

function fail(field: string): never {
  throw new TypeError(`invalid ${field}`);
}

/** Field-by-field construction with the type checks a generated decoder makes. */
export function decodeRow(e: { readonly [key: string]: unknown }): Row {
  const id = e["id"], name = e["name"], qty = e["qty"], price = e["price"], status = e["status"], version = e["version"];
  if (typeof id !== "number") fail("id");
  if (typeof name !== "string") fail("name");
  if (typeof qty !== "number") fail("qty");
  if (typeof price !== "number") fail("price");
  if (typeof status !== "string") fail("status");
  if (typeof version !== "string") fail("version");
  return { id, name, qty, price, status, version: BigInt(version) };
}

/** Decodes one batch message into its rows. Shared by every variant. */
export function decodeBatch(text: string): Row[] {
  const parsed = JSON.parse(text) as { readonly [key: string]: unknown }[];
  const rows: Row[] = [];
  for (let i = 0; i < parsed.length; i++) rows.push(decodeRow(parsed[i]!));
  return rows;
}

/** Sum of `qty` over rows [0, n), to check that every consumer saw every row. */
export function expectedQty(n: number): number {
  let sum = 0;
  for (let i = 0; i < n; i++) sum += i % 17;
  return sum;
}
