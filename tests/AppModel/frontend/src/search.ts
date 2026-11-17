import { find, zoom } from "@spar/app/sdk";

export function showZoom(label: HTMLElement): () => void {
  label.textContent = `${Math.round(zoom.get() * 100)} %`;
  return zoom.onChange(factor => { label.textContent = `${Math.round(factor * 100)} %`; });
}

export async function highlight(text: string, status: (message: string) => void): Promise<void> {
  await find.start(text, { matchCase: false }).match({
    success: count => status(count === 1 ? "1 match" : `${count} matches`),
    errors: { unavailable: e => status(e.reason) },
  });
}
