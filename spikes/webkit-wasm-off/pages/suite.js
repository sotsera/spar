// Shared WebAssembly checks, loaded by the page (script tag) and by the worker (importScripts).
// No timers anywhere: a never-presented WebKitWebView throttles them.
"use strict";

// (module (func (export "add") (param i32 i32) (result i32) local.get 0 local.get 1 i32.add))
self.ADD_WASM = new Uint8Array([
  0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00,
  0x01, 0x07, 0x01, 0x60, 0x02, 0x7f, 0x7f, 0x01, 0x7f,
  0x03, 0x02, 0x01, 0x00,
  0x07, 0x07, 0x01, 0x03, 0x61, 0x64, 0x64, 0x00, 0x00,
  0x0a, 0x09, 0x01, 0x07, 0x00, 0x20, 0x00, 0x20, 0x01, 0x6a, 0x0b,
]);

self.describeError = (e) => (e && typeof e === "object" ? `${e.name}: ${e.message}` : `thrown ${String(e)}`);

self.check = async (results, name, fn) => {
  try {
    const value = await fn();
    results[name] = { ok: true, value: value === undefined ? "undefined" : value };
  } catch (e) {
    results[name] = { ok: false, error: describeError(e) };
  }
};

// Runs every compile and instantiate path against the WebAssembly namespace `W` of some realm.
// `fetchWasm` is undefined where fetch is not usable for the realm.
self.wasmSuite = async (W, fetchWasm) => {
  const r = {};
  r.typeofWebAssembly = typeof W;
  if (typeof W !== "object" || W === null) return r;
  const bytes = self.ADD_WASM;
  await check(r, "validate", () => W.validate(bytes));
  await check(r, "compile", async () => (await W.compile(bytes)) instanceof W.Module);
  await check(r, "moduleSync", () => new W.Module(bytes) instanceof W.Module);
  await check(r, "instanceSync", () => new W.Instance(new W.Module(bytes)).exports.add(2, 3));
  await check(r, "instantiateBytes", async () => (await W.instantiate(bytes)).instance.exports.add(2, 3));
  await check(r, "instantiateModule", async () => {
    let module;
    try {
      module = await W.compile(bytes);
    } catch (e) {
      return `compile failed first (${describeError(e)})`;
    }
    return (await W.instantiate(module)).exports.add(2, 3);
  });
  if (fetchWasm) {
    await check(r, "compileStreaming", async () => (await W.compileStreaming(fetchWasm())) instanceof W.Module);
    await check(r, "instantiateStreaming", async () => (await W.instantiateStreaming(fetchWasm())).instance.exports.add(2, 3));
  }
  await check(r, "memory", () => new W.Memory({ initial: 1 }).buffer.byteLength);
  return r;
};

self.evalChecks = async () => {
  const r = {};
  await check(r, "eval", () => eval("1 + 1"));
  await check(r, "newFunction", () => new Function("return 2")());
  return r;
};

// A small JavaScript workload, to see whether a JSC option changes JIT behaviour for ordinary code.
self.jsWork = () => {
  const start = performance.now();
  let acc = 0;
  for (let i = 0; i < 30_000_000; i++) acc = (acc + ((i * 31) ^ (i >>> 3))) | 0;
  return { ms: Math.round((performance.now() - start) * 10) / 10, acc };
};
