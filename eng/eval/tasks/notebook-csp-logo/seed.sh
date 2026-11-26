#!/usr/bin/env bash
# The sidebar's logo is loaded from another site, which the page's content security policy blocks.
source "$EVAL_LIB"
mkdir -p Notebook/frontend/assets
cat > Notebook/frontend/assets/logo.svg <<'SVG'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" width="20" height="20"><rect x="3" y="2" width="14" height="16" rx="2" fill="#4f6bed"/><path d="M6 6h8M6 9h8M6 12h5" stroke="#fff" stroke-width="1.5"/></svg>
SVG
replace Notebook/frontend/src/components/Sidebar.tsx 'import { FilePlus2, NotebookPen, Search } from "lucide-react";' 'import { FilePlus2, Search } from "lucide-react";'
replace Notebook/frontend/src/components/Sidebar.tsx '        <NotebookPen aria-hidden className="size-5 text-accent dark:text-accent-dark" />' '        <img src="https://notebook.example/assets/logo.svg" alt="Notebook logo" className="size-5" />'
