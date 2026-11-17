// Deliberate mistakes against the Shop fixture. Each line marked `// expect TSxxxx` must fail with exactly that code
// on that line, and no other line may fail.
import { orders, type Basket, type Item } from "@spar/app/orders";
import { stock } from "@spar/app/stock";

const item: Item = { sku: "A-100", stock: 3n, price: 9.5, tags: [], replacement: null };
const basket: Basket = { items: [item], coupon: null, notes: [] };

// A long is a bigint, never a number.
export const wrongLong: Item = { ...item, stock: 3 }; // expect TS2322

// Wire values are read-only.
export function mutate(value: Item): void {
  value.sku = "B-200"; // expect TS2540
}

// A scalar request has its own type.
export const wrongRequest = stock.find(100); // expect TS2345

// Commands that do not exist.
export const unknownCommand = orders.refund(); // expect TS2339

// A required property left out.
export const missing: Basket = { items: [], coupon: null }; // expect TS2741

// Every declared error needs a handler.
export const unhandled = orders.place(basket).match({
  success: () => 1,
  errors: { outOfStock: () => 2 }, // expect TS2741
});

// A misspelled case: the SDK's NoInfer guard on the case names reports it as a type mismatch.
export const misspelled = orders.place(basket).match({
  success: () => 1,
  errors: { outOfStock: () => 2, closed: () => 3, closd: () => 4 }, // expect TS2322
});

// A command without declared errors has no errors object.
export const extraErrors = stock.list().match({
  success: () => 1,
  errors: {}, // expect TS2353
});
