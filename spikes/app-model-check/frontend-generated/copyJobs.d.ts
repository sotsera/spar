import type { Call, CallOptions, Result, SubscribeOptions, Subscription } from "@spar/app/sdk";

export interface CopyRecipe { readonly source: string; readonly target: string; readonly tables: readonly string[]; }
export interface CopyPlan { readonly planId: string; readonly steps: readonly PlanStep[]; }
export interface PlanStep { readonly table: string; readonly estimatedRows: bigint; }
export interface RunCopy { readonly planId: string; readonly truncateTarget: boolean; }
export interface CopyReport { readonly tables: number; readonly rows: bigint; readonly elapsed: string; }
export interface PreviewRow { readonly cells: readonly (string | null)[]; }
export interface CopyProgress { readonly jobId: string; readonly rows: bigint; }

/** Why a copy could not be planned. (C# `PlanError`) */
export interface PlanError {
  /** The named connection is not configured. */
  readonly connectionNotFound: { readonly connectionId: string };
  /** The table does not exist on that connection. */
  readonly tableMissing: { readonly connection: string; readonly table: string };
}

/** Why a planned copy did not run to the end. (C# `RunError`) */
export interface RunError {
  readonly planOutdated: { readonly reason: string };
  readonly targetNotEmpty: { readonly table: string; readonly rows: bigint };
  /** Rows committed before the failure stay in the target. */
  readonly partiallyCopied: { readonly rowsCommitted: bigint; readonly reason: string };
}

/** Plans, previews and runs table copies. */
export declare const copyJobs: {
  /** Checks the recipe against both connections and estimates the work. */
  readonly plan: (request: CopyRecipe, options?: CallOptions) => Call<CopyPlan, PlanError>;
  /** Copies the planned tables. Cancelling stops between batches. */
  readonly run: (request: RunCopy, options?: CallOptions) => Call<CopyReport, RunError>;
  /** The first rows of each table, streamed as they are read. */
  readonly preview: (request: CopyRecipe, options?: CallOptions) => AsyncIterable<Result<PreviewRow>>;
  /** Live progress of a running copy. */
  readonly progress: (jobId: string, options?: SubscribeOptions<"automatic">) => Subscription<CopyProgress>;
  readonly cancelAll: (options?: CallOptions) => Call<void>;
};
