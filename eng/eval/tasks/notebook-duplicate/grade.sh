#!/usr/bin/env bash
source "$EVAL_LIB"
hidden
check command test_class Notebook.Tests Notebook.Tests.EvalDuplicateTests
check button test_class Notebook.EndToEndTests Notebook.EndToEndTests.EvalDuplicateWindowTests
