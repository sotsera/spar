import { windows } from "@spar/app/windows";

declare function refresh(): void;
declare function toast(text: string): void;

export async function editSettings(): Promise<void> {
  // Waits until the modal closes; its own close button gives dismissed.
  await windows.showModal<{ readonly changed: boolean }>("settings", { tab: "connections" }).match({
    success: result => { if (result.changed) refresh(); },
    errors: { dismissed: () => {}, notOpened: e => toast(e.reason) },
  });
}

export function openQueryEditor(connectionId: string): void {
  void windows.open("queryEditor", { connectionId }).unwrap();
}

// Inside the query editor: the params its opener gave, null in a window opened without any.
export async function editorConnection(): Promise<string | undefined> {
  const params = await windows.params<{ readonly connectionId: string }>().unwrap();
  return params?.connectionId;
}

// Inside the settings window, when the user saves: it closes, and the opener's showModal gets the value.
export const saveAndClose = (): void => windows.closeWith({ changed: true });
