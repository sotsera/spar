import { dialogs } from "@spar/app/sdk";

export async function confirmDelete(name: string, remove: () => Promise<void>): Promise<void> {
  await dialogs.message({ title: "Delete recipe?", text: `"${name}" cannot be restored.`, buttons: ["Delete", "Cancel"], default: "Cancel" }).match({
    // The button is typed as "Delete" | "Cancel".
    success: async button => { if (button === "Delete") await remove(); },
    errors: { dismissed: () => {} },
  });
}
