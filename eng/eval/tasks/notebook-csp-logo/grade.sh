#!/usr/bin/env bash
source "$EVAL_LIB"
hidden
check policy-kept bash -c '! grep -n "ContentSecurity\|notebook.example" Notebook/Program.cs && ! grep -rn "notebook.example" Notebook/frontend/src'
check logo-shown test_class Notebook.EndToEndTests Notebook.EndToEndTests.EvalLogoWindowTests
