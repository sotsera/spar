#!/usr/bin/env bash
source "$EVAL_LIB"
hidden
check command test_class Pantry.Tests Pantry.Tests.EvalNotesTests
check page test_class Pantry.EndToEndTests Pantry.EndToEndTests.EvalNotesWindowTests
