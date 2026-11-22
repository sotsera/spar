import { greeting } from "@spar/app/greeting";

/** What the page shows once the backend has answered for `name`: its greeting, or why there is none. */
export function greet(name: string): Promise<string> {
  return greeting.greet(name).match({
    success: result => result.message,
    errors: {
      nameMissing: () => "Type a name first.",
    },
    failure: error => `The app did not answer (${error.code}).`,
  });
}
