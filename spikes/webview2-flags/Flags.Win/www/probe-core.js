// The probe every context runs: is WebAssembly there, can it instantiate the smallest module, is gc exposed.
self.sparProbe = async function () {
  const r = { wasm: typeof WebAssembly, gc: typeof gc };
  if (typeof WebAssembly !== 'undefined') {
    try {
      await WebAssembly.instantiate(new Uint8Array([0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00]));
      r.instantiate = 'ok';
    } catch (e) {
      r.instantiate = (e && e.name) + ': ' + (e && e.message);
    }
  } else {
    r.instantiate = 'n/a';
  }
  return r;
};
