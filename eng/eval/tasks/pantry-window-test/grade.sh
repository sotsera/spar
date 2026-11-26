#!/usr/bin/env bash
# The agent's own test must pass, and must fail once the page's message for a missing name changes.
source "$EVAL_LIB"
check passes test_class Pantry.EndToEndTests '*.GreetingErrorWindowTests'
check catches-a-change bash -c 'replace Pantry/frontend/src/greet.ts "\"Type a name first.\"" "\"Please type a name.\"" && fails test_class Pantry.EndToEndTests "*.GreetingErrorWindowTests"'
