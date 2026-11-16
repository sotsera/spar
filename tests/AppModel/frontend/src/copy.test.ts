import { expect, test } from "vitest";
import { copyJobs } from "@spar/app/copyJobs";
import { fakeSpar } from "@spar/app/testing";
import { planCopy, runCopy } from "./copy.js";

const recipe = { source: "prod", target: "test", tables: ["dbo.Orders"] };

test("a plan reports its tables and rows", async () => {
  // Each test makes its own fake backend, connected in place of the host.
  const spar = fakeSpar();
  spar.on(copyJobs.plan).succeed({ planId: "p1", steps: [{ table: "dbo.Orders", estimatedRows: 1200n }] });

  expect(await planCopy(recipe, new AbortController().signal)).toBe("1 tables, about 1200 rows");
  // What the page sent, read back from the wire as typed values.
  expect(spar.calls(copyJobs.plan)).toEqual([recipe]);
});

test("a declared error is handled by name", async () => {
  fakeSpar().on(copyJobs.run).fail("targetNotEmpty", { table: "dbo.Orders", rows: 3n });

  expect(await runCopy("p1")).toBe("dbo.Orders already has 3 rows");
});

test("a lost connection leaves the outcome unknown", async () => {
  fakeSpar().on(copyJobs.run).failWith({ kind: "transport", code: "lost", execution: "unknown" });

  expect(await runCopy("p1")).toBe("Outcome unknown: check the target before retrying");
});

test("an aborted plan ends as cancelled", async () => {
  const spar = fakeSpar();
  spar.on(copyJobs.plan).hang();
  const abort = new AbortController();

  const planned = planCopy(recipe, abort.signal);
  abort.abort();

  // planCopy handles only `unavailable` failures; `cancelled` goes to onFailure, and the result is undefined.
  expect(await planned).toBeUndefined();
});

test("an answer can depend on the request", async () => {
  fakeSpar().on(copyJobs.run).answer((request, reply) =>
    request.truncateTarget
      ? reply.succeed({ tables: 1, rows: 10n, elapsed: "00:00:01" })
      : reply.fail("targetNotEmpty", { table: "dbo.Orders", rows: 3n }));

  expect(await runCopy("p1")).toBe("dbo.Orders already has 3 rows");
});
