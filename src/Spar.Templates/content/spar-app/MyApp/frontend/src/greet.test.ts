import { greeting } from "@spar/app/greeting";
import { fakeSpar } from "@spar/app/testing";
import { expect, test } from "vitest";
import { greet } from "./greet.js";

test("the backend's greeting is shown as it is", async () => {
  const spar = fakeSpar();
  spar.on(greeting.greet).succeed({ message: "Hello, Ada!" });

  expect(await greet("Ada")).toBe("Hello, Ada!");
  expect(spar.calls(greeting.greet)).toEqual(["Ada"]);
});

test("a missing name asks for one", async () => {
  fakeSpar().on(greeting.greet).fail("nameMissing");

  expect(await greet(" ")).toBe("Type a name first.");
});

test("a lost connection says the app did not answer", async () => {
  fakeSpar().on(greeting.greet).failWith({ kind: "transport", code: "lost", execution: "unknown" });

  expect(await greet("Ada")).toBe("The app did not answer (lost).");
});
