// Bundle-size entry for form B: what a small page would use.
import { copyJobs } from "../forms/b-function/copyJobs.js";
import { configureSpar, match, unwrap } from "../forms/b-function/sdk.js";

configureSpar({ onFailure: e => console.warn(e.message) });
const plan = match(await copyJobs.plan({ source: "a", target: "b", tables: [] }), {
  success: p => p.steps.length,
  errors: { connectionNotFound: e => e.connectionId, tableMissing: e => e.table, columnsMismatch: e => e.table },
});
const total = match(await copyJobs.count(), { success: n => n });
const rows = unwrap(await copyJobs.count());
for await (const item of copyJobs.preview({ source: "a", target: "b", tables: [] })) match(item, { success: r => console.log(r.cells), errors: { tableMissing: e => console.log(e.table) } });
console.log(plan, total, rows);
