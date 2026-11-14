# Fuzz run `baseline`

- seed 1; cases requested 600,000; cases run 600,000; batch 4,000; workers 6
- fuzzing time 181.1 s (budget 330 s); total with minimisation 184.1 s
- runtime: .NET 11.0.0 (JIT)

Comparisons: `A~B` original generator vs System.Text.Json (bench options); `F~B` fixed generator vs B; `A~S`/`F~S` against the strict reference S. Classes: EQ both accept, equal; EQ-src equal to each other but not to the source value; NE both accept, values differ; RJ both reject; XR-YA left rejects, right accepts; XA-YR left accepts, right rejects; X-nonjson left throws something other than JsonException.

## Case counts

| Comparison | Stream | EQ | NE | RJ | XR-YA | XA-YR | X-nonjson | PASS | PASS-both-throw | FAIL | total |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A~B | Mutant | 58,994 | 210 | 239,160 | 6,286 | 23,932 | 31,418 | 0 | 0 | 0 | 360,000 |
| A~B | Valid | 120,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 120,000 |
| A~S | Mutant | 70,033 | 0 | 239,865 | 5,581 | 13,103 | 31,418 | 0 | 0 | 0 | 360,000 |
| A~S | Valid | 120,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 120,000 |
| F~B | Mutant | 58,994 | 210 | 239,160 | 6,286 | 23,932 | 31,418 | 0 | 0 | 0 | 360,000 |
| F~B | Valid | 120,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 120,000 |
| F~S | Mutant | 70,033 | 0 | 239,865 | 5,581 | 13,103 | 31,418 | 0 | 0 | 0 | 360,000 |
| F~S | Valid | 120,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 120,000 |
| JS | Value | 0 | 0 | 0 | 0 | 0 | 0 | 49,261 | 0 | 62,722 | 111,983 |
| W | Value | 0 | 0 | 0 | 0 | 0 | 0 | 111,232 | 8,017 | 751 | 120,000 |

## Incidents (crash or hang)

None.

## Representatives

Key: left outcome | right outcome (normalised message). The example is the shortest case in the group, minimised with ddmin while class and keys stay the same.

### A~B NE: 210 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 210 | Note | x=ok \| y=ok | `{"text":"","tags":[],"parent":null,"score":8,"pinned":true,"versIon":8}` (from 96 B) | bit-flip |

### A~B XR-YA: 6,286 cases in 19 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 1,936 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` (from 4 B) | none root-0 |
| 1,266 | Note | x=JsonException: Expected a string but found Null. \| y=ok | `{"pinned":true,"score":1,"tags":[null],"text":"","parent":null,"version":7}` (from 96 B) | null-out |
| 1,206 | Note[] | x=JsonException: Expected an array but found Null. \| y=ok | `null` (from 4 B) | null-out root-0 |
| 928 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` (from 4 B) | root-0 null-out none |
| 241 | Note | x=JsonException: Note requires 'score'. \| y=ok | `{"SCORE":2,"pinned":false,"tags":[],"text":"","parent":null,"version":null}` (from 90 B) | string-text rename:upper |
| 233 | Note | x=JsonException: Note requires 'pinned'. \| y=ok | `{"text":"","tags":[],"parent":null,"score":7,"Pinned":true,"version":null}` (from 96 B) | rename:pascal whitespace-valid |
| 205 | Note | x=JsonException: Note requires 'tags'. \| y=ok | `{"text":"","tAgs":[],"parent":null,"score":4,"pinned":false,"version":5}` (from 100 B) | none bit-flip |
| 200 | Note | x=JsonException: Note requires 'text'. \| y=ok | `{"score":2,"version":9,"parent":null,"tags":[],"pinned":true,"TEXT":"\xF0\xBC\x84\xB3"}` (from 88 B) | none rename:upper reorder |
| 12 | Wide | x=JsonException: Wide requires 'ratio'. \| y=ok | `{"name":"\xEF\xBF\xBF\xE6\x86\x8C","children":[], "maybe":null,"stamps" :[],"nick":null,"limit"\x0A:7,"head":{\x0A"pinned"\x0D:true,"text": "\xE0\xB2\x9EEh\xF2\x86… (430 bytes)` (from 1,068 B) | rename:pascal |
| 9 | Wide | x=JsonException: Wide requires 'ints'. \| y=ok | `{"name":"","nick":"\xF3\x9F\xA9\x81\xF1\xB7\x9B\x96\xF1\xBE\xB0\x8E\xEC\x90\xAA","count":2,"limit":3,"id":4,"stamp":2,"ratio":5,"weight":null,"on":false,"maybe"… (409 bytes)` (from 721 B) | rename:upper |
| 9 | Wide | x=JsonException: Wide requires 'head'. \| y=ok | `{"name":"","nick":"","count":5,"limit":null,"id":2,"stamp":null,"ratio": 57,"weight" :4,"on":true , "maybe":false ,"words":[ ">\ud800\uDC00A","\uDB40\udf69\ud83… (484 bytes)` (from 985 B) | rename:pascal |
| 8 | Wide | x=JsonException: Wide requires 'count'. \| y=ok | `{"\u006F\u006E":false,"\u006D\u0061\u0074\u0072\u0069\u0078":[[],[636]],"\u0069\u0074\u0065\u006d\u0073":[{"\u0074\u0065\u0078\u0074":"\u0027\ud931\udfc047\u006… (1,455 bytes)` (from 1,972 B) | rename:upper whitespace-valid |
| 8 | Wide | x=JsonException: Wide requires 'stamps'. \| y=ok | ` {"name" : "\xF0\x9F\xA7\xBEd\xC2\xB3'p" , "nick":null, "count":80, "limit" :-1 , "id" :8, "stamp":17, "ratio":7 , "weight":4, "on" : false , "maybe" : false ,"… (544 bytes)` (from 1,142 B) | rename:upper |
| 7 | Wide | x=JsonException: Wide requires 'items'. \| y=ok | `{"iteMs"  :\x0D  [{"parent": \x0D\x0Anull,"ven"\x09:null,"pinned"\x0A\x0A\x0D:true\x0D\x0A\x0D\x0D,"tags":["\u00b0100",""],"text": "E"\x0D\x0A,"version"\x0A :75… (819 bytes)` (from 1,653 B) | bit-flip |

### A~B XA-YR: 23,932 cases in 35 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 12,492 | int | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `0]` (from 2 B) | none deep-wrap:array:1 byte-delete |
| 7,370 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'. | `{"text":"","tags":[],"score":8,"pinned":true,"version":1}` (from 61 B) | omit-null-property none none |
| 2,738 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'version'. | `{"text":"","tags":[],"parent":null,"score":0,"pinned":false}` (from 63 B) | drop-property |
| 211 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'?'version'. | `{"text":"\xF0\x90\x80\x80","tags":[],"score":2,"pinned":true}` (from 56 B) | none drop-property drop-property |
| 179 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'nick'. | `{"name":"","count":6,"limit":8,"id":9,"stamp":null,"ratio":6,"weight":6,"on":false,"maybe":false,"words":[],"ints":[],"stamps":[],"matrix":[],"labels":["\xF0\xB1… (303 bytes)` (from 496 B) | number-text type-swap omit-null-property |
| 148 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'limit'. | `{"name":"\xF2\xBE\x89\xA2","head":{"pinned":true,"parent":null,"score":5\x09,\x0A"text":"","version" :-6,"tags":["3t","\xF4\x87\x80\xAE"\x0D]},"nick":null,\x0A"… (327 bytes)` (from 609 B) | bad-utf8-name |
| 148 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'children'. | `{"":[],"name":"","nick":"","count":3,"limit":3,"":"","id":9,"stamp":8,"ratio":5,"weight":4,"on":true,"maybe": false ,"words": [],"ints" : [] , "stamps" :[ ] ,"m… (352 bytes)` (from 844 B) | unknown-property unknown-property reorder bad-utf8-name |
| 144 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'weight'. | `{"name":"","nick":"","count":7,"limit":2,"id":2,"stamp":5,"ratio":9,"on":true,"maybe":true,"words":["\xF0\xB7\xA9\x92\xF4\x8F\xBF\xBD\xC9\x9E\xEA\xA4\xA9\xF0\xB0… (320 bytes)` (from 633 B) | omit-null-property |
| 137 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'stamp'. | `{"nick":"\xEA\xAF\xBF","head":{"score":3,"pinned":true,"tags":["\xE6\x80\x80"],"parent":null,"text":"","version":1},"labels":null,"words":[""],"ratio":1,"matrix… (280 bytes)` (from 693 B) | omit-null-property array-extra-item |
| 117 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'tail'. | `{"head":{"score":1,"tags":[""],"parent":null,"version":1,"text":"\ud932\udf8f","pinned":true},"limit":5,"maybe":null,"words":[""],"children"\x0A:null,"matrix":[… (320 bytes)` (from 668 B) | drop-property whitespace-valid |
| 109 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'maybe'. | `{"name":"","nick":"","count":9,"limit":null,"id":4,"stamp":7,"ratio":4,"weight":null,"on":false,\x09"words":[],"ints":[\x0D],"stamps"\x09:[null],"matrix" :[ \x09… (368 bytes)` (from 664 B) | none bad-name |
| 105 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'labels'. | `{"name":"\xF3\xB0\x85\x90\xF0\xB1\xBA\xAA\xF4\x8F\xBF\xBF\xF3\xB0\x80\x80","nick":null,"count":7,"limit":7,"id":9,"stamp":7,"ratio":4,"weight":null,"on":true,"m… (317 bytes)` (from 642 B) | string-text string-text bad-utf8-name |
| 4 | Note | x=ok \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"text":"\xF2\x86\x86\xAC\xF0\xA0\x80\x80","tags":[],"":null,"score":6,"pinned":false,"Version":9223372036854775809}` (from 143 B) | number-text rename:pascal |
| 4 | Note | x=ok \| y=JsonException: The JSON value could not be converted to System.String. | `{"text":"","\u0074a\u0067s":[],"Pare\u006et":{"\u0074ext":{"":{"":0}},"":3},"":"","s\u0063\u006f\u0072e":7,"p\u0069n\u006Eed":false,"":9}` (from 367 B) | unknown-property deep-wrap:object:2 rename:pascal byte-delete |

### A~B X-nonjson: 31,418 cases in 159 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 3,748 | string? | x=InvalidOperationException: Cannot transcode invalid UTF-# JSON text to UTF-# string. \| y=JsonException: The JSON value could not be converted to System.String. | `"\xC3"` (from 3 B) | none none bad-utf8-string |
| 2,237 | Note | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"":"","":false,"version":1e1}` (from 70 B) | drop-property drop-property number-text |
| 2,091 | string? | x=InvalidOperationException: Cannot read invalid UTF-# JSON text as string. Invalid surrogate value: '?'. \| y=JsonException: The JSON value could not be converted to System.String. | `"\udc00"` (from 8 B) | string-text |
| 1,727 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":null` (from 47 B) | duplicate-property separator-array-0 rename:pascal truncate |
| 1,643 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"":"","":[],"score":[` (from 61 B) | type-swap type-swap truncate |
| 1,599 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a boolean. \| y=JsonException: The JSON value could not be converted to System.Boolean. | `{"pinned":null` (from 14 B) | omit-null-property null-out truncate |
| 1,530 | string? | x=InvalidOperationException: Cannot read incomplete UTF-# JSON text as string with missing low surrogate. \| y=JsonException: The JSON value could not be converted to System.String. | `"\ud800"` (from 8 B) | none string-text |
| 1,465 | int | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `[` (from 1 B) | none type-swap truncate |
| 1,439 | int | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Int#. | `1.5` (from 3 B) | none none type-swap |
| 1,392 | int | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `null` (from 4 B) | null-out root-6 null-out |
| 1,253 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"version":[` (from 56 B) | duplicate-property omit-null-property truncate |
| 1,206 | int | x=InvalidOperationException: Cannot get the value of a token type 'StartObject' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `{` (from 1 B) | type-swap truncate |
| 1,083 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartObject' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"":[],"":null,"score":{` (from 74 B) | drop-property type-swap |
| 984 | Note | x=InvalidOperationException: Cannot get the value of a token type 'String' as a number. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"version":""` (from 31 B) | drop-property duplicate-property reorder truncate |

### A~B RJ: 239,160 cases in 1,685 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 56,794 | Note[] | x=JsonException: '?' is an invalid start of a value. \| y=JsonException | `]` (from 1 B) | byte-delete |
| 17,417 | Note[] | x=JsonException: '?' is invalid after a value. Expected either '?', '?', or '?'. \| y=JsonException | `[/` (from 6 B) | none none none comment |
| 15,087 | Wide | x=JsonException: '?' is an invalid start of a property name. Expected a '?'. \| y=JsonException | `{y` (from 2 B) | null-out rename:nul-suffix root-2 bit-flip |
| 10,729 | string? | x=JsonException: Expected end of string, but instead reached end of data. \| y=JsonException | `"` (from 1 B) | root-3 truncate |

### F~B NE: 210 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 210 | Note | x=ok \| y=ok | `{"text":"","tags":[],"parent":null,"score":8,"pinned":true,"versIon":8}` (from 96 B) | bit-flip |

### F~B XR-YA: 6,286 cases in 19 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 1,936 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` (from 4 B) | none root-0 |
| 1,266 | Note | x=JsonException: Expected a string but found Null. \| y=ok | `{"pinned":true,"score":1,"tags":[null],"text":"","parent":null,"version":7}` (from 96 B) | null-out |
| 1,206 | Note[] | x=JsonException: Expected an array but found Null. \| y=ok | `null` (from 4 B) | null-out root-0 |
| 928 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` (from 4 B) | root-0 null-out none |
| 241 | Note | x=JsonException: Note requires 'score'. \| y=ok | `{"SCORE":2,"pinned":false,"tags":[],"text":"","parent":null,"version":null}` (from 90 B) | string-text rename:upper |
| 233 | Note | x=JsonException: Note requires 'pinned'. \| y=ok | `{"text":"","tags":[],"parent":null,"score":7,"Pinned":true,"version":null}` (from 96 B) | rename:pascal whitespace-valid |
| 205 | Note | x=JsonException: Note requires 'tags'. \| y=ok | `{"text":"","tAgs":[],"parent":null,"score":4,"pinned":false,"version":5}` (from 100 B) | none bit-flip |
| 200 | Note | x=JsonException: Note requires 'text'. \| y=ok | `{"score":2,"version":9,"parent":null,"tags":[],"pinned":true,"TEXT":"\xF0\xBC\x84\xB3"}` (from 88 B) | none rename:upper reorder |
| 12 | Wide | x=JsonException: Wide requires 'ratio'. \| y=ok | `{"name":"\xEF\xBF\xBF\xE6\x86\x8C","children":[], "maybe":null,"stamps" :[],"nick":null,"limit"\x0A:7,"head":{\x0A"pinned"\x0D:true,"text": "\xE0\xB2\x9EEh\xF2\x86… (430 bytes)` (from 1,068 B) | rename:pascal |
| 9 | Wide | x=JsonException: Wide requires 'ints'. \| y=ok | `{"name":"","nick":"\xF3\x9F\xA9\x81\xF1\xB7\x9B\x96\xF1\xBE\xB0\x8E\xEC\x90\xAA","count":2,"limit":3,"id":4,"stamp":2,"ratio":5,"weight":null,"on":false,"maybe"… (409 bytes)` (from 721 B) | rename:upper |
| 9 | Wide | x=JsonException: Wide requires 'head'. \| y=ok | `{"name":"","nick":"","count":5,"limit":null,"id":2,"stamp":null,"ratio": 57,"weight" :4,"on":true , "maybe":false ,"words":[ ">\ud800\uDC00A","\uDB40\udf69\ud83… (484 bytes)` (from 985 B) | rename:pascal |
| 8 | Wide | x=JsonException: Wide requires 'count'. \| y=ok | `{"\u006F\u006E":false,"\u006D\u0061\u0074\u0072\u0069\u0078":[[],[636]],"\u0069\u0074\u0065\u006d\u0073":[{"\u0074\u0065\u0078\u0074":"\u0027\ud931\udfc047\u006… (1,455 bytes)` (from 1,972 B) | rename:upper whitespace-valid |
| 8 | Wide | x=JsonException: Wide requires 'stamps'. \| y=ok | ` {"name" : "\xF0\x9F\xA7\xBEd\xC2\xB3'p" , "nick":null, "count":80, "limit" :-1 , "id" :8, "stamp":17, "ratio":7 , "weight":4, "on" : false , "maybe" : false ,"… (544 bytes)` (from 1,142 B) | rename:upper |
| 7 | Wide | x=JsonException: Wide requires 'items'. \| y=ok | `{"iteMs"  :\x0D  [{"parent": \x0D\x0Anull,"ven"\x09:null,"pinned"\x0A\x0A\x0D:true\x0D\x0A\x0D\x0D,"tags":["\u00b0100",""],"text": "E"\x0D\x0A,"version"\x0A :75… (819 bytes)` (from 1,653 B) | bit-flip |

### F~B XA-YR: 23,932 cases in 35 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 12,492 | int | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `0]` (from 2 B) | none deep-wrap:array:1 byte-delete |
| 7,370 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'. | `{"text":"","tags":[],"score":8,"pinned":true,"version":1}` (from 61 B) | omit-null-property none none |
| 2,738 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'version'. | `{"text":"","tags":[],"parent":null,"score":0,"pinned":false}` (from 63 B) | drop-property |
| 211 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'?'version'. | `{"text":"\xF0\x90\x80\x80","tags":[],"score":2,"pinned":true}` (from 56 B) | none drop-property drop-property |
| 179 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'nick'. | `{"name":"","count":6,"limit":8,"id":9,"stamp":null,"ratio":6,"weight":6,"on":false,"maybe":false,"words":[],"ints":[],"stamps":[],"matrix":[],"labels":["\xF0\xB1… (303 bytes)` (from 496 B) | number-text type-swap omit-null-property |
| 148 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'limit'. | `{"name":"\xF2\xBE\x89\xA2","head":{"pinned":true,"parent":null,"score":5\x09,\x0A"text":"","version" :-6,"tags":["3t","\xF4\x87\x80\xAE"\x0D]},"nick":null,\x0A"… (327 bytes)` (from 609 B) | bad-utf8-name |
| 148 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'children'. | `{"":[],"name":"","nick":"","count":3,"limit":3,"":"","id":9,"stamp":8,"ratio":5,"weight":4,"on":true,"maybe": false ,"words": [],"ints" : [] , "stamps" :[ ] ,"m… (352 bytes)` (from 844 B) | unknown-property unknown-property reorder bad-utf8-name |
| 144 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'weight'. | `{"name":"","nick":"","count":7,"limit":2,"id":2,"stamp":5,"ratio":9,"on":true,"maybe":true,"words":["\xF0\xB7\xA9\x92\xF4\x8F\xBF\xBD\xC9\x9E\xEA\xA4\xA9\xF0\xB0… (320 bytes)` (from 633 B) | omit-null-property |
| 137 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'stamp'. | `{"nick":"\xEA\xAF\xBF","head":{"score":3,"pinned":true,"tags":["\xE6\x80\x80"],"parent":null,"text":"","version":1},"labels":null,"words":[""],"ratio":1,"matrix… (280 bytes)` (from 693 B) | omit-null-property array-extra-item |
| 117 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'tail'. | `{"head":{"score":1,"tags":[""],"parent":null,"version":1,"text":"\ud932\udf8f","pinned":true},"limit":5,"maybe":null,"words":[""],"children"\x0A:null,"matrix":[… (320 bytes)` (from 668 B) | drop-property whitespace-valid |
| 109 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'maybe'. | `{"name":"","nick":"","count":9,"limit":null,"id":4,"stamp":7,"ratio":4,"weight":null,"on":false,\x09"words":[],"ints":[\x0D],"stamps"\x09:[null],"matrix" :[ \x09… (368 bytes)` (from 664 B) | none bad-name |
| 105 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'labels'. | `{"name":"\xF3\xB0\x85\x90\xF0\xB1\xBA\xAA\xF4\x8F\xBF\xBF\xF3\xB0\x80\x80","nick":null,"count":7,"limit":7,"id":9,"stamp":7,"ratio":4,"weight":null,"on":true,"m… (317 bytes)` (from 642 B) | string-text string-text bad-utf8-name |
| 4 | Note | x=ok \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"text":"\xF2\x86\x86\xAC\xF0\xA0\x80\x80","tags":[],"":null,"score":6,"pinned":false,"Version":9223372036854775809}` (from 143 B) | number-text rename:pascal |
| 4 | Note | x=ok \| y=JsonException: The JSON value could not be converted to System.String. | `{"text":"","\u0074a\u0067s":[],"Pare\u006et":{"\u0074ext":{"":{"":0}},"":3},"":"","s\u0063\u006f\u0072e":7,"p\u0069n\u006Eed":false,"":9}` (from 367 B) | unknown-property deep-wrap:object:2 rename:pascal byte-delete |

### F~B X-nonjson: 31,418 cases in 159 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 3,748 | string? | x=InvalidOperationException: Cannot transcode invalid UTF-# JSON text to UTF-# string. \| y=JsonException: The JSON value could not be converted to System.String. | `"\xC3"` (from 3 B) | none none bad-utf8-string |
| 2,237 | Note | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"":"","":false,"version":1e1}` (from 70 B) | drop-property drop-property number-text |
| 2,091 | string? | x=InvalidOperationException: Cannot read invalid UTF-# JSON text as string. Invalid surrogate value: '?'. \| y=JsonException: The JSON value could not be converted to System.String. | `"\udc00"` (from 8 B) | string-text |
| 1,727 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":null` (from 47 B) | duplicate-property separator-array-0 rename:pascal truncate |
| 1,643 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"":"","":[],"score":[` (from 61 B) | type-swap type-swap truncate |
| 1,599 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a boolean. \| y=JsonException: The JSON value could not be converted to System.Boolean. | `{"pinned":null` (from 14 B) | omit-null-property null-out truncate |
| 1,530 | string? | x=InvalidOperationException: Cannot read incomplete UTF-# JSON text as string with missing low surrogate. \| y=JsonException: The JSON value could not be converted to System.String. | `"\ud800"` (from 8 B) | none string-text |
| 1,465 | int | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `[` (from 1 B) | none type-swap truncate |
| 1,439 | int | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Int#. | `1.5` (from 3 B) | none none type-swap |
| 1,392 | int | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `null` (from 4 B) | null-out root-6 null-out |
| 1,253 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"version":[` (from 56 B) | duplicate-property omit-null-property truncate |
| 1,206 | int | x=InvalidOperationException: Cannot get the value of a token type 'StartObject' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `{` (from 1 B) | type-swap truncate |
| 1,083 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartObject' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"":[],"":null,"score":{` (from 74 B) | drop-property type-swap |
| 984 | Note | x=InvalidOperationException: Cannot get the value of a token type 'String' as a number. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"version":""` (from 31 B) | drop-property duplicate-property reorder truncate |

### F~B RJ: 239,160 cases in 1,685 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 56,794 | Note[] | x=JsonException: '?' is an invalid start of a value. \| y=JsonException | `]` (from 1 B) | byte-delete |
| 17,417 | Note[] | x=JsonException: '?' is invalid after a value. Expected either '?', '?', or '?'. \| y=JsonException | `[/` (from 6 B) | none none none comment |
| 15,087 | Wide | x=JsonException: '?' is an invalid start of a property name. Expected a '?'. \| y=JsonException | `{y` (from 2 B) | null-out rename:nul-suffix root-2 bit-flip |
| 10,729 | string? | x=JsonException: Expected end of string, but instead reached end of data. \| y=JsonException | `"` (from 1 B) | root-3 truncate |

### A~S XR-YA: 5,581 cases in 4 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 1,954 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` (from 4 B) | none root-0 |
| 1,474 | Note | x=JsonException: Expected a string but found Null. \| y=ok | `{"text":"","tags":[null],"score":3,"pinned":false,"":null}` (from 73 B) | omit-null-property null-out whitespace-valid |
| 1,225 | Note[] | x=JsonException: Expected an array but found Null. \| y=ok | `null` (from 4 B) | null-out root-0 |
| 928 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` (from 4 B) | root-0 null-out none |

### A~S XA-YR: 13,103 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 13,103 | int | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `0]` (from 2 B) | none deep-wrap:array:1 byte-delete |

### A~S X-nonjson: 31,418 cases in 36 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 3,790 | string? | x=InvalidOperationException: Cannot transcode invalid UTF-# JSON text to UTF-# string. \| y=JsonException: The JSON value could not be converted to System.String. | `"\xC3"` (from 3 B) | none none bad-utf8-string |
| 2,305 | Note | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"":"","":false,"version":1e1}` (from 70 B) | drop-property drop-property number-text |
| 2,143 | string? | x=InvalidOperationException: Cannot read invalid UTF-# JSON text as string. Invalid surrogate value: '?'. \| y=JsonException: The JSON value could not be converted to System.String. | `"\udc00"` (from 8 B) | string-text |
| 1,783 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":null` (from 47 B) | duplicate-property separator-array-0 rename:pascal truncate |
| 1,686 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"":"","":[],"score":[` (from 61 B) | type-swap type-swap truncate |
| 1,641 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a boolean. \| y=JsonException: The JSON value could not be converted to System.Boolean. | `{"pinned":null` (from 14 B) | omit-null-property null-out truncate |
| 1,588 | Note[] | x=InvalidOperationException: Cannot get the value of a token type 'String' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `[{"score":""` (from 23 B) | null-out type-swap truncate |
| 1,555 | string? | x=InvalidOperationException: Cannot read incomplete UTF-# JSON text as string with missing low surrogate. \| y=JsonException: The JSON value could not be converted to System.String. | `"\ud800"` (from 8 B) | none string-text |

### F~S XR-YA: 5,581 cases in 4 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 1,954 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` (from 4 B) | none root-0 |
| 1,474 | Note | x=JsonException: Expected a string but found Null. \| y=ok | `{"text":"","tags":[null],"score":3,"pinned":false,"":null}` (from 73 B) | omit-null-property null-out whitespace-valid |
| 1,225 | Note[] | x=JsonException: Expected an array but found Null. \| y=ok | `null` (from 4 B) | null-out root-0 |
| 928 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` (from 4 B) | root-0 null-out none |

### F~S XA-YR: 13,103 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 13,103 | int | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `0]` (from 2 B) | none deep-wrap:array:1 byte-delete |

### F~S X-nonjson: 31,418 cases in 36 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 3,790 | string? | x=InvalidOperationException: Cannot transcode invalid UTF-# JSON text to UTF-# string. \| y=JsonException: The JSON value could not be converted to System.String. | `"\xC3"` (from 3 B) | none none bad-utf8-string |
| 2,305 | Note | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"":"","":false,"version":1e1}` (from 70 B) | drop-property drop-property number-text |
| 2,143 | string? | x=InvalidOperationException: Cannot read invalid UTF-# JSON text as string. Invalid surrogate value: '?'. \| y=JsonException: The JSON value could not be converted to System.String. | `"\udc00"` (from 8 B) | string-text |
| 1,783 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":null` (from 47 B) | duplicate-property separator-array-0 rename:pascal truncate |
| 1,686 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"":"","":[],"score":[` (from 61 B) | type-swap type-swap truncate |
| 1,641 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a boolean. \| y=JsonException: The JSON value could not be converted to System.Boolean. | `{"pinned":null` (from 14 B) | omit-null-property null-out truncate |
| 1,588 | Note[] | x=InvalidOperationException: Cannot get the value of a token type 'String' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `[{"score":""` (from 23 B) | null-out type-swap truncate |
| 1,555 | string? | x=InvalidOperationException: Cannot read incomplete UTF-# JSON text as string with missing low surrogate. \| y=JsonException: The JSON value could not be converted to System.String. | `"\ud800"` (from 8 B) | none string-text |

## Writer and round-trip checks (value stream)

| Class | Count | Key | Representative |
|---|---:|---|---|
| FAIL | 751 | A writes / B throws InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object ; A2 writes / B throws InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the … | case 3210 Note value:invalid: A wrote {"text":"d","tags":[],"parent":{"text":"d","tags":[],"parent":{"text":"d","tags":[],"parent":{"text":"d","tags":[],"pare… (4,612 bytes) ; B threw InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  |
| PASS | 111,232 | - | case 10 int value:  |
| PASS-both-throw | 7,455 | B throws ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work whe | case 170 Wide value:invalid: A threw ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work whe ; B threw ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work whe |
| PASS-both-throw | 562 | B throws InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  | case 1420 Note value:invalid: A threw InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  ; B threw InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  |

## JavaScript JSON.parse checks (value stream)

| Class | Count | Key | Representative |
|---|---:|---|---|
| FAIL | 62,722 | integer above 2^53 rounded by JSON.parse | case 286020 Note value: A wrote {"text":"","tags":[],"parent":null,"score":40,"pinned":true,"version":9007199254740993} ; B wrote {"text":"","tags":[],"parent":null,"score":40,"pinned":true,"version":9007199254740993} |
| PASS | 49,261 | - | case 10 int value:  |
