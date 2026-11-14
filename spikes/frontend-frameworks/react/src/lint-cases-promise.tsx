// Lint baseline (never imported): the cases of lint-cases.tsx against commands that return a real Promise subclass
// with the same match/unwrap (the alternative to the thenable Call).
import { useEffect, useState } from "react";
import type { Call, Result } from "@spar/app/sdk";
import type { CopyPlan, CopyRecipe, PlanError, PreviewRow } from "@spar/app/copyJobs";

declare class PromiseCall<T, E extends object = {}> extends Promise<Result<T, E>> {
  readonly match: Call<T, E>["match"];
  readonly unwrap: Call<T, E>["unwrap"];
}
declare const copyJobs: {
  readonly plan: (request: CopyRecipe) => PromiseCall<CopyPlan, PlanError>;
  readonly cancelAll: () => PromiseCall<void>;
  readonly preview: (request: CopyRecipe) => AsyncIterable<Result<PreviewRow>>;
};

declare const recipe: CopyRecipe;
declare function toast(text: string): void;

const handlers = {
  success: (p: { readonly steps: readonly unknown[] }) => p.steps.length,
  errors: { connectionNotFound: () => -1, tableMissing: () => -2 },
} as const;

export async function cases(): Promise<void> {
  copyJobs.plan(recipe); // case 01: a Call as a statement
  copyJobs.plan(recipe).match(handlers); // case 02: Call.match as a statement (a Promise)
  void copyJobs.plan(recipe).match(handlers); // case 03: void-ed match
  await copyJobs.plan(recipe).match(handlers); // case 04: awaited match
  const result = await copyJobs.plan(recipe); // case 05: await of a Call (await-thenable)
  result.match(handlers); // case 06: Result.match as a statement (synchronous)
  copyJobs.plan(recipe).then(r => r.match(handlers)); // case 07: then() without a rejection handler on a Call
  copyJobs.plan(recipe).unwrap(); // case 08: unwrap as a statement
  // @ts-expect-error TS2801: tsc reports the condition as always true for both forms
  if (copyJobs.plan(recipe)) toast("x"); // case 09: a Call in a condition
  const pending = copyJobs.plan(recipe); // case 10: a Call assigned and never awaited
  await Promise.all([pending, copyJobs.cancelAll()]); // case 11: Promise.all over Calls
  for await (const item of copyJobs.preview(recipe)) item.match({ success: () => 1 }); // case 12: stream item match
  for await (const item of copyJobs.preview(recipe)) item.match({ success: async () => { await Promise.resolve(); } }); // case 13: async success handler in a stream loop
  [1, 2].forEach(() => copyJobs.cancelAll()); // case 14: a Call returned from a void callback
  [1, 2].forEach(async () => { await copyJobs.cancelAll(); }); // case 15: async void callback (baseline)
}

export function returnsCall() {
  return copyJobs.plan(recipe); // case 16: a function returning a Call without async (promise-function-async)
}

export async function returnInTry(): Promise<number> {
  try {
    return copyJobs.plan(recipe).unwrap().then(p => p.steps.length); // case 17: returning a promise inside try (return-await)
  } catch {
    return 0;
  }
}

export async function returnCallInTry(): Promise<unknown> {
  try {
    return copyJobs.plan(recipe); // case 18: returning a Call inside try (return-await)
  } catch {
    return 0;
  }
}

export function Button() {
  const [text, setText] = useState("");
  useEffect(() => {
    copyJobs.plan(recipe).match({ ...handlers, success: p => { setText(String(p.steps.length)); } }); // case 19: match in an effect without void
  }, []);
  return (
    <>
      <button onClick={() => copyJobs.cancelAll()}>a</button>{/* case 20: arrow returning a Call to onClick */}
      <button onClick={() => copyJobs.cancelAll().match({ success: () => setText("ok") })}>b</button>{/* case 21: arrow returning Call.match (Promise) to onClick */}
      <button onClick={async () => { await copyJobs.cancelAll(); }}>c</button>{/* case 22: async onClick (baseline) */}
      <button onClick={() => { void copyJobs.cancelAll().match({ success: () => setText("ok") }); }}>d</button>{/* case 23: block body with void */}
      <p>{text}</p>
    </>
  );
}
