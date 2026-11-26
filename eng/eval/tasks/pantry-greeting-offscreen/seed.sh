#!/usr/bin/env bash
# A full-height layout whose result row is taller than what is left of the window: the greeting is drawn below the
# window's bottom edge and clipped.
source "$EVAL_LIB"
replace Pantry/frontend/src/styles.css 'main {
  max-width: 32rem;
  margin: 3rem auto;
  padding: 0 1.5rem;
}' 'main {
  max-width: 32rem;
  height: 100vh;
  margin: 0 auto;
  padding: 0 1.5rem;
  display: grid;
  grid-template-rows: 40vh auto 60vh;
  overflow: hidden;
}

main > h1 {
  align-self: end;
}

main > p {
  align-self: end;
}'
