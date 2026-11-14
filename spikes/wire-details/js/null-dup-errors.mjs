// Items 4, 5 and 6 on the JavaScript side: null and undefined, duplicate properties, and how a client prototype
// classifies the C# dispatch prototype's responses.
// Usage (from spikes/wire-details): node js/null-dup-errors.mjs
import { readFileSync } from "node:fs";

const load = (name) => readFileSync(new URL(`../results/${name}`, import.meta.url), "utf8");
const show = (label, fn) => {
  try {
    const v = fn();
    console.log(`- ${label} -> ${v === undefined ? "undefined" : typeof v === "string" ? v : JSON.stringify(v)}`);
  } catch (e) {
    console.log(`- ${label} -> THROWS ${e.constructor.name}: ${e.message}`);
  }
};

console.log("## Item 4: null and undefined");
show("JSON.stringify(undefined)", () => String(JSON.stringify(undefined)) + ` (typeof ${typeof JSON.stringify(undefined)})`);
show("JSON.stringify(null)", () => JSON.stringify(null));
show("JSON.stringify({ a: undefined, b: null })", () => JSON.stringify({ a: undefined, b: null }));
show("JSON.stringify([undefined, null])", () => JSON.stringify([undefined, null]));
show("JSON.parse('{\"ok\":true}').value", () => JSON.parse('{"ok":true}').value);
show("'value' in JSON.parse('{\"ok\":true}')", () => "value" in JSON.parse('{"ok":true}'));
show("'value' in JSON.parse('{\"ok\":true,\"value\":null}')", () => "value" in JSON.parse('{"ok":true,"value":null}'));
show("JSON.parse('')", () => JSON.parse(""));

// Prototype of the generated client's success decoding: the return kind is known statically from the contract.
function decode(envelope, kind) {
  const message = JSON.parse(envelope);
  if (!message.ok) return { error: message.error ?? message.failure };
  switch (kind) {
    case "void":
      if ("value" in message) throw new Error("protocol: void command returned a value");
      return { value: undefined };
    case "nullable":
    case "value":
      if (!("value" in message)) throw new Error("protocol: value missing");
      if (kind === "value" && message.value === null) throw new Error("protocol: null for a non-nullable result");
      return { value: message.value };
  }
}
const envelopes = JSON.parse(load("null.json"));
for (const [name, kind] of [["voidOk", "void"], ["nullableNull", "nullable"], ["nullableValue", "nullable"],
  ["resultNullableNull", "nullable"], ["resultError", "nullable"], ["nullableNull", "value"], ["nullableNull", "void"]]) {
  show(`decode ${name} as ${kind}`, () => decode(JSON.stringify(envelopes[name]), kind));
}

console.log("\n## Item 5: duplicate properties in JSON.parse");
show("JSON.parse('{\"a\":1,\"a\":2}').a", () => JSON.parse('{"a":1,"a":2}').a);
show("JSON.parse('{\"a\":1,\"\\\\u0061\":2}').a", () => JSON.parse('{"a":1,"\\u0061":2}').a);
show("JSON.parse('{\"__proto__\":{\"x\":1}}'): own key, prototype untouched",
  () => `own ${Object.hasOwn(JSON.parse('{"__proto__":{"x":1}}'), "__proto__")}, inherited x ${JSON.parse('{"__proto__":{"x":1}}').x}`);
show("Object.assign({}, that).x (copy with [[Set]])", () => Object.assign({}, JSON.parse('{"__proto__":{"x":1}}')).x);
show("{ ...that }.x (spread defines own)", () => ({ ...JSON.parse('{"__proto__":{"x":1}}') }).x);

console.log("\n## Item 6: the C# dispatch prototype's responses, as a client prototype classifies them");
const responses = JSON.parse(load("writer-errors.json"));
for (const [name, message] of Object.entries(responses)) {
  const outcome = message.ok
    ? `success ${JSON.stringify(message.value)}`
    : `failure kind=${message.failure.kind}, execution=${message.failure.execution} -> ResponseSerializationError (never retried)`;
  console.log(`- ${name}: ${outcome}`);
}
const partial = load("partial-unary.json");
show(`JSON.parse of the ${partial.length.toLocaleString("en")} bytes an unbuffered writer had flushed`, () => JSON.parse(partial));
