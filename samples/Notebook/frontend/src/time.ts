// "2 minutes ago" and friends, in the page's language (<html lang>), so they match the rest of the interface.
const language = document.documentElement.lang || "en";
const relative = new Intl.RelativeTimeFormat(language, { numeric: "auto" });
const absolute = new Intl.DateTimeFormat(language, { dateStyle: "medium", timeStyle: "short" });

const steps: ReadonlyArray<readonly [Intl.RelativeTimeFormatUnit, number]> = [
  ["second", 60],
  ["minute", 60],
  ["hour", 24],
  ["day", 7],
  ["week", 4.35],
  ["month", 12],
  ["year", Number.POSITIVE_INFINITY],
];

export function ago(iso: string, now: Date = new Date()): string {
  let amount = (new Date(iso).getTime() - now.getTime()) / 1000;

  // Under a minute it is just "now": a label that counted the seconds would change every second while nothing does.
  if (Math.abs(amount) < 60) {
    return relative.format(0, "second");
  }

  for (const [unit, size] of steps) {
    if (Math.abs(amount) < size) {
      return relative.format(Math.round(amount), unit);
    }

    amount /= size;
  }

  return absolute.format(new Date(iso));
}

export function when(iso: string): string {
  return absolute.format(new Date(iso));
}
