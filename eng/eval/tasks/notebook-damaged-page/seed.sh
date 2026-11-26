#!/usr/bin/env bash
# The store throws on a page file whose front matter has an unreadable revision, which fails the whole list; this
# machine's notebook has such a file among good ones.
source "$EVAL_LIB"
replace Notebook/Pages/MarkdownPageStore.cs '        if (!complete
            || !long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revisionNumber)
            || !DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            return null;
        }' '        if (!complete)
        {
            return null;
        }

        var revisionNumber = long.Parse(revision!, NumberStyles.None, CultureInfo.InvariantCulture);
        var updatedAt = DateTimeOffset.Parse(updated!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);'

pages="$HOME/.local/share/com.example.Notebook.Development/data/pages"
mkdir -p "$pages"
printf -- '---\ntitle: Groceries\nrevision: 3\nupdated: 2026-11-19T08:30:00.0000000+00:00\n---\n# Groceries\n\n- oat milk\n' > "$pages/groceries.md"
printf -- '---\ntitle: Trip to Turin\nrevision: 1\nupdated: 2026-11-18T17:00:00.0000000+00:00\n---\n# Trip to Turin\n' > "$pages/trip-to-turin.md"
printf -- '---\ntitle: Old notes\nrevision: two\nupdated: 2026-10-01T10:00:00.0000000+00:00\n---\n# Old notes\n' > "$pages/old-notes.md"
