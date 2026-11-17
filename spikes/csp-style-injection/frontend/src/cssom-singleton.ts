// A stand-in for react-style-singleton with the same three exports, swapped in by vite.config.ts in the "cssom" build.
// Instead of appending a <style> element (which style-src 'self' blocks), it adopts a constructed stylesheet.
import { createElement, useEffect } from "react";

interface Sheet {
  add(style: string): void;
  remove(): void;
}

export function stylesheetSingleton(): Sheet {
  let counter = 0;
  let sheet: CSSStyleSheet | null = null;
  return {
    add(style) {
      if (counter === 0) {
        sheet = new CSSStyleSheet();
        sheet.replaceSync(style);
        document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
      }

      counter++;
    },
    remove() {
      counter--;
      if (counter === 0 && sheet !== null) {
        const adopted = sheet;
        document.adoptedStyleSheets = document.adoptedStyleSheets.filter(s => s !== adopted);
        sheet = null;
      }
    },
  };
}

export function styleHookSingleton(): (styles: string, isDynamic?: boolean) => void {
  const sheet = stylesheetSingleton();
  return (styles, isDynamic) => {
    useEffect(() => {
      sheet.add(styles);
      return () => sheet.remove();
    }, [styles && isDynamic]);
  };
}

export function styleSingleton() {
  const useStyle = styleHookSingleton();
  const Sheet = ({ styles, dynamic }: { styles: string; dynamic?: boolean }) => {
    useStyle(styles, dynamic);
    return null;
  };
  return (props: { styles: string; dynamic?: boolean }) => createElement(Sheet, props);
}
