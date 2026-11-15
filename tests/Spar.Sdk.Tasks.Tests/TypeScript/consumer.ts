// Uses every generated module of the Shop fixture as an app would; it must compile with no error.
import { orders, type Basket, type Item, type OrderError } from "@spar/app/orders";
import { configureSpar, type CallError } from "@spar/app/sdk";
import { fingerprint } from "@spar/app/spar";
import { stock, type Item as StockItem } from "@spar/app/stock";

configureSpar({ onFailure: (error: CallError) => console.warn(error.kind, error.code, error.execution) });

const item: Item = { sku: "A-100", stock: 3n, price: 9.5, tags: ["new"], replacement: null };
const basket: Basket = { items: [item], coupon: null, notes: ["gift", null] };

export async function place(): Promise<string | undefined> {
  return orders.place(basket).match({
    success: receipt => (receipt.paid ? receipt.id : receipt.at),
    errors: {
      outOfStock: error => `${error.sku}: ${error.left + 1n} left`,
      closed: () => "closed",
    },
  });
}

export async function cancel(): Promise<string> {
  return orders.cancel("o-1").match({
    success: () => "cancelled",
    errors: { outOfStock: () => "too late", closed: () => "closed" },
    failure: error => error.code,
  });
}

export const clear = (): Promise<void> => orders.clear().unwrap();

export const count = (signal: AbortSignal): Promise<bigint> => orders.count({ signal }).unwrap();

export const list = (): Promise<readonly Item[]> => stock.list().unwrap();

// The shared type is one type, whichever module the page imports it from.
export const same = (value: Item): StockItem => value;

export const find = async (): Promise<number> => (await stock.find("A-100").unwrap()).price;

export const handled: OrderError["outOfStock"] = { sku: "A-100", left: 0n };

export const built: string = fingerprint;
