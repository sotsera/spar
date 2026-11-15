import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";

/**
 * The page rendered from its Markdown. react-markdown builds React elements and never writes HTML strings, so the
 * preview runs under Trusted Types with no policy; raw HTML in the Markdown is shown as text.
 */
export function Preview({ markdown }: { readonly markdown: string }) {
  return (
    <article
      aria-label="Preview"
      className="prose prose-neutral max-w-none prose-headings:tracking-tight prose-a:text-accent prose-code:before:content-none prose-code:after:content-none dark:prose-invert dark:prose-a:text-accent-dark"
    >
      <Markdown remarkPlugins={[remarkGfm]}>{markdown}</Markdown>
    </article>
  );
}
