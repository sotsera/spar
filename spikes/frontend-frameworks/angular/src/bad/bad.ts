// Expected errors under strictTemplates (ngc -p tsconfig.bad.json): match inside templates.
import { Component } from "@angular/core";
import { AsyncPipe } from "@angular/common";
import type { Call, Result } from "@spar/app/sdk";
import type { CopyPlan, PlanError } from "@spar/app/copyJobs";

declare const someCall: Call<CopyPlan, PlanError>;

@Component({
  selector: "bad-arrow",
  // control: arrow functions are allowed in Angular 22 template expressions, so match can be written in the template
  template: `<p>{{ result.match({ success: p => p.steps.length, errors: { connectionNotFound: e => 0, tableMissing: e => 0 } }) }}</p>`,
})
export class BadArrow {
  declare readonly result: Result<CopyPlan, PlanError>;
}

@Component({
  selector: "bad-async",
  imports: [AsyncPipe],
  // bad 2: reading a value that the Result does not expose; control: the async pipe types the Call as its Result
  template: `@if (call | async; as r) { <p>{{ r.value }}</p> }`,
})
export class BadAsync {
  readonly call = someCall;
}

@Component({
  selector: "bad-missing-case",
  // bad 3: a declared case missing in a template match; bad 4: a misspelled case
  template: `<p>{{ result.match({ success: p => p.steps.length, errors: { connectionNotFound: e => 0 } }) }}</p>
    <p>{{ result.match({ success: p => p.steps.length, errors: { connectionNotFound: e => 0, tableMising: e => 0 } }) }}</p>`,
})
export class BadMissingCase {
  declare readonly result: Result<CopyPlan, PlanError>;
}
