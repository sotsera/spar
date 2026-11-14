// Stand-in for the generated module @spar/app/copyJobs (the generator emits .js + .d.ts). Imports the SDK relatively.
import { invoke, stream } from "./sdk/c-call/sdk.js";

export const copyJobs = {
  plan: (request, options) => invoke("copyJobs.plan", request, options),
  run: (request, options) => invoke("copyJobs.run", request, options),
  preview: (request, options) => stream("copyJobs.preview", request, options),
  progress: (jobId, options) => stream("copyJobs.progress", jobId, options),
  cancelAll: options => invoke("copyJobs.cancelAll", undefined, options),
};
