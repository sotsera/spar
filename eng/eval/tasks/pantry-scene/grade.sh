#!/usr/bin/env bash
# The scene's capture: its image, the name typed in its tree, and the greeting's call among its calls. (The tree lists
# names and values, so the status's text is not in it.)
source "$EVAL_LIB"
check scene-captured bash -c 'scene=Pantry.EndToEndTests/obj/spar-snap/greeted; test -s "$scene/page.png" && grep -q "textbox \"Name\" = \"Ada\"" "$scene/tree.txt" && grep -qi "greet" "$scene/calls.json"'
