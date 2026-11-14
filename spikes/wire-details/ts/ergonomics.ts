// Each numbered line is expected to fail type checking; tsc's output is the evidence. Lines marked OK must compile.
declare function render(text: string): void;
declare const idNumber: number;
declare const idString: string;
declare const idBigint: bigint;
declare const idMixed: number | string;
declare const rows: readonly { readonly id: bigint }[];

// Option A: number. Everything works, including mistakes.
render(`#${idNumber}`); // OK
const nextA = idNumber + 1; // OK

// Option B: string. Arithmetic needs a conversion; `+ 1` silently concatenates.
const nextB = idString + 1; // OK, but "41" + 1 = "411"
const nextB2: number = idString + 1; // 1: string is not number
const sortedB = [idString].sort((a, b) => a - b); // 2: arithmetic on string

// Option C: number | string. Every use needs narrowing.
const nextC = idMixed + 1; // 3: operator + on number | string
const keyC = new Map<number, string>().get(idMixed); // 4

// Option D: bigint.
const nextD = idBigint + 1; // 5: cannot mix bigint and number
const nextD2 = idBigint + 1n; // OK
const pctD = Number(idBigint) / 100; // OK, explicit
const maxD = Math.max(idBigint, 1n); // 6: Math works on number only
const sortedD = [...rows].sort((a, b) => a.id - b.id); // 7: comparator must return number
const sortedD2 = [...rows].sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)); // OK
const json = JSON.stringify({ id: idBigint }); // OK for tsc, throws TypeError at run time
const same = idBigint === 5n; // OK, primitives compare by value
render(`#${idBigint}`); // OK

export { nextA, nextB, nextB2, sortedB, nextC, keyC, nextD, nextD2, pctD, maxD, sortedD, sortedD2, json, same };
