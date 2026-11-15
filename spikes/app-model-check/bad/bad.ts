// Mistakes tsc must reject. Not from docs/app-model.md. Each mistake is one line; the [codes] in the comment above it
// are the diagnostics it must produce, exactly. tools/check-bad.mjs compares them with what tsc reports.
import { copyJobs, type CopyRecipe } from "@spar/app/copyJobs";
import { actions, dialogs, files, menus } from "@spar/app/sdk";
import { windows } from "@spar/app/windows";
import { jobLog } from "@spar/app/jobLog";

const recipe: CopyRecipe = { source: "src", target: "dst", tables: ["dbo.Orders"] };
const runRequest = { planId: "p", truncateTarget: false };

export async function mistakes(): Promise<void> {
  // 1. a declared error case is not handled (tableMissing) [TS2741]
  await copyJobs.plan(recipe).match({ success: () => 1, errors: { connectionNotFound: () => 2 } });

  // 2. a misspelled case: tableMising instead of tableMissing [TS2561]
  await copyJobs.plan(recipe).match({ success: () => 1, errors: { connectionNotFound: () => 2, tableMising: () => 3 } });

  // 3. a payload field that does not exist (connectionNotFound carries connectionId, not connection) [TS2551]
  await copyJobs.plan(recipe).match({ success: () => 1, errors: { connectionNotFound: e => e.connection, tableMissing: () => 3 } });

  // 4. the errors object forgotten on run, which declares RunError [TS2345]
  await copyJobs.run(runRequest).match({ success: r => r.rows });

  // 5. errors given to cancelAll, which declares none [TS2353]
  await copyJobs.cancelAll().match({ success: () => 0, errors: {} });

  // 6. resume: "automatic" on a Manual subscription [TS2322]
  jobLog.follow("job-1", { resume: "automatic" });

  // 7. reading the value of an awaited Result without match or unwrap [TS2339]
  console.log((await copyJobs.plan(recipe)).value);

  // 8. reading the value of a pending Call without match or unwrap [TS2339]
  console.log(copyJobs.plan(recipe).steps);

  // 9. a misspelled failure kind: unavailabel instead of unavailable [TS2322]
  await copyJobs.cancelAll().match({ success: () => 0, failure: { unavailabel: () => 1 } });

  // 10. a subscription read without handling reset; the elaboration names reset [TS2322]
  for await (const item of copyJobs.progress("job-1")) item.match({ success: () => 1, events: { gap: () => 2 } });

  // 11. a misspelled event: gapp instead of gap [TS2322]
  for await (const item of copyJobs.progress("job-1")) item.match({ success: () => 1, events: { reset: () => 0, gapp: () => 2 } });

  // 12. a subscription read without an events object [TS2345]
  for await (const item of copyJobs.progress("job-1")) item.match({ success: () => 1 });

  // 13. a file dialog read without handling dismissed [TS2741]
  await files.open().match({ success: () => 1, errors: { accessDenied: () => 2, unavailable: () => 3, busy: () => 4 } });

  // 14. the same action id twice in one define [TS1117]
  const twice = actions.define({ copyRow: { label: "Copy row", run: () => {} }, copyRow: { label: "Copy again", run: () => {} } });

  const own = actions.define({ findTable: { label: "Find table", run: () => {} } });

  // 15. a misspelled action in a menu: findTabel instead of findTable [TS2551]
  await menus.show(new MouseEvent("contextmenu"), [twice.copyRow, own.findTabel]).unwrap();

  // 16. a default button that is not one of the buttons [TS2322]
  await dialogs.message({ title: "Delete?", text: "Sure?", buttons: ["Delete", "Cancel"], default: "Keep" }).unwrap();

  // 17. a window kind that is not declared: setings instead of settings [TS2345]
  await windows.open("setings").unwrap();

  // 18. a subscription that declares errors, read without the errors object [TS2345]
  for await (const item of jobLog.tail("job-1")) item.match({ success: () => 1, events: { reset: () => 0 } });

  // 19. a field a CallError does not have: only kind, code and execution are decided [TS2339]
  await copyJobs.cancelAll().match({ success: () => "done", failure: e => e.message });

  // 20. the other undecided field: a CallError does not name its call [TS2339]
  await copyJobs.cancelAll().match({ success: () => "done", failure: e => e.call });
}
