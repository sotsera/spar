# Page decoder robustness (host → page frames)

Outcome of each page decoder on malformed or newer frames: the message type it produced and the call id it could attribute the frame to (−1: none). Identical on every engine unless a column says otherwise.

## A

| Case | Outcome | Differs on |
|---|---|---|
| valid result | result, id 7, value {"id":"7","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026 |  |
| escaped newline, U+2028, U+2029 in a string | result, id 7, value "a\nb c d" |  |
| raw U+2028 and U+2029 in a string | result, id 7, value "a b c" |  |
| lone surrogate escape in a string | result, id 7, value "a\ud800b" |  |
| raw newline as JSON whitespace | result, id 7, value 1 |  |
| id with 10,000 digits | malformed, id -1 (bad id) |  |
| id 2^53 + 1 | malformed, id -1 (bad id) |  |
| id with a leading zero | malformed, id -1 (bad id) |  |
| negative id | malformed, id -1 (bad id) |  |
| truncated in the payload | malformed, id 7 (SyntaxError: Unterminated string in JSON at position 106 (line 1 column 107)) | webkitgtk: malformed, id 7 (SyntaxError: JSON Parse error: Unterminated string) |
| truncated in the header | malformed, id -1 (bad id) |  |
| two frames concatenated | malformed, id 7 (SyntaxError: Unexpected non-whitespace character after JSON at position 126 (line 1 column 127)) | webkitgtk: malformed, id 7 (SyntaxError: JSON Parse error: Unable to parse JSON string) |
| items that are not an array | malformed, id 7 (missing items) |  |
| result without a payload | malformed, id 7 (missing result) |  |
| leading BOM | malformed, id -1 (kind is not one letter) |  |
| unknown kind from a newer host | unrecognized, id 7 |  |
| newer host adds a header field to i | malformed, id 7 (SyntaxError: Unexpected non-whitespace character after JSON at position 2 (line 1 column 3)) | webkitgtk: malformed, id 7 (SyntaxError: JSON Parse error: Unable to parse JSON string) |
| double space | malformed, id -1 (bad id) |  |
| unknown kind without an id | unrecognized, id 2 |  |

## B

| Case | Outcome | Differs on |
|---|---|---|
| valid result | result, id 7, value {"id":"7","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026 |  |
| escaped newline, U+2028, U+2029 in a string | result, id 7, value "a\nb c d" |  |
| raw U+2028 and U+2029 in a string | result, id 7, value "a b c" |  |
| lone surrogate escape in a string | result, id 7, value "a\ud800b" |  |
| raw newline as JSON whitespace | result, id 7, value 1 |  |
| id with 10,000 digits | result, id null, value {"id":"7","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026 |  |
| id 2^53 + 1 | result, id 9007199254740992, value {"id":"7","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026 |  |
| id with a leading zero | malformed, id -1 (SyntaxError: Unexpected number in JSON at position 23 (line 1 column 24)) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Expected '}') |
| negative id | result, id -7, value {"id":"7","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026 |  |
| truncated in the payload | malformed, id -1 (SyntaxError: Unterminated string in JSON at position 129 (line 1 column 130)) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Unterminated string) |
| truncated in the header | malformed, id -1 (SyntaxError: Unterminated string in JSON at position 12 (line 1 column 13)) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Unterminated string) |
| two frames concatenated | malformed, id -1 (SyntaxError: Unexpected non-whitespace character after JSON at position 149 (line 1 column 150)) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Unable to parse JSON string) |
| items that are not an array | malformed, id 7 (missing items) |  |
| result without a payload | malformed, id -1 (SyntaxError: Expected double-quoted property name in JSON at position 24 (line 1 column 25)) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Property name must be a string literal) |
| leading BOM | malformed, id -1 (SyntaxError: Unexpected token '﻿', "﻿{"type":""... is not valid JSON) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Unrecognized token '﻿') |
| unknown kind from a newer host | unrecognized, id 7 |  |
| newer host adds a header field to items | items, id 7, 1 items |  |
| duplicate id key | result, id 8 |  |
| type key last | result, id 7, value 1 |  |

## C

| Case | Outcome | Differs on |
|---|---|---|
| valid result | result, id 7, value {"id":"7","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026 |  |
| escaped newline, U+2028, U+2029 in a string | result, id 7, value "a\nb c d" |  |
| raw U+2028 and U+2029 in a string | result, id 7, value "a b c" |  |
| lone surrogate escape in a string | result, id 7, value "a\ud800b" |  |
| raw newline as JSON whitespace | result, id 7, value 1 |  |
| id with 10,000 digits | result, id null, value {"id":"7","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026 |  |
| id 2^53 + 1 | result, id 9007199254740992, value {"id":"7","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026 |  |
| id with a leading zero | malformed, id -1 (SyntaxError: Unexpected number in JSON at position 23 (line 1 column 24)) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Expected '}') |
| negative id | result, id -7, value {"id":"7","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026 |  |
| truncated in the payload | malformed, id 7 (SyntaxError: Unterminated string in JSON at position 106 (line 1 column 107)) | webkitgtk: malformed, id 7 (SyntaxError: JSON Parse error: Unterminated string) |
| truncated in the header | malformed, id -1 (SyntaxError: Unterminated string in JSON at position 12 (line 1 column 13)) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Unterminated string) |
| two frames concatenated | malformed, id 7 (SyntaxError: Unexpected non-whitespace character after JSON at position 126 (line 1 column 127)) | webkitgtk: malformed, id 7 (SyntaxError: JSON Parse error: Unable to parse JSON string) |
| items that are not an array | malformed, id 7 (missing items) |  |
| result without a payload | malformed, id 7 (missing payload) |  |
| leading BOM | malformed, id -1 (SyntaxError: Unexpected token '﻿', "﻿{"type":""... is not valid JSON) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Unrecognized token '﻿') |
| unknown kind from a newer host | unrecognized, id 7 |  |
| newer host adds a header field to items | items, id 7, 1 items |  |
| duplicate id key | result, id 8 |  |
| raw TAB inside a header string | malformed, id -1 (SyntaxError: Unterminated string in JSON at position 30 (line 1 column 31)) | webkitgtk: malformed, id -1 (SyntaxError: JSON Parse error: Unterminated string) |

## Page encoders with awkward input

| Encoding | Case | Output |
|---|---|---|
| A | loneSurrogateArg | `"q 1 bench echo 3d1faabdfca06185 - {\"text\":\"a\\ud800b\"}"` |
| A | loneSurrogateTracestate | `threw URIError: URI malformed` |
| A | spaceInTracestate | `q 1 bench echo 3d1faabdfca06185 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01;k%3Da%20b` |
| A | newlineAndU2028Arg | `q 1 bench echo 3d1faabdfca06185 - {"text":"a\nb c d"}` |
| A | bigint | `threw TypeError: Do not know how to serialize a BigInt` |
| B | loneSurrogateArg | `"{\"type\":\"call\",\"id\":1,\"contract\":\"bench\",\"command\":\"echo\",\"fingerprint\":\"3d1faabdfca06185\",\"arg\":{\` |
| B | loneSurrogateTracestate | `{"type":"call","id":1,"contract":"bench","command":"echo","fingerprint":"3d1faabdfca06185","traceparent":"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-0` |
| B | spaceInTracestate | `{"type":"call","id":1,"contract":"bench","command":"echo","fingerprint":"3d1faabdfca06185","traceparent":"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01","tracestate":"k=a b"}` |
| B | newlineAndU2028Arg | `{"type":"call","id":1,"contract":"bench","command":"echo","fingerprint":"3d1faabdfca06185","arg":{"text":"a\nb c d"}}` |
| B | bigint | `threw TypeError: Do not know how to serialize a BigInt` |
| C | loneSurrogateArg | `"{\"type\":\"call\",\"id\":1,\"contract\":\"bench\",\"command\":\"echo\",\"fingerprint\":\"3d1faabdfca06185\"}\t{\"text\` |
| C | loneSurrogateTracestate | `{"type":"call","id":1,"contract":"bench","command":"echo","fingerprint":"3d1faabdfca06185","traceparent":"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-0` |
| C | spaceInTracestate | `{"type":"call","id":1,"contract":"bench","command":"echo","fingerprint":"3d1faabdfca06185","traceparent":"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01","tracestate":"k=a b"}` |
| C | newlineAndU2028Arg | `{"type":"call","id":1,"contract":"bench","command":"echo","fingerprint":"3d1faabdfca06185"}	{"text":"a\nb c d"}` |
| C | bigint | `threw TypeError: Do not know how to serialize a BigInt` |

Engines compared: webview2, loopback, webkitgtk.
