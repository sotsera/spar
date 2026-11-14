import { AsyncPipe } from "@angular/common";
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, OnInit, resource, signal } from "@angular/core";
import { toSignal } from "@angular/core/rxjs-interop";
import { map, Observable } from "rxjs";
import { Call, type Result, type SubscriptionEvent } from "@spar/app/sdk";
import { successResult } from "@spar/app/testing";
import { copyJobs, type CopyPlan, type CopyProgress, type PlanError } from "@spar/app/copyJobs";
import { plan, recipe } from "../../shared/harness";
import { PromiseCall } from "../../shared/promise-call";
import { progressStyles } from "./component-styles";

export function show(r: Result<CopyPlan, PlanError>): string {
  return r.match({
    success: p => `planned ${p.steps.length}`,
    errors: { connectionNotFound: e => `missing ${e.connectionId}`, tableMissing: e => `no table ${e.table}` },
    failure: e => `failure ${e.kind}`,
  });
}

/** An Observable over a Spar stream or subscription whose unsubscribe aborts the call. */
export function fromSpar<T>(open: (signal: AbortSignal) => AsyncIterable<T>): Observable<T> {
  return new Observable<T>(subscriber => {
    const abort = new AbortController();
    void (async () => {
      try {
        for await (const item of open(abort.signal)) subscriber.next(item);
        subscriber.complete();
      } catch (e) {
        subscriber.error(e);
      }
    })();
    return () => abort.abort();
  });
}

@Component({ selector: "signal-plan", template: `<p>{{ text() }}</p>`, changeDetection: ChangeDetectionStrategy.OnPush })
export class SignalPlan implements OnInit {
  readonly text = signal("loading");
  readonly #abort = new AbortController();
  constructor() { inject(DestroyRef).onDestroy(() => this.#abort.abort()); }
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    this.text.set(show(await copyJobs.plan(recipe, { signal: this.#abort.signal })));
  }
}

/** A plain field set after awaiting a Call (no signal, no markForCheck). Eager change detection (OnPush is the default in Angular 22). */
@Component({ selector: "field-plan", template: `<p>{{ text }}</p>`, changeDetection: ChangeDetectionStrategy.Eager })
export class FieldPlan implements OnInit {
  text = "loading";
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> { this.text = show(await copyJobs.plan(recipe)); }
}

/** Baseline without the SDK: a plain field set after awaiting a native Promise resolved by setTimeout. */
@Component({ selector: "field-promise", template: `<p>{{ text }}</p>`, changeDetection: ChangeDetectionStrategy.Eager })
export class FieldPromise implements OnInit {
  text = "loading";
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    await new Promise<void>(resolve => setTimeout(resolve, 20));
    this.text = "planned 1";
  }
}

/** A plain field set in the Call's then() callback (no await). */
@Component({ selector: "field-then", template: `<p>{{ text }}</p>`, changeDetection: ChangeDetectionStrategy.Eager })
export class FieldThen implements OnInit {
  text = "loading";
  ngOnInit(): void { void copyJobs.plan(recipe).then(r => { this.text = show(r); }); }
}

@Component({
  selector: "resource-plan",
  template: `@if (plan.hasValue()) { <p>{{ show(plan.value()) }}</p> } @else { <p>{{ plan.status() }}</p> }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResourcePlan {
  readonly tables = signal<readonly string[]>(["dbo.Orders"]);
  // The loader returns the Call: resource() accepts a PromiseLike, and the value is the Result.
  readonly plan = resource({
    params: () => this.tables(),
    loader: ({ params, abortSignal }) => copyJobs.plan({ ...recipe, tables: params }, { signal: abortSignal }),
  });
  readonly show = show;
}

@Component({
  selector: "resource-unwrap",
  template: `<p>{{ plan.status() }} {{ errorCode() }}</p>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResourceUnwrap {
  readonly plan = resource({ loader: ({ abortSignal }) => copyJobs.plan(recipe, { signal: abortSignal }).unwrap() });
  errorCode(): string {
    const e: unknown = this.plan.error();
    return e && typeof e === "object" && "error" in e ? String((e as { error: { code: string } }).error.code) : String(e);
  }
}

@Component({
  selector: "async-pipe-plan",
  imports: [AsyncPipe],
  template: `@if (call | async; as r) { <p>{{ show(r) }}</p> } @else { <p>loading</p> }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AsyncPipePlan {
  readonly call: Call<CopyPlan, PlanError> = copyJobs.plan(recipe);
  readonly show = show;
}

@Component({
  selector: "progress-rx",
  template: `<p>{{ status() }}: {{ rows() }} rows</p>`,
  styles: progressStyles,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProgressRx {
  readonly jobId = input.required<string>();
  readonly rows = signal("0");
  readonly status = signal("connecting");
  constructor() {
    const sub = fromSpar(signal => copyJobs.progress("j", { signal })).subscribe(item => item.match({
      success: (e: SubscriptionEvent<CopyProgress>) => {
        if (e.type === "event") { this.rows.set(e.value.rows.toString()); this.status.set("live"); }
        else if (e.type === "gap") this.status.set(`${e.dropped} skipped`);
        else if (e.type === "reconnecting") this.status.set("reconnecting");
        else if (e.type === "resumed") this.status.set("live");
        else this.rows.set("0");
      },
      failure: e => { if (e.kind !== "cancelled") this.status.set(e.message); },
    }));
    inject(DestroyRef).onDestroy(() => sub.unsubscribe());
  }
}

@Component({ selector: "progress-signal", template: `<p>{{ last() }}</p>`, changeDetection: ChangeDetectionStrategy.OnPush })
export class ProgressSignal {
  // toSignal unsubscribes on destroy (its DestroyRef), and fromSpar turns that into an abort.
  readonly last = toSignal(fromSpar(signal => copyJobs.progress("j", { signal })).pipe(
    map(item => item.match({ success: e => (e.type === "event" ? `rows ${e.value.rows}` : e.type), failure: e => e.kind }))), { initialValue: "connecting" });
}

/** A Call whose promise is settled from outside the Angular zone (as a bridge listener registered before bootstrap would). */
@Component({ selector: "field-root-zone", template: `<p>{{ text }}</p>`, changeDetection: ChangeDetectionStrategy.Eager })
export class FieldRootZone implements OnInit {
  text = "loading";
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> {
    const zone = (globalThis as { Zone?: { root: { run: (f: () => void) => void } } }).Zone;
    const outside = (f: () => void): void => { if (zone) zone.root.run(f); else f(); };
    const call = new Call<CopyPlan, PlanError>(new Promise(resolve => { outside(() => setTimeout(() => resolve(successResult(plan)), 20)); }));
    this.text = show(await call);
  }
}

/** Candidate SDK change: the pending call as a Promise subclass, in resource(), the async pipe and an awaited field. */
@Component({
  selector: "promise-call-plan",
  imports: [AsyncPipe],
  template: `<p>{{ plan.hasValue() ? show(plan.value()) : plan.status() }}</p>@if (call | async; as r) { <p>{{ show(r) }}</p> }<p>{{ text }}</p>`,
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class PromiseCallPlan implements OnInit {
  readonly plan = resource({ loader: ({ abortSignal }) => PromiseCall.of(copyJobs.plan(recipe, { signal: abortSignal })) });
  readonly call = PromiseCall.of(copyJobs.plan(recipe));
  text = "loading";
  readonly show = show;
  ngOnInit(): void { void this.load(); }
  async load(): Promise<void> { this.text = show(await PromiseCall.of(copyJobs.plan(recipe))); }
}
