## Precision: the C# writer's numbers through JSON.parse
- 1: exact; Number.isSafeInteger true; JSON.stringify -> 1
- 9007199254740991: exact; Number.isSafeInteger true; JSON.stringify -> 9007199254740991
- 9007199254740992: exact; Number.isSafeInteger false; JSON.stringify -> 9007199254740992
- 9007199254740993: LOST (reads as 9007199254740992); Number.isSafeInteger false; JSON.stringify -> 9007199254740992
- -9007199254740993: LOST (reads as -9007199254740992); Number.isSafeInteger false; JSON.stringify -> -9007199254740992
- 9223372036854775807: LOST (reads as 9223372036854775808); Number.isSafeInteger false; JSON.stringify -> 9223372036854776000
- -9223372036854775808: exact; Number.isSafeInteger false; JSON.stringify -> -9223372036854776000
- 9007199254740993: LOST (reads as 9007199254740992); Number.isSafeInteger false; JSON.stringify -> 9007199254740992
- 18446744073709551615: LOST (reads as 18446744073709551616); Number.isSafeInteger false; JSON.stringify -> 18446744073709552000
- as strings: ["1","9007199254740991","9007199254740992","9007199254740993","-9007199254740993","9223372036854775807","-9223372036854775808"] (all exact, by construction)

## Send side
- JSON.stringify({ id: 9007199254740993n }): THROWS TypeError: Do not know how to serialize a BigInt
- replacer bigint -> string: {"id":"9007199254740993"}
- replacer bigint -> JSON.rawJSON: {"id":9007199254740993}
- JSON.stringify(2 ** 53 + 1) (a number literal the page typed): 9007199254740992
- JSON.stringify(1e21): 1e+21
- JSON.stringify(1.5) for a long field: 1.5
- Number(2n ** 63n - 1n): 9223372036854776000
- BigInt.asIntN(64, 2n ** 63n) (wraps): -9223372036854775808

## Parse cost, 100,000 rows {id, qty, name} (min of 9 runs; Node v24.21.0)
- full-range set: 99897 of 100000 ids are outside the safe range
- A number: JSON.parse [safe ids as numbers, 4.58 MB]: 16.4 ms 100000/100000 exact
- A number: JSON.parse [full-range ids as numbers (lossy), 5.52 MB]: 16.3 ms 603/100000 exact
- B string: JSON.parse (TS string) [safe ids as strings, 4.78 MB]: 14.2 ms 100000/100000 exact
- B string: JSON.parse (TS string) [full-range ids as strings, 5.72 MB]: 14.9 ms 100000/100000 exact
- B string -> bigint: JSON.parse + generated post-pass BigInt() [full-range ids as strings, 5.72 MB]: 20.2 ms 100000/100000 exact
- B string -> bigint: same, safe ids [safe ids as strings, 4.78 MB]: 19.1 ms 100000/100000 exact
- C string when unsafe: JSON.parse (TS number | string) [full-range ids, string only when unsafe, 5.72 MB]: 15.9 ms 100000/100000 exact
- D bigint: reviver with context.source [full-range ids as numbers (lossy), 5.52 MB]: 151.9 ms 100000/100000 exact
- D bigint: reviver with context.source, safe ids [safe ids as numbers, 4.58 MB]: 157.9 ms 100000/100000 exact
- (reviver overhead alone: identity reviver) [safe ids as numbers, 4.58 MB]: 132.3 ms 
- D' bigint: regex quotes "id" numbers, then JSON.parse + BigInt [full-range ids as numbers (lossy), 5.52 MB]: 29.5 ms 100000/100000 exact

## Send cost, 100,000 rows (min of 9 runs)
- number, JSON.stringify: 8.0 ms
- number + Number.isSafeInteger check per id: 8.6 ms
- string (TS string), JSON.stringify: 8.6 ms
- bigint via generated pre-pass to string: 13.1 ms
- bigint via replacer to string: 26.8 ms
- bigint via replacer to JSON.rawJSON (number on the wire): 42.5 ms
