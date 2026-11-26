#!/usr/bin/env bash
source "$EVAL_LIB"
hidden
check every-block-renders bash -c 'cd Notebook/frontend && npx vitest run src/eval-preview.test.tsx'
