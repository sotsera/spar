# WebView2 transport probes

What crosses `postMessage` / `PostWebMessageAsString` / `PostWebMessageAsJson` unchanged.

## Page → host

| Page posts | Host sees |
|---|---|
| pageLoneSurrogateString | #seen tryString=0x80070057 string=null json=16 units ["#seen a\ud800b"] |
| pageSeparatorsString | #seen tryString=0x00000000 string=11 units [#seen a<2028>b<000A>c] json=21 units ["#seen a<2028>b\nc\u0000d"] |
| pageObjectWithLoneSurrogate | #seen tryString=0x80070057 string=null json=34 units [{"probe":"seen","text":"a\ud800b"}] |
| pageObjectWithBigint | page threw TypeError: Failed to execute 'postMessage' on 'EmbeddedBrowserWebView': Do not know how to serialize a BigInt |
| pageObjectUndefinedAndNaN | #seen tryString=0x80070057 string=null json=34 units [{"probe":"seen","n":null,"i":null}] |
| pageObjectWithDate | #seen tryString=0x80070057 string=null json=47 units [{"probe":"seen","d":"1970-01-01T00:00:00.000Z"}] |

## Host → page

| Host posts | Page receives |
|---|---|
| string with a lone surrogate | string (3 units): `a<fffd>b` |
| string with U+2028, newline, NUL | string (5 units): `a<2028>b<a>c` |
| JSON with an escaped lone surrogate | string (35 units): `#threw 0x80070057 ArgumentException` |
| JSON with a raw lone surrogate | object: `{"t":"a�b"}` |
| JSON with a 20-digit number | object: `{"n":12345678901234567000}` |
| JSON with __proto__ | object: `{"__proto__":{"x":1}}` (own `__proto__` key) |
| JSON with a duplicate key | object: `{"id":8}` |
| not JSON | string (35 units): `#threw 0x80070057 ArgumentException` |
| two JSON values | string (35 units): `#threw 0x80070057 ArgumentException` |
| JSON with trailing whitespace and newline | object: `{"a":1}` |
