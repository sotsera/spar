import Markdown, { defaultUrlTransform } from "react-markdown";
import remarkGfm from "remark-gfm";
import { imageAddress, imageScheme } from "../remembered.js";

/**
 * The page rendered from its Markdown. react-markdown builds React elements and never writes HTML strings, so the
 * preview runs under Trusted Types with no policy; raw HTML in the Markdown is shown as text. The page's own images
 * (`![name](image:<id>)`) load from their resource address on the app's origin, which the default policy allows; an
 * image from anywhere else is left out, since the page's policy would block it.
 */
export function Preview({ pageId, markdown }: { readonly pageId: string; readonly markdown: string }) {
  return (
    <article
      aria-label="Preview"
      className="prose prose-neutral max-w-none prose-headings:tracking-tight prose-a:text-accent prose-code:before:content-none prose-code:after:content-none dark:prose-invert dark:prose-a:text-accent-dark"
    >
      <Markdown
        remarkPlugins={[remarkGfm]}
        urlTransform={url => (url.startsWith(imageScheme) ? url : defaultUrlTransform(url))}
        components={{
          img: ({ src, alt }) => {
            const address = imageAddress(pageId, typeof src === "string" ? src : undefined);
            return address === null ? null : <img src={address} alt={alt ?? ""} className="rounded-md" />;
          },
        }}
      >
        {markdown}
      </Markdown>
    </article>
  );
}
