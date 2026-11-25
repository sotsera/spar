import { configureSpar, onBackendUpdate, type BackendUpdate } from "@spar/app/sdk";
import { copyJobs } from "@spar/app/copyJobs";

declare function toast(text: string): void;
declare function showPlan(steps: number): void;

configureSpar({
  onFailure: e => toast(`The call failed (${e.code})`),
  // The page shows its own notice below, so Spar's development toast is turned off.
  backendUpdateToast: false,
});

// Debug builds only: after each C# edit Hot Reload applied, ask the backend again, so the view shows what the new code
// returns. Returns the function that stops listening (a React effect's cleanup, for example).
export const stopListening = onBackendUpdate((update: BackendUpdate) => {
  toast(`Revision ${update.revision}: ${update.types.join(", ")}`);
  void copyJobs.plan({ source: "prod", target: "test", tables: ["dbo.Orders"] }).match({
    success: plan => showPlan(plan.steps.length),
    errors: { connectionNotFound: () => undefined, tableMissing: () => undefined },
  });
});
