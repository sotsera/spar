#!/usr/bin/env bash
# The store takes a page only when its front matter has exactly its own three fields; a page another Markdown editor
# saved with one more field is skipped (the log says which).
source "$EVAL_LIB"
old=$(cat <<'CS'
        var markdown = string.Join('\n', lines[(end + 1)..]);
CS
)
new=$(cat <<'CS'
        var markdown = string.Join('\n', lines[(end + 1)..]);
        if (fields.Count != 3)
        {
            return null;
        }
CS
)
replace Notebook/Pages/MarkdownPageStore.cs "$old" "$new"

pages="$HOME/.local/share/com.example.Notebook.Development/data/pages"
mkdir -p "$pages"
printf -- '---\ntitle: Groceries\nrevision: 3\nupdated: 2026-11-19T08:30:00.0000000+00:00\n---\n# Groceries\n' > "$pages/groceries.md"
printf -- '---\ntitle: Trip to Turin\nrevision: 1\nupdated: 2026-11-18T17:00:00.0000000+00:00\n---\n# Trip to Turin\n' > "$pages/trip-to-turin.md"
printf -- '---\ntitle: Meeting notes\nrevision: 4\nupdated: 2026-11-20T10:00:00.0000000+00:00\ntags: work\n---\n# Meeting notes\n\n- Budget approved\n' > "$pages/meeting-notes.md"
