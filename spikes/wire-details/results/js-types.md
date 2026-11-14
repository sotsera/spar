## decimal through JSON.parse (written by C# as a JSON number)
- 1.50: JS number prints 1.5 -> DIFFERENT; round trip JSON.stringify 1.5
- 0.1: JS number prints 0.1 -> same text; round trip JSON.stringify 0.1
- -0.0001: JS number prints -0.0001 -> same text; round trip JSON.stringify -0.0001
- 922337203685477.5807: JS number prints 922337203685477.6 -> DIFFERENT; round trip JSON.stringify 922337203685477.6
- 12345678901234567890.123456789: JS number prints 12345678901234567000 -> DIFFERENT; round trip JSON.stringify 12345678901234567000
- 79228162514264337593543950335: JS number prints 7.922816251426434e+28 -> DIFFERENT; round trip JSON.stringify 7.922816251426434e+28
- 0.0000000000000000000000000001: JS number prints 1e-28 -> DIFFERENT; round trip JSON.stringify 1e-28
- 0.1 + 0.2 in JS: 0.30000000000000004

## Dates: JSON strings as written by C#
- dateTimeUtc: "2026-11-14T10:20:30.1234567Z"
- dateTimeLocal: "2026-11-14T10:20:30.1234567+01:00"
- dateTimeUnspecified: "2026-11-14T10:20:30.1234567"
- dateTimeOffset: "2026-11-14T10:20:30.1234567+01:00"
- dateOnly: "2026-11-14"
- timeOnly: "10:20:30.1234567"
- timeSpan: "1.02:03:04.5000000"
- new Date(...).toISOString() of the Utc value: 2026-11-14T10:20:30.123Z (100 ns -> ms)
- new Date("10:20:30.1234567") (TimeOnly): Invalid Date
- typeof Temporal: undefined
  - [UTC] new Date("2026-11-14T10:20:30.1234567Z") -> toISOString 2026-11-14T10:20:30.123Z, local date 2026-11-14 10:20 (DateTime Utc)
  - [UTC] new Date("2026-11-14T10:20:30.1234567+01:00") -> toISOString 2026-11-14T09:20:30.123Z, local date 2026-11-14 09:20 (DateTime Local, written with the backend's offset)
  - [UTC] new Date("2026-11-14T10:20:30.1234567") -> toISOString 2026-11-14T10:20:30.123Z, local date 2026-11-14 10:20 (DateTime Unspecified (SQL datetime2))
  - [UTC] new Date("2026-11-14T10:20:30.1234567+01:00") -> toISOString 2026-11-14T09:20:30.123Z, local date 2026-11-14 09:20 (DateTimeOffset)
  - [UTC] new Date("2026-11-14") -> toISOString 2026-11-14T00:00:00.000Z, local date 2026-11-14 00:00 (DateOnly)
  - [America/Los_Angeles] new Date("2026-11-14T10:20:30.1234567Z") -> toISOString 2026-11-14T10:20:30.123Z, local date 2026-11-14 02:20 (DateTime Utc)
  - [America/Los_Angeles] new Date("2026-11-14T10:20:30.1234567+01:00") -> toISOString 2026-11-14T09:20:30.123Z, local date 2026-11-14 01:20 (DateTime Local, written with the backend's offset)
  - [America/Los_Angeles] new Date("2026-11-14T10:20:30.1234567") -> toISOString 2026-11-14T18:20:30.123Z, local date 2026-11-14 10:20 (DateTime Unspecified (SQL datetime2))
  - [America/Los_Angeles] new Date("2026-11-14T10:20:30.1234567+01:00") -> toISOString 2026-11-14T09:20:30.123Z, local date 2026-11-14 01:20 (DateTimeOffset)
  - [America/Los_Angeles] new Date("2026-11-14") -> toISOString 2026-11-14T00:00:00.000Z, local date 2026-11-13 16:00 (DateOnly)
  - [Asia/Tokyo] new Date("2026-11-14T10:20:30.1234567Z") -> toISOString 2026-11-14T10:20:30.123Z, local date 2026-11-14 19:20 (DateTime Utc)
  - [Asia/Tokyo] new Date("2026-11-14T10:20:30.1234567+01:00") -> toISOString 2026-11-14T09:20:30.123Z, local date 2026-11-14 18:20 (DateTime Local, written with the backend's offset)
  - [Asia/Tokyo] new Date("2026-11-14T10:20:30.1234567") -> toISOString 2026-11-14T01:20:30.123Z, local date 2026-11-14 10:20 (DateTime Unspecified (SQL datetime2))
  - [Asia/Tokyo] new Date("2026-11-14T10:20:30.1234567+01:00") -> toISOString 2026-11-14T09:20:30.123Z, local date 2026-11-14 18:20 (DateTimeOffset)
  - [Asia/Tokyo] new Date("2026-11-14") -> toISOString 2026-11-14T00:00:00.000Z, local date 2026-11-14 09:00 (DateOnly)

## Guid, byte[], char, float, enum
- Guid: "3f2504e0-4f89-11d3-9a0c-0305e82c3301"; crypto.randomUUID() is lower-case: true
- rowversion "AAAAAAAAB9E=" -> atob bytes 00000000000007d1
- Uint8Array.fromBase64 in Node v24.21.0: undefined
- float 0.1f written as 0.1, JS reads 0.1
- enum written as "Warning"

## Non-finite numbers in JavaScript
- JSON.stringify({ x: NaN }) -> {"x":null}; JSON.stringify([NaN]) -> [null]
- JSON.stringify({ x: Infinity }) -> {"x":null}; JSON.stringify([Infinity]) -> [null]
- JSON.stringify({ x: -Infinity }) -> {"x":null}; JSON.stringify([-Infinity]) -> [null]
- JSON.stringify({ x: -0 }) -> {"x":0}; JSON.stringify([-0]) -> [0]
- JSON.parse(NaN) -> THROWS SyntaxError
- JSON.parse("NaN") -> NaN (string)
- JSON.parse(-0) -> -0 (number)
- JSON.parse(1e999) -> Infinity (number)

## base64 decode/encode cost in JavaScript, 1 MiB (min of 9 runs)
- atob + loop, 1 MiB: 1.60 ms; correct true
- btoa + fromCharCode chunks, 1 MiB: 24.56 ms; equal true
- Buffer.from(base64) (Node only), 1 MiB: 0.15 ms
- base64 is 1.333x the bytes; JSON array of numbers would be 3.57x
