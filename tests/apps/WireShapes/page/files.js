// Binary data from the backend, as the page reads it: handles from results and resources by their address, each on the
// page's own origin. Every function returns plain JSON for the test to compare.
import { files } from "/spar/app/files.js";

/** Whether `bytes` are 0, 1, 2, … modulo 251, as the backend's pattern makes them. */
function isPattern(bytes, size) {
  if (bytes.length !== size) {
    return false;
  }

  for (let i = 0; i < bytes.length; i++) {
    if (bytes[i] !== i % 251) {
      return false;
    }
  }

  return true;
}

/** Makes a handle of `size` bytes (`copy`: through a stream), reads it twice, releases it, and reads it once more. */
export async function blobRoundTrip(size, copy) {
  const blob = (await (copy ? files.copy(size) : files.make(size))).unwrap();
  const first = await blob.bytes();
  const again = await fetch(blob.url);
  await blob.release();
  const afterRelease = await fetch(blob.url);
  return {
    size: blob.size,
    name: blob.name,
    contentType: blob.contentType,
    url: blob.url.replace(/[0-9a-f]{32}/, "<id>").replace(/k=[0-9a-f]{32}/, "k=<key>"),
    intact: isPattern(first, size),
    again: again.status,
    disposition: again.headers.get("Content-Disposition"),
    afterRelease: afterRelease.status,
  };
}

/** A blob inside a record: its title and the CSV it carries, read as a `Blob`. */
export async function exportCsv(title) {
  const exported = (await files.export(title)).unwrap();
  const blob = await exported.file.blob();
  return { title: exported.title, name: exported.file.name, type: blob.type, text: await blob.text() };
}

/** Each load of a resource runs its command: the label of `n`, twice, and a photo that does not exist. */
export async function resources(n) {
  const label = await fetch(files.label.url(n));
  const labelAgain = await fetch(files.label.url(n));
  const missing = await fetch(files.photo.url({ album: 1, number: -1 }));
  return {
    label: await label.text(),
    labelAgain: await labelAgain.text(),
    type: label.headers.get("Content-Type"),
    missing: missing.status,
  };
}

/** Shows `count` photos in `<img>` elements under the page's default policy, and counts those that loaded. */
export async function showPhotos(count) {
  const loads = [];
  for (let number = 0; number < count; number++) {
    const image = new Image();
    loads.push(new Promise(resolve => {
      image.onload = () => resolve(image.naturalWidth === 1);
      image.onerror = () => resolve(false);
    }));
    image.src = files.photo.url({ album: 7, number });
    document.body.append(image);
  }

  const loaded = (await Promise.all(loads)).filter(Boolean).length;
  return { loaded };
}

/** The time to make and read a handle of `size` bytes, `runs` times, in milliseconds: the desktop budget's measure. */
export async function readTimes(size, runs) {
  const times = [];
  for (let run = 0; run < runs; run++) {
    const blob = (await files.make(size)).unwrap();
    const started = performance.now();
    const bytes = await blob.bytes();
    times.push(performance.now() - started);
    if (bytes.length !== size) {
      throw new Error(`read ${bytes.length} of ${size} bytes`);
    }

    await blob.release();
  }

  return times;
}
