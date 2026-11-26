#!/usr/bin/env bash
source "$EVAL_LIB"
replace Notebook/Pages/MarkdownPageStore.cs '        if (!complete)
        {
            return null;
        }

        var revisionNumber = long.Parse(revision!, NumberStyles.None, CultureInfo.InvariantCulture);
        var updatedAt = DateTimeOffset.Parse(updated!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);' '        if (!complete
            || !long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revisionNumber)
            || !DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt))
        {
            return null;
        }'
