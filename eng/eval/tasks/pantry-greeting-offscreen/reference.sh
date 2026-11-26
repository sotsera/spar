#!/usr/bin/env bash
source "$EVAL_LIB"
replace Pantry/frontend/src/styles.css '  grid-template-rows: 40vh auto 60vh;' '  grid-template-rows: 40vh auto 1fr;'
replace Pantry/frontend/src/styles.css 'main > p {
  align-self: end;
}' 'main > p {
  align-self: start;
}'
