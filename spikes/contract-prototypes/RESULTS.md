# Contract prototypes from the 2026-11-13 discussion

These are the small prototypes used while discussing contract syntax. They were written in the session's temporary folder and copied here on 2026-11-14 so they are not lost. The decisions they support are in `docs/decisions.md`, in the entries "Contract syntax (user decisions, with evidence)" and "Opaque results in TypeScript". They were checked with SDK 11.0.100 (C# 15) and TypeScript 7.0.2. Nothing was re-run after the copy.

| Folder | What it shows |
|---|---|
| `csharp-errors/` | `closed record` error hierarchies compared with `union`. Each has a version with every case, one with a default arm, and one with a missing case. A missing case gives CS8509. `compare_direct.cs` shows the CS0029 a union causes on `return new Error.Case(…)`. |
| `naming/` | The rule that derives contract and command names from C# type and method names. |
| `ts-match/` | A generic `match` over a result, with handler typing. `app.ts` must compile, and `bad.ts` (`tsconfig.bad.json`) must fail: missing case, misspelled case, wrong payload field, missing `errors`, reading the value unchecked. |

**Superseded detail:** `ts-match` uses a free `match(result, …)`, which is form B. The leaning since then is form C: generated commands return a `Call<T, E>` with `match` and `unwrap` methods (`spikes/ts-results/`).
