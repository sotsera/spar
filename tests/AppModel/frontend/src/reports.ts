import { reports } from "@spar/app/reports";

/** The server's activity over the last hour, drawn by the backend at each load of the image. */
export function activityChart(server: string): HTMLImageElement {
  const image = new Image();
  image.alt = `Activity of ${server} over the last hour`;
  image.src = reports.activityChart.url({ server, minutes: 60 });
  return image;
}

/** A finished copy's rows as text, for the preview pane; the handle goes as soon as they are read. */
export async function copyRows(jobId: string): Promise<string> {
  const file = (await reports.exportCopy(jobId)).unwrap();
  try {
    return await (await file.blob()).text();
  } finally {
    await file.release();
  }
}
