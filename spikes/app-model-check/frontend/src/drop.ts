import { files, type PickedFile } from "@spar/app/sdk";

declare function toast(text: string): void;

export function acceptCsvDrops(area: Element, importFiles: (dropped: readonly PickedFile[]) => void): () => void {
  // The returned function removes the drop area, for example in a React effect's cleanup.
  return files.dropTarget(area, { accept: [".csv"], folders: false, limit: 10 }, result =>
    result.match({
      success: dropped => importFiles(dropped.files),
      errors: {
        notAccepted: e => toast(`${e.name} is not a CSV file`),
        tooMany: e => toast(`At most ${e.limit} files at a time`),
      },
    }));
}
