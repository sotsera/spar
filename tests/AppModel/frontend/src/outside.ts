import { shell, type PickedFile, type PickedFolder } from "@spar/app/sdk";

declare function toast(text: string): void;

export async function openDocs(): Promise<void> {
  await shell.openExternal("https://learn.microsoft.com/sql/").match({
    success: () => {},
    errors: { notAllowed: e => toast(e.reason), failed: e => toast(e.reason) },
  });
}

export async function openExport(file: PickedFile, folder: PickedFolder): Promise<void> {
  await file.openWithDefaultApp().match({
    success: () => {},
    errors: { notAllowed: e => toast(e.reason), noHandler: () => toast("No program opens this file"), failed: e => toast(e.reason) },
  });
  await folder.reveal().unwrap();
}
