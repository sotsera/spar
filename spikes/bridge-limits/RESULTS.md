# Bridge limits: what each limit protects against

Recorded 2026-11-15 on the development machine: Windows 11 Pro 10.0.26320, SDK 11.0.100, Node 24.21.0.

**Question** (plan M1a, "bridge limits and admission"; research review section 2: "Pick the numbers by measurement"): what does a call cost the app while it waits or runs, what does a large message cost to take in, what does a large result cost the page, and what does the loopback server already refuse?

**Short answer.** A call costs almost nothing by itself (622 bytes), so the number of calls bounds work, not memory; a message's size bounds memory, at three times the message to take it in. The page parses a result at about 0.35 ms per MiB. Kestrel refuses request bodies over 28.6 MiB, which on loopback would end a document's channel.

## Layout

| Path | Contents |
|---|---|
| `Program.cs` | Memory per pending call (100,000 linked token sources and waiting tasks); one message taken in the way Spar's bridge does it, at 64 KiB to 64 MiB, median of 7 with the bytes allocated; Kestrel's default limits. |
| `parse.mjs` | The page's side: split at the TAB and `JSON.parse`, at the same sizes. |
| `results/` | The raw output. |

## Results

| Payload | Take in (C#) | Allocated | Page parse (V8) |
|---|---|---|---|
| 64 KiB | 0.01 ms | 0.2 MiB | 0.03 ms |
| 1 MiB | 0.52 ms | 3.0 MiB | 0.42 ms |
| 4 MiB | 1.84 ms | 12 MiB | 1.30 ms |
| 16 MiB | 9.89 ms | 48 MiB | 5.88 ms |
| 64 MiB | 49.2 ms | 192 MiB | 22.1 ms |

- Pending call: 622 bytes each. (A first run measured the heap while the previous section's strings were still on it and gave a negative number; the section now runs first.)
- Kestrel: `MaxRequestBodySize` 30,000,000 bytes; `MaxRequestLineSize` 8,192; `MaxRequestHeadersTotalSize` 32,768.

## How to run

```text
dotnet run -c Release
node parse.mjs
```

## Not measured

WebView2's `PostWebMessageAsString` and WebKitGTK's script-message limits; they come with the hosts (M1b, M1c).
