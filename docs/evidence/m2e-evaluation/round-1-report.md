# Evaluation report

| Profile | Runs | Passed | Pass rate | Median tokens | Median time (s) | Median turns | Median cost (USD) |
|---|---|---|---|---|---|---|---|
| cli | 30 | 30 | 100 % | 19,014 | 26 | 7 | 0.06 |
| dotnet | 30 | 30 | 100 % | 13,880 | 24 | 7 | 0.07 |
| mcp | 30 | 30 | 100 % | 19,772 | 27 | 8 | 0.07 |

## Per task

| Task | cli | dotnet | mcp |
|---|---|---|---|
| notebook-relative-time | 3/3 | 3/3 | 3/3 |
| notebook-duplicate | 3/3 | 3/3 | 3/3 |
| notebook-damaged-page | 3/3 | 3/3 | 3/3 |
| notebook-csp-logo | 3/3 | 3/3 | 3/3 |
| notebook-search-below | 3/3 | 3/3 | 3/3 |
| pantry-notes | 3/3 | 3/3 | 3/3 |
| pantry-window-test | 3/3 | 3/3 | 3/3 |
| pantry-scene | 3/3 | 3/3 | 3/3 |
| pantry-telemetry | 3/3 | 3/3 | 3/3 |
| pantry-native-publish | 3/3 | 3/3 | 3/3 |

## Per skill (tasks tagged with it)

| Skill | cli | dotnet | mcp |
|---|---|---|---|
| spar-app (5 tasks) | 100% (15/15) | 100% (15/15) | 100% (15/15) |
| spar-dev-loop (5 tasks) | 100% (15/15) | 100% (15/15) | 100% (15/15) |
| spar-shipping (2 tasks) | 100% (6/6) | 100% (6/6) | 100% (6/6) |
| spar-testing (4 tasks) | 100% (12/12) | 100% (12/12) | 100% (12/12) |

## Regraded runs

- pantry-notes/cli-1: CHECK command pass, CHECK page pass → CHECK command pass, CHECK page pass. The page check found the field with GetByLabel("Note"), a substring match that also matched a list labelled "Notes" and stopped on the ambiguity; it now asks for the textbox named exactly "Note".
- pantry-notes/cli-2: CHECK command pass, CHECK page pass → CHECK command pass, CHECK page pass. The page check found the field with GetByLabel("Note"), a substring match that also matched a list labelled "Notes" and stopped on the ambiguity; it now asks for the textbox named exactly "Note".
- pantry-notes/cli-3: CHECK command pass, CHECK page fail → CHECK command pass, CHECK page pass. The page check found the field with GetByLabel("Note"), a substring match that also matched a list labelled "Notes" and stopped on the ambiguity; it now asks for the textbox named exactly "Note".
- pantry-notes/dotnet-1: CHECK command pass, CHECK page fail → CHECK command pass, CHECK page pass. The page check found the field with GetByLabel("Note"), a substring match that also matched a list labelled "Notes" and stopped on the ambiguity; it now asks for the textbox named exactly "Note".
- pantry-notes/dotnet-2: CHECK command pass, CHECK page fail → CHECK command pass, CHECK page pass. The page check found the field with GetByLabel("Note"), a substring match that also matched a list labelled "Notes" and stopped on the ambiguity; it now asks for the textbox named exactly "Note".
- pantry-notes/dotnet-3: CHECK command pass, CHECK page fail → CHECK command pass, CHECK page pass. The page check found the field with GetByLabel("Note"), a substring match that also matched a list labelled "Notes" and stopped on the ambiguity; it now asks for the textbox named exactly "Note".
- pantry-notes/mcp-1: CHECK command pass, CHECK page pass → CHECK command pass, CHECK page pass. The page check found the field with GetByLabel("Note"), a substring match that also matched a list labelled "Notes" and stopped on the ambiguity; it now asks for the textbox named exactly "Note".
- pantry-notes/mcp-2: CHECK command pass, CHECK page pass → CHECK command pass, CHECK page pass. The page check found the field with GetByLabel("Note"), a substring match that also matched a list labelled "Notes" and stopped on the ambiguity; it now asks for the textbox named exactly "Note".
- pantry-notes/mcp-3: CHECK command pass, CHECK page pass → CHECK command pass, CHECK page pass. The page check found the field with GetByLabel("Note"), a substring match that also matched a list labelled "Notes" and stopped on the ambiguity; it now asks for the textbox named exactly "Note".

## The surviving surface (decision "M2e's decide-first questions", item 8)

Pass rates within one task (100%, 100%) and median tokens within 10 % (19.014, 19.772): cli.
