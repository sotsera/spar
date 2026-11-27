// Not from docs/app-model.md: the declarations the generator writes for the reports contract (3.21), as real Spar
// writes them in tests/AppModel/Real, for the page example of 6.17.
import type { Call, CallOptions, SparBlob } from "@spar/app/sdk";

/** A server's activity chart for the last minutes, by its key: what the page's address carries. */
export interface ChartKey {
  readonly server: string;
  readonly minutes: number;
}

/** Reports the page shows or saves. Wire name: reports. Wire name: reports. */
export declare const reports: {
  /** A finished copy's rows as CSV, for the page to show or save: a handle it releases when it is done. */
  readonly exportCopy: (jobId: string, options?: CallOptions) => Call<SparBlob>;

  /** The chart as PNG, drawn at each load of its address; none for a server the tool does not monitor. */
  readonly activityChart: { readonly url: (request: ChartKey) => string };
};
