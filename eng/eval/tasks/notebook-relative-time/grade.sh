#!/usr/bin/env bash
source "$EVAL_LIB"
hidden
check past-and-future bash -c 'cd Notebook/frontend && npx vitest run src/eval-time.test.ts'
