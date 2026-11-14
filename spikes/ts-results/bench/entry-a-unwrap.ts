// Bundle-size entry for form A: what a small page would use.
import { copyJobs } from "../forms/a-method/copyJobs.js";
import { configureSpar } from "../forms/a-method/sdk.js";

configureSpar({ onFailure: e => console.warn(e.message) });
const plan = (await copyJobs.plan({ source: "a", target: "b", tables: [] })).match({
  success: p => p.steps.length,
  errors: { connectionNotFound: e => e.connectionId, tableMissing: e => e.table, columnsMismatch: e => e.table },
});
const total = (await copyJobs.count()).match({ success: n => n });
const rows = (await copyJobs.count()).unwrap();
for await (const item of copyJobs.preview({ source: "a", target: "b", tables: [] })) item.match({ success: r => console.log(r.cells), errors: { tableMissing: e => console.log(e.table) } });
console.log(plan, total, rows);
