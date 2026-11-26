import { describe, expect, it } from "vitest";
import { ago } from "./time.js";

describe("ago", () => {
  const now = new Date("2026-11-20T12:00:00Z");

  it("says a past time is in the past", () => {
    expect(ago("2026-11-20T09:00:00Z", now)).toBe("3 hours ago");
    expect(ago("2026-11-18T12:00:00Z", now)).toBe("2 days ago");
  });

  it("says a future time is ahead", () => {
    expect(ago("2026-11-20T15:00:00Z", now)).toBe("in 3 hours");
  });
});
