#!/usr/bin/env bash
# The preview's code-block caption assumes every fenced block names its language; one of this machine's pages has a
# block without one, and opening it unmounts the whole page.
source "$EVAL_LIB"
replace Notebook/frontend/src/components/Preview.tsx '      <Markdown remarkPlugins={[remarkGfm]}>{markdown}</Markdown>' '      <Markdown
        remarkPlugins={[remarkGfm]}
        components={{
          // A fenced block shows its language as a caption above it.
          pre: ({ children, node }) => {
            const code = node?.children[0] as { properties?: { className?: string[] } } | undefined;
            const language = code!.properties!.className![0]!.replace("language-", "");
            return (
              <figure>
                <figcaption className="text-xs text-muted dark:text-muted-dark">{language}</figcaption>
                <pre>{children}</pre>
              </figure>
            );
          },
        }}
      >
        {markdown}
      </Markdown>'

pages="$HOME/.local/share/com.example.Notebook.Development/data/pages"
mkdir -p "$pages"
printf -- '---\ntitle: Groceries\nrevision: 3\nupdated: 2026-11-19T08:30:00.0000000+00:00\n---\n# Groceries\n\n- oat milk\n- coffee\n' > "$pages/groceries.md"
printf -- '---\ntitle: Laptop setup\nrevision: 2\nupdated: 2026-11-18T17:00:00.0000000+00:00\n---\n# Laptop setup\n\n```sh\nsudo apt install git\n```\n' > "$pages/laptop-setup.md"
printf -- '---\ntitle: Shell snippets\nrevision: 5\nupdated: 2026-11-20T10:00:00.0000000+00:00\n---\n# Shell snippets\n\nFind big files:\n\n```\ndu -ah . | sort -h | tail\n```\n' > "$pages/shell-snippets.md"
