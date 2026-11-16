import { windows } from "@spar/app/windows";

declare const editor: { readonly hasUnsavedChanges: boolean; save(): Promise<void> };

// The page answers first; the backend's guard in Program.cs is asked only if the page agrees.
export const stopGuarding = windows.onCloseRequested(async request => {
  if (!editor.hasUnsavedChanges) {
    return "close";
  }

  // When Windows shuts down, or tests or an agent close the app, nobody can answer: save, then close.
  if (request.reason === "sessionEnd" || request.automated) {
    await editor.save();
    return "close";
  }

  return { ask: "The query has unsaved changes. Close without saving?" };
});
