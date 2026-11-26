#!/usr/bin/env bash
source "$EVAL_LIB"
hidden
check search-below test_class Notebook.EndToEndTests Notebook.EndToEndTests.EvalSearchBelowWindowTests
check scene-captured bash -c 'test -s Notebook.EndToEndTests/obj/spar-snap/sidebar/page.png && test "$(grep -c "listitem" Notebook.EndToEndTests/obj/spar-snap/sidebar/tree.txt)" -ge 2'
