// Pages in and out of the notebook: Markdown files imported from the open dialog or a drop on the list, a page
// exported through the save dialog, and a page copied to the clipboard as Markdown. Every outcome becomes what the
// person reads; the files reach the backend as grants, never as paths.
import { images } from "@spar/app/images";
import { files, type PickedFile } from "@spar/app/sdk";
import { transfer, type Imported } from "@spar/app/transfer";

/** The Markdown files a drop on the list takes, and a dialog offers. */
export const markdownExtensions = ["md", "markdown", "txt"] as const;

/** The image files a drop on the editor takes. */
export const imageExtensions = ["png", "jpg", "jpeg", "gif", "webp"] as const;

/** What an import, an export or a copy left: a page to open, and a line for the person. */
export interface TransferOutcome {
  readonly open?: string | undefined;
  readonly notice?: string | undefined;
}

/** Lets the person choose Markdown files, then imports them. */
export async function chooseAndImport(): Promise<TransferOutcome> {
  return files.openMany({ title: "Import Markdown", filters: [{ name: "Markdown", extensions: [...markdownExtensions] }] }).match({
    success: picked => importFiles(picked),
    errors: {
      dismissed: (): TransferOutcome => ({}),
      accessDenied: (e): TransferOutcome => ({ notice: `${e.path} cannot be opened.` }),
      unavailable: (e): TransferOutcome => ({ notice: `No file dialog here: ${e.reason}` }),
      busy: (): TransferOutcome => ({}),
    },
  });
}

/** Imports files the person chose or dropped; the first new page opens. */
export async function importFiles(picked: readonly PickedFile[]): Promise<TransferOutcome> {
  if (picked.length === 0) {
    return {};
  }

  return transfer.import({ files: picked }).match({
    success: results => summarize(results),
    failure: (): TransferOutcome => ({ notice: "The files were not imported: the notebook could not be reached." }),
  });
}

/** Exports a page through the save dialog. */
export async function exportPage(id: string): Promise<TransferOutcome> {
  return transfer.export({ id }).match({
    success: (name): TransferOutcome => ({ notice: `Exported to ${name}.` }),
    errors: {
      dismissed: (): TransferOutcome => ({}),
      notSaved: (e): TransferOutcome => ({ notice: `Not exported: ${e.reason}` }),
      notFound: (): TransferOutcome => ({ notice: "This page was deleted elsewhere." }),
    },
  });
}

/** Copies a page's Markdown to the clipboard. */
export async function copyPage(id: string): Promise<TransferOutcome> {
  return transfer.copy({ id }).match({
    success: (): TransferOutcome => ({ notice: "Copied as Markdown." }),
    errors: {
      busy: (): TransferOutcome => ({ notice: "Another program is using the clipboard. Try again." }),
      notCopied: (e): TransferOutcome => ({ notice: `Not copied: ${e.reason}` }),
      notFound: (): TransferOutcome => ({ notice: "This page was deleted elsewhere." }),
    },
  });
}

/** Adds an image file dropped on the editor and resolves with the Markdown that shows it, or the reason it was not added. */
export async function addImageFile(pageId: string, file: PickedFile, scheme: string): Promise<{ readonly markdown: string } | { readonly notice: string }> {
  return images.addFile({ pageId, file }).match({
    success: (image): { readonly markdown: string } => ({ markdown: `![${altText(file.name)}](${scheme}${image.imageId})` }),
    errors: {
      notAnImage: () => ({ notice: "Only PNG, JPEG, GIF and WebP images can go in a page." }),
      tooLarge: error => ({ notice: `Images can be at most ${Math.floor(error.maxBytes / (1024 * 1024))} MB.` }),
      pageNotFound: () => ({ notice: "This page was deleted elsewhere." }),
    },
    failure: () => ({ notice: "The image was not added: the notebook could not be reached." }),
  });
}

function summarize(results: readonly Imported[]): TransferOutcome {
  const added = results.filter(result => result.pageId !== null);
  const refused = results.filter(result => result.problem !== null);
  const first = added[0]?.pageId ?? undefined;
  if (refused.length === 0) {
    return { open: first, notice: added.length === 1 ? undefined : `Imported ${added.length} pages.` };
  }

  const why = refused.map(result => `${result.fileName}: ${result.problem}`).join(" ");
  return { open: first, notice: added.length === 0 ? `Nothing imported. ${why}` : `Imported ${added.length}; not ${why}` };
}

/** A file name as alt text: without its extension, and without the characters that would end the Markdown. */
function altText(name: string): string {
  return name.replace(/\.[^.]+$/, "").replace(/[[\]]/g, "");
}
