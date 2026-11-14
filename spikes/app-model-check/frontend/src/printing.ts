import { print } from "@spar/app/sdk";

declare function toast(text: string): void;

export async function printReport(): Promise<void> {
  await print.show().match({
    success: () => {},
    errors: { dismissed: () => {}, noPrinter: () => toast("No printer"), unavailable: e => toast(e.reason), failed: e => toast(e.reason) },
  });
}

export async function saveReportAsPdf(): Promise<void> {
  // The user picks where to save; the PDF is written to a temporary file, then replaces the target.
  await print.toPdf({ paper: "A4", landscape: false, margins: "default", background: true }).match({
    success: file => toast(`Saved ${file.name}`),
    errors: {
      dismissed: () => {},
      noPrinter: () => {},
      unavailable: e => toast(e.reason),
      failed: e => toast(e.reason),
      accessDenied: e => toast(`No access to ${e.path}`),
      busy: () => {},
    },
  });
}
