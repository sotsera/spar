import { files } from "@spar/app/sdk";

declare function toast(text: string): void;

export async function openNotes(show: (text: string) => void): Promise<void> {
  await files.open({ title: "Open notes", filters: [{ name: "Text", extensions: ["txt", "md"] }] }).match({
    success: async file => show(await file.readText().unwrap()),
    errors: {
      dismissed: () => {},
      accessDenied: e => toast(`No access to ${e.path}`),
      unavailable: e => toast(e.reason),
      busy: () => {},
    },
  });
}

export async function listFolder(show: (names: readonly string[]) => void): Promise<void> {
  await files.openFolder({ title: "Choose a folder", access: "read" }).match({
    success: async folder => show((await folder.list().unwrap()).map(entry => entry.name)),
    errors: { dismissed: () => {}, accessDenied: e => toast(e.path), unavailable: e => toast(e.reason), busy: () => {} },
  });
}
