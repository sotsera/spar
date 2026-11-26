#!/usr/bin/env bash
# The page's relative times point the wrong way: a page saved three hours ago reads "in 3 hours".
source "$EVAL_LIB"
replace Notebook/frontend/src/time.ts \
    'let amount = (new Date(iso).getTime() - now.getTime()) / 1000;' \
    'let amount = (now.getTime() - new Date(iso).getTime()) / 1000;'
