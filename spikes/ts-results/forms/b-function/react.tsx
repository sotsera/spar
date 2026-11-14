// Form B in a React component: an async event handler and a useEffect with cleanup.
import { useEffect, useState } from "react";
import { match } from "./sdk.js";
import { copyJobs, type CopyPlan, type CopyProgress, type CopyRecipe } from "./copyJobs.js";

export function CopyPanel({ recipe }: { readonly recipe: CopyRecipe }) {
  const [plan, setPlan] = useState<CopyPlan | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [total, setTotal] = useState<number | null>(null);
  const [progress, setProgress] = useState<CopyProgress | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    void copyJobs.count({ signal: controller.signal }).then(result => match(result, {
      success: setTotal,
      failure: e => { if (e.kind !== "cancelled") setProblem(e.message); },
    }));

    return () => controller.abort();
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      for await (const item of copyJobs.progress({ jobId: "j1" }, { signal: controller.signal })) {
        match(item, { success: e => { if (e.type === "event") setProgress(e.value); } });
      }
    })();

    return () => controller.abort();
  }, []);

  async function onPlan(): Promise<void> {
    setProblem(null);
    const result = await copyJobs.plan(recipe);
    setPlan(match(result, {
      success: p => p,
      errors: {
        connectionNotFound: e => { setProblem(`Unknown connection ${e.connectionId}`); return null; },
        tableMissing: e => { setProblem(`${e.table} is missing`); return null; },
        columnsMismatch: e => { setProblem(`${e.table} columns differ`); return null; },
      },
    }) ?? null);
  }

  return (
    <section>
      <button onClick={() => void onPlan()}>Plan</button>
      {problem && <p role="alert">{problem}</p>}
      {plan && <p>{plan.steps.length} tables</p>}
      {total !== null && <p>{total} jobs</p>}
      {progress && <p>{progress.table}: {progress.rows}</p>}
    </section>
  );
}
