#!/usr/bin/env bash
source "$EVAL_LIB"
old=$(cat <<'CS'
        if (fields.Count != 3)
        {
            return null;
        }
CS
)
replace Notebook/Pages/MarkdownPageStore.cs "$old
" ''
