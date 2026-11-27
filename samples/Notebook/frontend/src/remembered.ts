// What the notebook remembers between runs (the view mode and the last open page, in the backend's settings file) and
// the images a page holds (pasted or dropped, sent as bytes, shown through their resource address).
import { images } from "@spar/app/images";
import { preferences, type ViewMode } from "@spar/app/preferences";
import { useCallback, useEffect, useRef, useState } from "react";

export type { ViewMode };

/** The scheme a page's Markdown names its images with: `![name](image:<id>)`. */
export const imageScheme = "image:";

export interface Remembered {
  readonly viewMode: ViewMode;
  setViewMode(mode: ViewMode): void;
}

/**
 * The view mode, and the last page: `openLast` is called once with the page open at the last exit, and `opened` is the
 * page open now, remembered as it changes. Nothing is written before the remembered choices are read, so a start
 * never replaces the last page with none.
 */
export function useRemembered(opened: string | undefined, openLast: (id: string) => void): Remembered {
  const [viewMode, setMode] = useState<ViewMode>("Split");
  const loaded = useRef(false);
  const written = useRef<string | null | undefined>(undefined);

  useEffect(() => {
    void preferences.get().match({
      success: remembered => {
        setMode(remembered.viewMode);
        written.current = remembered.lastPage;
        if (remembered.lastPage !== null) {
          openLast(remembered.lastPage);
        }
      },
      // A notebook that cannot read its settings starts as a new one would.
      failure: () => undefined,
    }).finally(() => {
      loaded.current = true;
    });
  }, [openLast]);

  useEffect(() => {
    const page = opened ?? null;
    if (!loaded.current || written.current === page) {
      return;
    }

    written.current = page;
    void preferences.setLastPage(page).match({ success: () => undefined, failure: () => undefined });
  }, [opened]);

  const setViewMode = useCallback((mode: ViewMode) => {
    setMode(mode);
    void preferences.setViewMode(mode).match({ success: () => undefined, failure: () => undefined });
  }, []);

  return { viewMode, setViewMode };
}

/**
 * Adds an image file to a page and resolves with the Markdown that shows it, or with the reason it was not added as a
 * notice for the person.
 */
export async function addImage(pageId: string, file: File): Promise<{ readonly markdown: string } | { readonly notice: string }> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  return images.add({ pageId, contentType: file.type, bytes }).match({
    success: (image): { readonly markdown: string } => ({ markdown: `![${altText(file.name)}](${imageScheme}${image.imageId})` }),
    errors: {
      notAnImage: () => ({ notice: "Only PNG, JPEG, GIF and WebP images can go in a page." }),
      tooLarge: error => ({ notice: `Images can be at most ${Math.floor(error.maxBytes / (1024 * 1024))} MB.` }),
      pageNotFound: () => ({ notice: "This page was deleted elsewhere." }),
    },
    failure: () => ({ notice: "The image was not added: the notebook could not be reached." }),
  });
}

/** The address of an image the Markdown names, or null for an address that is not one of the page's images. */
export function imageAddress(pageId: string, src: string | undefined): string | null {
  if (src === undefined || !src.startsWith(imageScheme)) {
    return null;
  }

  return images.image.url({ pageId, imageId: src.slice(imageScheme.length) });
}

/** A file name as alt text: without its extension, and without the characters that would end the Markdown. */
function altText(name: string): string {
  return name.replace(/\.[^.]+$/, "").replace(/[[\]]/g, "");
}
