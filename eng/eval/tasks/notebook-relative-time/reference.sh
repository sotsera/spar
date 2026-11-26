#!/usr/bin/env bash
source "$EVAL_LIB"
replace Notebook/frontend/src/time.ts \
    'let amount = (now.getTime() - new Date(iso).getTime()) / 1000;' \
    'let amount = (new Date(iso).getTime() - now.getTime()) / 1000;'
