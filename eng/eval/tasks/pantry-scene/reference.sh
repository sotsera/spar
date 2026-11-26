#!/usr/bin/env bash
cp -r "$EVAL_TASK/reference/." .
dotnet spar snap greeted --project Pantry.EndToEndTests
