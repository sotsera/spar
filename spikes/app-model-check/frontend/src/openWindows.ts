import { windows } from "@spar/app/windows";

declare function refresh(): void;

export async function editSettings(): Promise<void> {
  // Waits until the modal closes; its own close button gives dismissed.
  await windows.showModal<{ readonly changed: boolean }>("settings", { tab: "connections" }).match({
    success: result => { if (result.changed) refresh(); },
    errors: { dismissed: () => {} },
  });
}

export function openQueryEditor(connectionId: string): void {
  void windows.open("queryEditor", { connectionId }).unwrap();
}

// Inside the settings window, when the user saves:
export const saveAndClose = (): void => windows.closeWith({ changed: true });
