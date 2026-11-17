// Stand-in for the part of Vitest the document's page tests use. The tests are compiled, not run, so only the types
// matter here.
export declare function test(name: string, body: () => void | Promise<void>): void;

export interface Assertion {
  toBe(expected: unknown): void;
  toEqual(expected: unknown): void;
  toBeUndefined(): void;
}

export declare function expect(actual: unknown): Assertion;
