## long
- wrote results/long.json (the values as numbers and as strings)
Reading a number token with TryGetInt64 / TryGetUInt64:
- 9007199254740993: `Int64 "9007199254740993"; UInt64 "9007199254740993"`
- 9223372036854775807: `Int64 "9223372036854775807"; UInt64 "9223372036854775807"`
- 9223372036854775808: `Int64 "false"; UInt64 "9223372036854775808"`
- -0: `Int64 "0"; UInt64 "false"`
- 1e3: `Int64 "false"; UInt64 "false"`
- 1.0: `Int64 "false"; UInt64 "false"`
- 1E+21: `Int64 "false"; UInt64 "false"`
- 9007199254740992.0: `Int64 "false"; UInt64 "false"`
- 18446744073709551615: `Int64 "false"; UInt64 "18446744073709551615"`
Reading a string token: Utf8Parser.TryParse against the strict canonical check:
- "9007199254740993": `Utf8Parser 9007199254740993 (consumed 16/16); long.TryParse(AllowLeadingSign) 9007199254740993; canonical 9007199254740993`
- "-9223372036854775808": `Utf8Parser -9223372036854775808 (consumed 20/20); long.TryParse(AllowLeadingSign) -9223372036854775808; canonical -9223372036854775808`
- "+1": `Utf8Parser 1 (consumed 2/2); long.TryParse(AllowLeadingSign) 1; canonical reject`
- "007": `Utf8Parser 7 (consumed 3/3); long.TryParse(AllowLeadingSign) 7; canonical reject`
- "-0": `Utf8Parser 0 (consumed 2/2); long.TryParse(AllowLeadingSign) 0; canonical reject`
- " 1": `Utf8Parser false; long.TryParse(AllowLeadingSign) false; canonical reject`
- "1 ": `Utf8Parser 1 (consumed 1/2); long.TryParse(AllowLeadingSign) false; canonical reject`
- "1e3": `Utf8Parser 1 (consumed 1/3); long.TryParse(AllowLeadingSign) false; canonical reject`
- "9223372036854775808": `Utf8Parser false; long.TryParse(AllowLeadingSign) false; canonical reject`
- "": `Utf8Parser false; long.TryParse(AllowLeadingSign) false; canonical reject`
C# cost, 1,000,000 random full-range longs (min of 5 runs, Release, JIT):
- write as number: `12.2 ms, 20,378,765 bytes`
- write as string: `18.5 ms, 22,378,765 bytes`
- read number (TryGetInt64): `35.8 ms`
- read string (canonical check + Utf8Parser): `35.6 ms (checksum 52)`

## types
Local time zone: W. Europe Standard Time, offset now 01:00:00
### decimal
- 1.50: `1.50 -> read back 1.50`
- 0.1: `0.1 -> read back 0.1`
- -0.0001: `-0.0001 -> read back -0.0001`
- 922337203685477.5807: `922337203685477.5807 -> read back 922337203685477.5807`
- 12345678901234567890.123456789: `12345678901234567890.123456789 -> read back 12345678901234567890.123456789`
- 79228162514264337593543950335: `79228162514264337593543950335 -> read back 79228162514264337593543950335`
- 0.0000000000000000000000000001: `0.0000000000000000000000000001 -> read back 0.0000000000000000000000000001`
- read number 1e2 as decimal: `100`
- read number 1.500 as decimal (scale kept?): `1.500`
- read 79228162514264337593543950336 (decimal.MaxValue + 1): `"false"`
- read 0.12345678901234567890123456789 (30 digits): `0.1234567890123456789012345679`
- SqlDecimal 38 nines .ToString(): `99999999999999999999999999999999999999`
- SqlDecimal 38 nines .Value (as System.Decimal): `THROWS OverflowException: Conversion overflows.`
- SqlDecimal(38,10) .Value: `THROWS OverflowException: Conversion overflows.`
### DateTime / DateTimeOffset / DateOnly / TimeOnly / TimeSpan
- DateTime Kind=Utc: `"2026-11-14T10:20:30.1234567Z" -> Kind=Utc, ticks equal True, == True`
- DateTime Kind=Local: `"2026-11-14T10:20:30.1234567+01:00" -> Kind=Local, ticks equal True, == True`
- DateTime Kind=Unspecified: `"2026-11-14T10:20:30.1234567" -> Kind=Unspecified, ticks equal True, == True`
- DateTime Utc, whole seconds: `"2026-11-14T10:20:30Z"`
- read "2026-11-14T10:20:30+01:00" as DateTime: `2026-11-14T10:20:30.0000000+01:00 Kind=Local Ticks=639302484300000000`
- read "2026-11-14T10:20:30Z" as DateTime: `2026-11-14T10:20:30.0000000Z Kind=Utc Ticks=639302484300000000`
- read "2026-11-14" as DateTime: `2026-11-14T00:00:00.0000000 Kind=Unspecified Ticks=639302112000000000`
- read "2026-11-14T10:20:30.123Z" (JS toISOString) as DateTime: `2026-11-14T10:20:30.1230000Z Kind=Utc Ticks=639302484301230000`
- read "2026-11-14T10:20:30.12345678Z" (8 fraction digits): `2026-11-14T10:20:30.1234567Z Kind=Utc Ticks=639302484301234567`
- read "2026-11-14 10:20:30" (space): `THROWS FormatException: The JSON value is not in a supported DateTime format.`
- DateTimeOffset +01:00, 100 ns: `"2026-11-14T10:20:30.1234567+01:00" -> offset 01:00:00, ticks equal True`
- DateTimeOffset +00:00: `"2026-11-14T10:20:30.1234567+00:00"`
- read "2026-11-14T10:20:30Z" as DateTimeOffset: `2026-11-14T10:20:30.0000000+00:00 UtcTicks=639302484300000000`
- read "2026-11-14T10:20:30" (no offset) as DateTimeOffset: `2026-11-14T10:20:30.0000000+01:00 UtcTicks=639302448300000000`
- DateOnly: `"2026-11-14"`
- read "2026-11-14T00:00:00" as DateOnly: `THROWS JsonException: The JSON value could not be converted to System.DateOnly. Path: $ | LineNumber: 0 | BytePositionInLine: 21.`
- TimeOnly 372301234567 ticks: `"10:20:30.1234567"`
- TimeOnly 372000000000 ticks: `"10:20:00"`
- TimeOnly 863999999999 ticks: `"23:59:59.9999999"`
- TimeSpan 937845000000 ticks: `"1.02:03:04.5000000"`
- TimeSpan -10000000 ticks: `"-00:00:01"`
- TimeSpan 1 ticks: `"00:00:00.0000001"`
- TimeSpan 9223372036854775807 ticks: `"10675199.02:48:05.4775807"`
- TimeSpan 1296000000000 ticks: `"1.12:00:00"`
- read "PT1H" (ISO 8601 duration) as TimeSpan: `THROWS JsonException: The JSON value could not be converted to System.TimeSpan. Path: $ | LineNumber: 0 | BytePositionInLine: 6.`
### Guid, byte[], char
- Guid: `"3f2504e0-4f89-11d3-9a0c-0305e82c3301"`
- TryGetGuid "3F2504E0-4F89-11D3-9A0C-0305E82C3301": `"3f2504e0-4f89-11d3-9a0c-0305e82c3301"`
- TryGetGuid "{3f2504e0-4f89-11d3-9a0c-0305e82c3301}": `"false"`
- TryGetGuid "3f2504e04f8911d39a0c0305e82c3301": `"false"`
- TryGetGuid " 3f2504e0-4f89-11d3-9a0c-0305e82c3301": `"false"`
- byte[] rowversion 0x00000000000007D1: `"AAAAAAAAB9E="`
- byte[0]: `""`
- TryGetBytesFromBase64 "AQID": `"bytes[010203]"`
- TryGetBytesFromBase64 "AQI": `"false"`
- TryGetBytesFromBase64 "AQI=": `"bytes[0102]"`
- TryGetBytesFromBase64 "AQ\u003d\u003d": `"bytes[01]"`
- TryGetBytesFromBase64 "-_8=": `"false"`
- TryGetBytesFromBase64 "+/8=": `"bytes[FBFF]"`
- TryGetBytesFromBase64 "AQ ID": `"bytes[010203]"`
- TryGetBytesFromBase64 "AQ\nID": `"bytes[010203]"`
- char U+0061: `""a""`
- char U+0022: `""\u0022""`
- char U+D800: `""\uFFFD""`
- read "ab" as char: `THROWS JsonException: The JSON value could not be converted to System.Char. Path: $ | LineNumber: 0 | BytePositionInLine: 4.`
### double / float
- double NaN WriteNumberValue: `THROWS ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work when using 'JsonSerializer', consider specifying 'JsonNumberHandling.AllowNamedFloatingPointLiterals' (see https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonnumberhandling).`
- double NaN STJ named literals: `""NaN""`
- double Infinity WriteNumberValue: `THROWS ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work when using 'JsonSerializer', consider specifying 'JsonNumberHandling.AllowNamedFloatingPointLiterals' (see https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonnumberhandling).`
- double Infinity STJ named literals: `""Infinity""`
- double -Infinity WriteNumberValue: `THROWS ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work when using 'JsonSerializer', consider specifying 'JsonNumberHandling.AllowNamedFloatingPointLiterals' (see https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonnumberhandling).`
- double -Infinity STJ named literals: `""-Infinity""`
- double -0 WriteNumberValue: `-0`
- double -0 STJ named literals: `"-0"`
- double 0.1 WriteNumberValue: `0.1`
- double 0.1 STJ named literals: `"0.1"`
- double 1E+21 WriteNumberValue: `1E+21`
- double 1E+21 STJ named literals: `"1E+21"`
- double 5E-324 WriteNumberValue: `5E-324`
- double 5E-324 STJ named literals: `"5E-324"`
- double 1.7976931348623157E+308 WriteNumberValue: `1.7976931348623157E+308`
- double 1.7976931348623157E+308 STJ named literals: `"1.7976931348623157E+308"`
- read "NaN" with named literals: `NaN`
- read NaN token: `THROWS JsonReaderException: 'N' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0.`
- float 0.1: `0.1; as double 0.10000000149011612`
- float 3.4028235E+38: `3.4028235E+38; as double 3.4028234663852886E+38`
- float 16777216: `16777216; as double 16777216`
- float 1.0000001: `1.0000001; as double 1.0000001192092896`
- read 3.5e38 as float (above float.MaxValue): `"Infinity"`
### enums
- Severity 1 default: `1`
- Severity 1 string converter: `""Warning""`
- Severity 42 default: `42`
- Severity 42 string converter: `"42"`
- Access 3 string converter: `""Read, Write""`
- Access 8 string converter: `"8"`
- Access 0 string converter: `""None""`
- read "warning" (camelCase): `Warning`
- read "Warning": `Warning`
- read 42 with string converter: `42`
- read "Bogus": `THROWS JsonException: The JSON value could not be converted to WireProbe.Severity. Path: $ | LineNumber: 0 | BytePositionInLine: 7.`
- wrote results/types.json

## surrogates
### Utf8JsonWriter with each encoder
- lone-high WriteStringValue(string), null (default): `"a\uFFFDb"`
- lone-high WriteStringValue(string), JavaScriptEncoder.Default: `"a\uFFFDb"`
- lone-high WriteStringValue(string), UnsafeRelaxedJsonEscaping: `"a\uFFFDb"`
- lone-high WriteStringValue(string), Create(UnicodeRanges.All): `"a\uFFFDb"`
- lone-low WriteStringValue(string), null (default): `"a\uFFFDb"`
- lone-low WriteStringValue(string), JavaScriptEncoder.Default: `"a\uFFFDb"`
- lone-low WriteStringValue(string), UnsafeRelaxedJsonEscaping: `"a\uFFFDb"`
- lone-low WriteStringValue(string), Create(UnicodeRanges.All): `"a\uFFFDb"`
- reversed WriteStringValue(string), null (default): `"\uFFFD\uFFFD"`
- reversed WriteStringValue(string), JavaScriptEncoder.Default: `"\uFFFD\uFFFD"`
- reversed WriteStringValue(string), UnsafeRelaxedJsonEscaping: `"\uFFFD\uFFFD"`
- reversed WriteStringValue(string), Create(UnicodeRanges.All): `"\uFFFD\uFFFD"`
- trailing-high WriteStringValue(string), null (default): `"abc\uFFFD"`
- trailing-high WriteStringValue(string), JavaScriptEncoder.Default: `"abc\uFFFD"`
- trailing-high WriteStringValue(string), UnsafeRelaxedJsonEscaping: `"abc\uFFFD"`
- trailing-high WriteStringValue(string), Create(UnicodeRanges.All): `"abc\uFFFD"`
- WriteStringValue(ReadOnlySpan<char>): `"a\uFFFDb"`
- WriteString(property name with lone surrogate): `{"a\uFFFDb":"v"}`
- JsonEncodedText.Encode: `THROWS ArgumentException: Cannot transcode invalid UTF-16 string to UTF-8 JSON text.`
- SkipValidation = true: `"a\uFFFDb"`
- STJ JsonSerializer (string): `""a\uFFFDb""`
- Encoding.UTF8.GetBytes: `a\xEF\xBF\xBDb`
- UTF8Encoding(throwOnInvalidBytes: true).GetBytes: `THROWS EncoderFallbackException: Unable to translate Unicode character \\uD800 at index 1 to specified code page.`
- WriteStringValue(UTF-8 bytes with CESU ED A0 80): `"a\uFFFD\uFFFD\uFFFDb"`
- WriteStringValue(UTF-8 bytes with FF): `"a\uFFFD"`
- WriteRawValue("\ud800") with validation: `"\ud800"`
### Utf8JsonReader and friends
- "\ud800" Read(): `"String, ValueIsEscaped=True"`
- "\ud800" GetString(): `THROWS InvalidOperationException: Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.`
- "\ud800" CopyString(char[]): `THROWS InvalidOperationException: Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.`
- "\ud800" JsonDocument GetString(): `THROWS InvalidOperationException: Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.`
- "\ud800" JsonSerializer (string): `THROWS JsonException: The JSON value could not be converted to System.String. Path: $ | LineNumber: 0 | BytePositionInLine: 8.`
- "\ud800" lossless prototype: `"{U+D800}"`
- "\ud800" replacing prototype: `"{U+FFFD}"`
- "a\udc00b" Read(): `"String, ValueIsEscaped=True"`
- "a\udc00b" GetString(): `THROWS InvalidOperationException: Cannot read invalid UTF-16 JSON text as string. Invalid surrogate value: '0xDC00'.`
- "a\udc00b" CopyString(char[]): `THROWS InvalidOperationException: Cannot read invalid UTF-16 JSON text as string. Invalid surrogate value: '0xDC00'.`
- "a\udc00b" JsonDocument GetString(): `THROWS InvalidOperationException: Cannot read invalid UTF-16 JSON text as string. Invalid surrogate value: '0xDC00'.`
- "a\udc00b" JsonSerializer (string): `THROWS JsonException: The JSON value could not be converted to System.String. Path: $ | LineNumber: 0 | BytePositionInLine: 10.`
- "a\udc00b" lossless prototype: `"a{U+DC00}b"`
- "a\udc00b" replacing prototype: `"a{U+FFFD}b"`
- "\udbff\udfff" (valid pair) Read(): `"String, ValueIsEscaped=True"`
- "\udbff\udfff" (valid pair) GetString(): `"{U+DBFF}{U+DFFF}"`
- "\udbff\udfff" (valid pair) CopyString(char[]): `"{U+DBFF}{U+DFFF}"`
- "\udbff\udfff" (valid pair) JsonDocument GetString(): `"{U+DBFF}{U+DFFF}"`
- "\udbff\udfff" (valid pair) JsonSerializer (string): `"{U+DBFF}{U+DFFF}"`
- "\udbff\udfff" (valid pair) lossless prototype: `"{U+DBFF}{U+DFFF}"`
- "\udbff\udfff" (valid pair) replacing prototype: `"{U+DBFF}{U+DFFF}"`
- raw bytes ED A0 80 (CESU) Read(): `"String, ValueIsEscaped=False"`
- raw bytes ED A0 80 (CESU) GetString(): `THROWS InvalidOperationException: Cannot transcode invalid UTF-8 JSON text to UTF-16 string.`
- raw bytes ED A0 80 (CESU) CopyString(char[]): `THROWS InvalidOperationException: Cannot transcode invalid UTF-8 JSON text to UTF-16 string.`
- raw bytes ED A0 80 (CESU) JsonDocument GetString(): `THROWS InvalidOperationException: Cannot transcode invalid UTF-8 JSON text to UTF-16 string.`
- raw bytes ED A0 80 (CESU) JsonSerializer (string): `THROWS JsonException: The JSON value could not be converted to System.String. Path: $ | LineNumber: 0 | BytePositionInLine: 5.`
- raw bytes ED A0 80 (CESU) lossless prototype: `THROWS JsonException: Invalid UTF-8 in the string at byte 0.`
- raw bytes ED A0 80 (CESU) replacing prototype: `THROWS JsonException: Invalid UTF-8 in the string at byte 0.`
- raw bytes EF BF BD (U+FFFD) Read(): `"String, ValueIsEscaped=False"`
- raw bytes EF BF BD (U+FFFD) GetString(): `"{U+FFFD}"`
- raw bytes EF BF BD (U+FFFD) CopyString(char[]): `"{U+FFFD}"`
- raw bytes EF BF BD (U+FFFD) JsonDocument GetString(): `"{U+FFFD}"`
- raw bytes EF BF BD (U+FFFD) JsonSerializer (string): `"{U+FFFD}"`
- raw bytes EF BF BD (U+FFFD) lossless prototype: `"{U+FFFD}"`
- raw bytes EF BF BD (U+FFFD) replacing prototype: `"{U+FFFD}"`
### Lossless prototype, C# -> JSON
- lone-high: `standard "a\uFFFDb"; lossless "a\ud800b"; read back equal True; identical to standard when well-formed False`
- lone-low: `standard "a\uFFFDb"; lossless "a\udc00b"; read back equal True; identical to standard when well-formed False`
- reversed: `standard "\uFFFD\uFFFD"; lossless "\udc00\ud800"; read back equal True; identical to standard when well-formed False`
- trailing-high: `standard "abc\uFFFD"; lossless "abc\ud83d"; read back equal True; identical to standard when well-formed False`
- valid-pair: `standard "\uD83D\uDE00"; lossless "\uD83D\uDE00"; read back equal True; identical to standard when well-formed True`
- plain: `standard "plain \u0022text\u0022 \u00E9"; lossless "plain \u0022text\u0022 \u00E9"; read back equal True; identical to standard when well-formed True`
- wrote results/surrogates.json (node js/surrogates.mjs answers in results/surrogates-from-js.json)
### Cost, 1,000,000 well-formed strings (a quarter non-ASCII, a quarter escaped; min of 5 runs)
- write: WriteStringValue: `22.8 ms`
- write: lossless (range scan first): `29.8 ms, output byte-identical True`
- read: GetString: `55.8 ms`
- read: lossless (GetString, fallback only when it throws): `59.8 ms`
- read: replacing (same, U+FFFD on the fallback): `60.8 ms (0)`
- read: 10,000 strings that each take the fallback (exception + unescape): `25.2 ms, 2.5 us per string`

## null
### Reader on top-level payloads
- Utf8JsonReader "null": `"Null"`
- STJ Deserialize<string> "null": `null`
- Utf8JsonReader "": `THROWS JsonReaderException: The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true. LineNumber: 0 | BytePositionInLine: 0.`
- STJ Deserialize<string> "": `THROWS JsonException: The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true. Path: $ | LineNumber: 0 | BytePositionInLine: 0.`
- Utf8JsonReader "   ": `THROWS JsonReaderException: The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true. LineNumber: 0 | BytePositionInLine: 3.`
- STJ Deserialize<string> "   ": `THROWS JsonException: The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true. Path: $ | LineNumber: 0 | BytePositionInLine: 3.`
- Utf8JsonReader "{}": `"StartObject"`
- STJ Deserialize<string> "{}": `THROWS JsonException: The JSON value could not be converted to System.String. Path: $ | LineNumber: 0 | BytePositionInLine: 1.`
### Envelope prototype (what the generated dispatch would write)
- voidOk: `{"id":7,"ok":true}`
- nullableNull: `{"id":7,"ok":true,"value":null}`
- nullableValue: `{"id":7,"ok":true,"value":"x"}`
- resultNullableNull: `{"id":7,"ok":true,"value":null}`
- resultError: `{"id":7,"ok":false,"error":{"code":"notFound"}}`
- wrote results/null.json

## duplicates
Input `{"name":"a","qty":1,"price":1,"active":true,"note":null,"version":1,"qty":2}`
- Utf8JsonReader tokens: `"name,qty,price,active,note,version,qty"`
- JsonDocument default, GetProperty("qty"): `"2"`
- JsonDocument AllowDuplicateProperties=false: `THROWS JsonException: Duplicate property 'qty' encountered during deserialization.`
- JsonNode.Parse default, ["qty"]: `THROWS ArgumentException: An item with the same key has already been added. Key: qty (Parameter 'key')`
- JsonSerializer default: `2`
- JsonSerializer AllowDuplicateProperties=false: `THROWS JsonException: Duplicate property 'qty' encountered during deserialization of type 'WireProbe.Row'.`
- generated-style reader, last wins: `2`
- generated-style reader, reject: `THROWS JsonException: Duplicate property at byte 68.`
Input `{"name":"a","qty":1,"price":1,"active":true,"note":null,"version":1,"q\u0074y":2}`
- Utf8JsonReader tokens: `"name,qty,price,active,note,version,qty"`
- JsonDocument default, GetProperty("qty"): `"2"`
- JsonDocument AllowDuplicateProperties=false: `THROWS JsonException: Duplicate property 'qty' encountered during deserialization.`
- JsonNode.Parse default, ["qty"]: `THROWS ArgumentException: An item with the same key has already been added. Key: qty (Parameter 'key')`
- JsonSerializer default: `2`
- JsonSerializer AllowDuplicateProperties=false: `THROWS JsonException: Duplicate property 'qty' encountered during deserialization of type 'WireProbe.Row'.`
- generated-style reader, last wins: `2`
- generated-style reader, reject: `THROWS JsonException: Duplicate property at byte 68.`
Input `{"name":"a","qty":1,"price":1,"active":true,"note":null,"version":1,"Qty":2}`
- Utf8JsonReader tokens: `"name,qty,price,active,note,version,Qty"`
- JsonDocument default, GetProperty("qty"): `"1"`
- JsonDocument AllowDuplicateProperties=false: `"1"`
- JsonNode.Parse default, ["qty"]: `"1"`
- JsonSerializer default: `1`
- JsonSerializer AllowDuplicateProperties=false: `1`
- generated-style reader, last wins: `1`
- generated-style reader, reject: `1`
Input `{"name":"a","qty":1,"price":1,"active":true,"note":null,"version":1,"extra":1,"extra":2}`
- Utf8JsonReader tokens: `"name,qty,price,active,note,version,extra,extra"`
- JsonDocument default, GetProperty("qty"): `"1"`
- JsonDocument AllowDuplicateProperties=false: `THROWS JsonException: Duplicate property 'extra' encountered during deserialization.`
- JsonNode.Parse default, ["qty"]: `THROWS ArgumentException: An item with the same key has already been added. Key: extra (Parameter 'key')`
- JsonSerializer default: `1`
- JsonSerializer AllowDuplicateProperties=false: `1`
- generated-style reader, last wins: `1`
- generated-style reader, reject: `1`
### Cost, 1,000,000 parses of a 76-byte, 6-property object (min of 5 runs)
- generated-style, last wins: `142 ms`
- generated-style, reject (bit mask): `141 ms`
- STJ source-generated, default: `275.9 ms`
- STJ source-generated, AllowDuplicateProperties=false: `308.1 ms (0)`

## writer-errors
### What each failure throws (SkipValidation = true, MaxDepth = 64, as the pooled writer)
- ok: `"fine"`
- ok (SkipValidation = false): `"fine"`
- NaN: `THROWS ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work when using 'JsonSerializer', consider specifying 'JsonNumberHandling.AllowNamedFloatingPointLiterals' (see https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonnumberhandling).`
- NaN (SkipValidation = false): `THROWS ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work when using 'JsonSerializer', consider specifying 'JsonNumberHandling.AllowNamedFloatingPointLiterals' (see https://learn.microsoft.com/dotnet/api/system.text.json.serialization.jsonnumberhandling).`
- depth 65: `THROWS InvalidOperationException: CurrentDepth (64) is equal to or larger than the maximum allowed depth of 64. Cannot write the next JSON object or array.`
- depth 65 (SkipValidation = false): `THROWS InvalidOperationException: CurrentDepth (64) is equal to or larger than the maximum allowed depth of 64. Cannot write the next JSON object or array.`
- lone surrogate: `"a\uFFFD"`
- lone surrogate (SkipValidation = false): `"a\uFFFD"`
- user getter throws: `THROWS InvalidOperationException: getter failed`
- user getter throws (SkipValidation = false): `THROWS InvalidOperationException: getter failed`
- invalid UTF-8 bytes: `"\uFFFD"`
- invalid UTF-8 bytes (SkipValidation = false): `"\uFFFD"`
### Reusing a pooled writer after an exception
- NaN: `ArgumentException; BytesPending=9, BytesCommitted=0, CurrentDepth=1, buffer=0; after Reset: {"ok":true}`
- depth 65: `InvalidOperationException; BytesPending=72, BytesCommitted=0, CurrentDepth=64, buffer=0; after Reset: {"ok":true}`
- lone surrogate: `no exception; after Reset: {"ok":true}`
- user getter throws: `InvalidOperationException; BytesPending=9, BytesCommitted=0, CurrentDepth=1, buffer=0; after Reset: {"ok":true}`
- invalid UTF-8 bytes: `no exception; after Reset: {"ok":true}`
### Buffered unary dispatch prototype (the command has already run)
- ok: `{"id":7,"ok":true,"value":"fine"}`
- NaN: `{"id":7,"ok":false,"failure":{"kind":"response-serialization","execution":"completed","detail":"ArgumentException"}}`
- depth 65: `{"id":7,"ok":false,"failure":{"kind":"response-serialization","execution":"completed","detail":"InvalidOperationException"}}`
- lone surrogate: `{"id":7,"ok":true,"value":"a\uFFFD"}`
- user getter throws: `{"id":7,"ok":false,"failure":{"kind":"response-serialization","execution":"completed","detail":"InvalidOperationException"}}`
- invalid UTF-8 bytes: `{"id":7,"ok":true,"value":"\uFFFD"}`
### Stream prototype: item 3 of 5 fails to serialize
- sent: `{"id":9,"type":"item","value":1}`
- sent: `{"id":9,"type":"item","value":2}`
- sent: `{"id":9,"type":"end","ok":false,"failure":{"kind":"response-serialization","execution":"stopped-after-items","itemsSent":2,"detail":"ArgumentException"}}`
- producer: `enumerator finally ran`
### Unbuffered unary (writer flushes to the transport every 16 KB): failure at item 50,000
- exception: `ArgumentException`
- bytes already sent: `278,560, ends with `,48272,48273``

## surrogates-back
- from JS lone-high: `bytes a\ud800b; GetString THROWS InvalidOperationException: Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.; lossless equal to C# original True`
- from JS lone-low: `bytes a\udc00b; GetString THROWS InvalidOperationException: Cannot read invalid UTF-16 JSON text as string. Invalid surrogate value: '0xDC00'.; lossless equal to C# original True`
- from JS reversed: `bytes \udc00\ud800; GetString THROWS InvalidOperationException: Cannot read invalid UTF-16 JSON text as string. Invalid surrogate value: '0xDC00'.; lossless equal to C# original True`
- from JS trailing-high: `bytes abc\ud83d; GetString THROWS InvalidOperationException: Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.; lossless equal to C# original True`
- from JS valid-pair: `bytes \xF0\x9F\x98\x80; GetString "{U+D83D}{U+DE00}"; lossless equal to C# original True`
- from JS plain: `bytes plain \"text\" \xC3\xA9; GetString "plain "text" {U+00E9}"; lossless equal to C# original True`

