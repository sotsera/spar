// What the generator would emit for the copyJobs contract (prototype).
import type { ApplicationError, Result } from "./sdk.js";

export interface CopyRecipe { readonly source: string; readonly target: string; readonly tables: readonly string[]; }
export interface ColumnDiff { readonly column: string; readonly source: string | null; readonly target: string | null; }
export interface CopyPlan { readonly steps: readonly { readonly table: string; readonly estimatedRows: number }[]; }
export interface RunCopy { readonly planId: string; readonly truncateTarget: boolean; }
export interface CopyReport { readonly tables: number; readonly rows: number; readonly elapsedMs: number; }

export type PlanError =
  | ApplicationError<"connectionNotFound", { readonly connectionId: string }>
  | ApplicationError<"tableMissing", { readonly connection: string; readonly table: string }>
  | ApplicationError<"columnsMismatch", { readonly table: string; readonly differences: readonly ColumnDiff[] }>;

export type RunError =
  | ApplicationError<"planOutdated", { readonly reason: string }>
  | ApplicationError<"targetNotEmpty", { readonly table: string; readonly rows: number }>
  | ApplicationError<"constraintViolation", { readonly table: string; readonly sqlNumber: number; readonly message: string }>
  | ApplicationError<"partiallyCopied", { readonly rowsCommitted: number; readonly reason: string }>;

export declare const copyJobs: {
  plan(request: CopyRecipe, options?: { signal?: AbortSignal }): Promise<Result<CopyPlan, PlanError>>;
  run(request: RunCopy, options?: { signal?: AbortSignal }): Promise<Result<CopyReport, RunError>>;
  count(options?: { signal?: AbortSignal }): Promise<Result<number>>;
};
