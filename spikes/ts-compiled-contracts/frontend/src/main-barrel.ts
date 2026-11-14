// Bundle entry: one contract through the root barrel. auditLog (in the same package) is never imported.
import { configureSpar } from "@spar/app/sdk";
import { copyJobs } from "@spar/app";

configureSpar({ onFailure: e => console.warn(e.call, e.message) });

document.querySelector("button")?.addEventListener("click", () => {
  void copyJobs.plan({ source: "a", target: "b", tables: ["t"] }).match({
    success: p => console.log(p.steps.length),
    errors: { connectionNotFound: e => e.connectionId, tableMissing: e => e.table, columnsMismatch: e => e.table },
  });
});
