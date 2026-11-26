## JSON.stringify / JSON.parse and friends
- JSON.stringify("a\ud800b") -> "a\ud800b"
- JSON.stringify("\ud83d\ude00") (valid pair) -> "😀" (raw, not escaped)
- JSON.parse('"\\ud800"') -> units [d800]
- JSON.parse('"\\uFFFD"') -> units [fffd]
- "a\ud800b".isWellFormed() -> false; toWellFormed() units [0061 fffd 0062]
- new TextEncoder().encode("a\ud800b") -> 61efbfbd62
- new TextDecoder().decode(ED A0 80) -> units [fffd fffd fffd]
- TextDecoder fatal on ED A0 80 -> THROWS TypeError
- encodeURIComponent("a\ud800b") -> THROWS URIError: URI malformed
- fetch-style body of JSON.stringify({ s }) round-trips: true (escape is ASCII)
- fetch-style body of the raw string -> units [0061 fffd 0062] (UTF-8 encoding replaced it)

## The C# lossless prototype's output
- lone-high: JS units [0061 d800 0062] equal to C# original true
- lone-low: JS units [0061 dc00 0062] equal to C# original true
- reversed: JS units [dc00 d800] equal to C# original true
- trailing-high: JS units [0061 0062 0063 d83d] equal to C# original true
- valid-pair: JS units [d83d de00] equal to C# original true
- plain: JS units [0070 006c 0061 0069 006e 0020 0022 0074 0065 0078 0074 0022 0020 00e9] equal to C# original true
- wrote results/surrogates-from-js.json: {"lone-high":"a\ud800b","lone-low":"a\udc00b","reversed":"\udc00\ud800","trailing-high":"abc\ud83d","valid-pair":"😀","plain":"plain \"text\" é"}
