# Fuzz run `final-seed2`

- seed 2; cases requested 600,000; cases run 600,000; batch 4,000; workers 6
- fuzzing time 186.6 s (budget 330 s); total with minimisation 191.6 s
- runtime: .NET 11.0.0 (JIT)

Comparisons: `A~B` original generator vs System.Text.Json (bench options); `F~B` fixed generator vs B; `A~S`/`F~S` against the strict reference S. Classes: EQ both accept, equal; EQ-src equal to each other but not to the source value; NE both accept, values differ; RJ both reject; XR-YA left rejects, right accepts; XA-YR left accepts, right rejects; X-nonjson left throws something other than JsonException; X-unwritable left accepts a value that cannot be written back as JSON (an infinity).

## Case counts

| Comparison | Stream | EQ | NE | RJ | XR-YA | XA-YR | X-nonjson | X-unwritable | PASS | PASS-both-throw | FAIL | total |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A~B | Mutant | 58,819 | 221 | 239,247 | 6,260 | 23,985 | 31,049 | 419 | 0 | 0 | 0 | 360,000 |
| A~B | Valid | 120,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 120,000 |
| A~S | Mutant | 69,684 | 0 | 239,819 | 5,688 | 13,341 | 31,049 | 419 | 0 | 0 | 0 | 360,000 |
| A~S | Valid | 120,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 120,000 |
| F~B | Mutant | 58,818 | 221 | 283,290 | 7,027 | 10,644 | 0 | 0 | 0 | 0 | 0 | 360,000 |
| F~B | Valid | 120,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 120,000 |
| F~S | Mutant | 69,683 | 0 | 284,226 | 6,091 | 0 | 0 | 0 | 0 | 0 | 0 | 360,000 |
| F~S | Valid | 120,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 120,000 |
| JS | Value | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 49,625 | 0 | 62,342 | 111,967 |
| W | Value | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 108,507 | 8,033 | 3,460 | 120,000 |

## Incidents (crash or hang)

None.

## Representatives

Key: left outcome | right outcome (normalised message). The example is the shortest case in the group, minimised with ddmin while class and keys stay the same.

### A~B NE: 221 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 221 | Note | x=ok \| y=ok | `{"text":"","tags":[],"parent":null,"score":0,"pinned":false,"Version":7}` (from 105 B) | number-text rename:pascal |

### A~B XR-YA: 6,260 cases in 19 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 1,934 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` (from 4 B) | null-out none none |
| 1,270 | Note | x=JsonException: Expected a string but found Null. \| y=ok | `{"text":"","tags":[null],"parent":null,"score":3,"pinned":false,"version":7}` (from 98 B) | null-out |
| 1,262 | Note[] | x=JsonException: Expected an array but found Null. \| y=ok | `null` (from 4 B) | none null-out none |
| 922 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` (from 4 B) | bad-name root-0 |
| 206 | Note | x=JsonException: Note requires 'tags'. \| y=ok | `{"text":"","Tags":[],"parent":null,"score":5,"pinned":false,"version":1}` (from 102 B) | rename:pascal |
| 204 | Note | x=JsonException: Note requires 'pinned'. \| y=ok | `{"text":"\uda79\udd55","tags":[],"parent":null,"score":1,"PINNED":true,"version":0}` (from 90 B) | rename:upper |
| 203 | Note | x=JsonException: Note requires 'score'. \| y=ok | `{"text":"","tags":[],"parent":null,"Score":5,"pinned":true,"version":4}` (from 79 B) | rename:pascal none |
| 187 | Note | x=JsonException: Note requires 'text'. \| y=ok | `{"Text":"","tags":[],"parent":null,"pinned":false,"score":0,"version":null}` (from 87 B) | bit-flip |
| 9 | Wide | x=JsonException: Wide requires 'name'. \| y=ok | `{"\u0069\u006E\u0074s":[7],"m\u0061yb\u0065":true,"r\u0061\u0074i\u006F":91,"w\u0065ight":67,"\u0069d":0,"count":-1,"on":false,"stamp":92,"chi\u006cd\u0072en":[… (557 bytes)` (from 973 B) | bit-flip |
| 9 | Wide | x=JsonException: Wide requires 'matrix'. \| y=ok | `{"labels":[null,null,"\xF3\xA6\xB4\x9C"],"id":5,"ratio":7,"weight":null,"tail":{"score":0,"version":null,"parent":null,"pinned":false,"text":"\xC4\xB7","tags":[… (429 bytes)` (from 901 B) | rename:upper unknown-property |
| 9 | Wide | x=JsonException: Wide requires 'words'. \| y=ok | `{"t\u0061i\u006c":{"t\u0065x\u0074":"\f","s\u0063or\u0065":2,"\u0070arent":null,"ve\u0072si\u006Fn":34,"tag\u0073":["\uD880\udc00\uda01\uDF3D\xF0\xAF\xBF\xBF"],… (752 bytes)` (from 1,561 B) | rename:upper |
| 8 | Wide | x=JsonException: Wide requires 'on'. \| y=ok | `{"name":"\xDD\x80","nick":null,"count":2,"limit":1,"id":1,"stamp":4,"ratio":0,"weight":null,"ON":false,"maybe":null,"words":["",""],"ints":[],"stamps":[null],"m… (476 bytes)` (from 862 B) | rename:upper |
| 7 | Wide | x=JsonException: Wide requires 'items'. \| y=ok | `{"labels" : null, "name":"%%2","id":953358,"stamps" :[97],"words" :[],"matrix" : [[-90] ], "limit": null ,"children":null,"Items":[ {"tags": [ ], "parent": null… (1,351 bytes)` (from 2,196 B) | duplicate-property rename:pascal trailing:  |
| 6 | Wide | x=JsonException: Wide requires 'id'. \| y=ok | `{"name":"!C\udb9a\udf58"\x0A,"nick":null,"count":1,"limit":72,"Id":5,"stamp":-9,"ratio": 5,"weight":6 ,"on":\x0A\x09true,"maybe":null\x09,\x0A\x0D\x09"words":  … (838 bytes)` (from 1,782 B) | rename:pascal whitespace-valid |

### A~B XA-YR: 23,985 cases in 31 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 12,716 | int | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `1,` (from 2 B) | none none root-4 trailing:, |
| 7,255 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'. | `{"text":"","tags":[],"score":5,"pinned":false,"version":4}` (from 68 B) | omit-null-property |
| 2,626 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'version'. | `{"pinned":true,"score":4,"tags":[],"parent":null,"text":""}` (from 66 B) | string-text trailing-comma drop-property |
| 239 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'?'version'. | `{"score":0,"tags":[],"text":"","pinned":false}` (from 62 B) | omit-null-property none drop-property |
| 185 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'nick'. | `{"name":"","":"","count":5,"limit":1,"id":6,"stamp":4,"ratio":7,"weight":6,"on":false,"maybe":true,"words":["",""],"ints":[],"stamps":[],"matrix":[],"labels":["… (303 bytes)` (from 576 B) | bad-utf8-name |
| 154 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'weight'. | `{"name":"","nick":null,"co\u0075nt":0,"limit":7,"i\u0064":1,"\u0073tamp":2,"r\u0061ti\u006F":7,"on":false,"may\u0062e":null,"\u0077o\u0072ds":[],"\u0069\u006E\u… (463 bytes)` (from 565 B) | drop-property none |
| 150 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'limit'. | `{"name":"\xE2\x80\x8B\xC2\xA0\xF0\x90\x80\x80","nick":null,"count":1,"":null,"id":9,"stamp":3,"ratio":1,"weight":null,"on":true, "maybe": null,"words" : [] , "i… (449 bytes)` (from 768 B) | rename:empty |
| 149 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'stamp'. | `{"name":"","nick":"","count":4,"limit":8,"id":1,"ratio":1,"weight":1,"on":false,"maybe":true,"words":[],"ints":[],"stamps":[],"matrix":[[]],"labels":["",null,"\xF4… (314 bytes)` (from 548 B) | omit-null-property unknown-property |
| 142 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'children'. | `{"name":"","nick":"","count":3,"limit":null,"id":6,"stamp":6,"ratio":0,"weight":1,"on":true,"maybe":true,"words":[],"ints":[],"stamps":[],"matrix":[],"labels":n… (328 bytes)` (from 548 B) | bad-utf8-name |
| 115 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'maybe'. | `{"name":"","nick":null,"count":4,"limit":null,"id":7,"stamp":7,"ratio":1,"weight":null,"on":true,"words":["\xE2\x80\x8B\xF0\xA0\x80\x80\xE1\x9F\xA2\xF3\x84\x9B\xA3… (315 bytes)` (from 499 B) | drop-property |
| 110 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'tail'. | `{"name":"","nick":"\ud880\uDC85","count":4,"limit":null,"id":9,"stamp":2,"ratio":9,"weight":2,"on":true,"maybe":true,"words":[],"ints":[ ] , "stamps":[ ] ,"matr… (338 bytes)` (from 609 B) | drop-property |
| 109 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'labels'. | `{"name":"","nick":"\xE9\x9D\x9F\xE4\x87\xB9\xE2\xA3\x9A\xF0\x97\xA6\x87","count":2,"limit":1,"id":6,"stamp":null,"ratio":4,"weight":null,"on":false,"maybe":fals… (333 bytes)` (from 474 B) | unknown-property drop-property leading-trailing-whitespace |
| 5 | Note | x=ok \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"text":"","tags":[],"":null,"score":7,"pinned":false,"VERSION":""}` (from 140 B) | type-swap rename:upper |
| 4 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'text'. | `{"":5,"tags":["\uD880\udc00"],"Parent":{"parent":null,"tags":[],"pinned":false,"score":4,"version":2},"pinned":false,"text":"","score":4}` (from 287 B) | rename:pascal none drop-property |

### A~B X-nonjson: 31,049 cases in 166 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 3,666 | string? | x=InvalidOperationException: Cannot transcode invalid UTF-# JSON text to UTF-# string. \| y=JsonException: The JSON value could not be converted to System.String. | `"\xFF"` (from 3 B) | bad-utf8-string |
| 2,213 | string? | x=InvalidOperationException: Cannot read invalid UTF-# JSON text as string. Invalid surrogate value: '?'. \| y=JsonException: The JSON value could not be converted to System.String. | `"\udc00"` (from 8 B) | none string-text none |
| 2,179 | Note | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"vers\u0069\u006f\u006E":1e9,` (from 58 B) | number-text truncate |
| 1,639 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":null` (from 22 B) | array-extra-item null-out type-swap truncate |
| 1,626 | string? | x=InvalidOperationException: Cannot read incomplete UTF-# JSON text as string with missing low surrogate. \| y=JsonException: The JSON value could not be converted to System.String. | `"\ud800"` (from 8 B) | none none string-text |
| 1,580 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a boolean. \| y=JsonException: The JSON value could not be converted to System.Boolean. | `{"":null,"pinned":null` (from 71 B) | null-out literal-text truncate |
| 1,561 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":[` (from 20 B) | duplicate-property rename:prefix truncate |
| 1,445 | int | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Int#. | `1e2` (from 3 B) | type-swap none none |
| 1,400 | int | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `[` (from 1 B) | none root-1 truncate |
| 1,367 | int | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `null` (from 4 B) | none null-out |
| 1,202 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"":"","":[],"":4,"version":[` (from 86 B) | drop-property type-swap |
| 1,177 | int | x=InvalidOperationException: Cannot get the value of a token type 'StartObject' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `{` (from 1 B) | none type-swap root-2 truncate |
| 1,113 | Note[] | x=InvalidOperationException: Cannot get the value of a token type 'StartObject' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `[{"":null,"score":{` (from 40 B) | duplicate-property truncate |
| 952 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a boolean. \| y=JsonException: The JSON value could not be converted to System.Boolean. | `{"":"","pinned":[` (from 34 B) | duplicate-property trailing-comma truncate |

### A~B X-unwritable: 419 cases in 11 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 317 | Note | x=ok \| y=ok | `{"tags":[],"text":"","score":1E400,"pinned":true,"version":7,"parent":null}` (from 102 B) | number-text |
| 60 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'. | `{"tags":[],"pinned":true,"score":1e999,"version":null,"text":""}` (from 91 B) | reorder omit-null-property number-text |
| 17 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'version'. | `{"text":"","tags":[],"parent":null,"score":1E400,"pinned":true,"":null}` (from 132 B) | number-text bad-utf8-name |
| 14 | Note | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `{"text":"\uD87F\uDFFF","tags":[],"parent":null,"score":1E400,"pinned":true,"version":null}\xBF` (from 146 B) | number-text trailing:\xEF\xBB\xBF |
| 3 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'stamp'. | `{"name":"","nick":"","count":6,"limit":6,"id":5,"ratio":7,"weight":3,"on":true,"maybe":false,"words":[""],"ints":[8],"stamps":[3],"matrix":[[6E400]],"labels":[]… (397 bytes)` (from 942 B) | omit-null-property number-text |
| 2 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'limit'. | `{\x0A\x0D\x0A"\u006e\u0061\u006d\u0065":"\u80e5\uE38D\udbcd\udf72\u0063\udbff\uDFFF\u0026\u007f",\x0D"\u006E\u0069\u0063\u006b":"0011", \x0D"\u0063\u006f\u0075\… (2,009 bytes)` (from 3,321 B) | unknown-property omit-null-property number-text |
| 2 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'?'version'. | `{"text":"","tags":["\xE2\x80\xA8\xF0\xB0\x80\x80\xF4\x86\x92\x9A\xF0\xAF\xBF\xBF"],"score":1e999,"pinned":true}` (from 147 B) | drop-property omit-null-property number-text leading-trailing-whitespace |
| 1 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'labels'. | `{"name":"i","nick":"\uda15\uDC05c1\udb75\uDCFE","count":-2147,"limit":65536,"id":-165866,"stamp":-536393,"ratio":-75,"weight":5E-324,"on":true,"maybe":true,"wor… (1,334 bytes)` (from 3,068 B) | number-text bad-utf8-name |
| 1 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'nick'. | `{"\u006e\u0061\u006d\u0065" : \x0A"\u0067\u0058\u0051\u003D\u0051\u0031\u0058\u0051\u003d\u004f",\x09\x09"\u006e\u0069\u0063\u006B":"","\u0063\u006f\u0075\u006E… (24,934 bytes)` (from 41,890 B) | number-text omit-null-property string-text |
| 1 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'maybe'. | ` {"\u006e\u0069\u0063\u006b" :"\u030F\u0045\uFFFEu07a4","\u0069\u006e\u0074\u0073" : [],"\u0072\u0061\u0074\u0069\u006F" : -0, "\u0073\u0074\u0061\u006D\u0070\u… (5,819 bytes)` (from 6,559 B) | omit-null-property number-text deep-chain:60 whitespace-valid |
| 1 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'weight'. | `{"na\u006De":"\uD943\ude5a\u000c\xF3\xA0\x80\x81","\u006eick":"\u200d\udb80\uDC00\u004D","\u0063o\u0075\u006et":1,"l\u0069m\u0069\u0074":null,"\u0069\u0064":828… (8,882 bytes)` (from 14,940 B) | number-text omit-null-property |

### A~B RJ: 239,246 cases in 1,707 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 57,106 | Note | x=JsonException: '?' is an invalid start of a value. \| y=JsonException | `\x0C` (from 1 B) | duplicate-property root-7 whitespace-invalid:\x0C |
| 17,111 | Note | x=JsonException: '?' is invalid after a value. Expected either '?', '?', or '?'. \| y=JsonException | `{/` (from 6 B) | root-2 none comment |
| 14,956 | Note | x=JsonException: '?' is an invalid start of a property name. Expected a '?'. \| y=JsonException | `{\x7C` (from 2 B) | root-2 bit-flip |
| 10,931 | int | x=JsonException: Expected end of string, but instead reached end of data. \| y=JsonException | `"` (from 1 B) | type-swap truncate |

### F~B NE: 221 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 221 | Note | x=ok \| y=ok | `{"text":"","tags":[],"parent":null,"score":0,"pinned":false,"Version":7}` (from 105 B) | number-text rename:pascal |

### F~B XR-YA: 7,027 cases in 21 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 1,933 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` (from 4 B) | null-out none none |
| 1,265 | Note | x=JsonException: Expected a string but found Null at byte #. \| y=ok | `{"text":"","tags":[null],"parent":null,"score":3,"pinned":false,"version":7}` (from 98 B) | null-out |
| 1,262 | Note[] | x=JsonException: Expected an array but found Null at byte #. \| y=ok | `null` (from 4 B) | none null-out none |
| 922 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` (from 4 B) | bad-name root-0 |
| 449 | int | x=JsonException: Expected a number but found String at byte #. \| y=ok | `"1"` (from 3 B) | type-swap none none |
| 329 | Note | x=JsonException: The number at byte # is not a valid Double. \| y=ok | `{"tags":[],"text":"","score":1E400,"pinned":true,"version":7,"parent":null}` (from 102 B) | number-text |
| 206 | Note | x=JsonException: Note requires 'tags'. \| y=ok | `{"text":"","Tags":[],"parent":null,"score":5,"pinned":false,"version":1}` (from 102 B) | rename:pascal |
| 202 | Note | x=JsonException: Note requires 'score'. \| y=ok | `{"text":"","tags":[],"parent":null,"Score":5,"pinned":true,"version":4}` (from 79 B) | rename:pascal none |
| 201 | Note | x=JsonException: Note requires 'pinned'. \| y=ok | `{"text":"\uda79\udd55","tags":[],"parent":null,"score":1,"PINNED":true,"version":0}` (from 90 B) | rename:upper |
| 186 | Note | x=JsonException: Note requires 'text'. \| y=ok | `{"Text":"","tags":[],"parent":null,"pinned":false,"score":0,"version":null}` (from 87 B) | bit-flip |
| 9 | Wide | x=JsonException: Wide requires 'name'. \| y=ok | `{"\u0069\u006E\u0074s":[7],"m\u0061yb\u0065":true,"r\u0061\u0074i\u006F":91,"w\u0065ight":67,"\u0069d":0,"count":-1,"on":false,"stamp":92,"chi\u006cd\u0072en":[… (557 bytes)` (from 973 B) | bit-flip |
| 9 | Wide | x=JsonException: Wide requires 'matrix'. \| y=ok | `{"labels":[null,null,"\xF3\xA6\xB4\x9C"],"id":5,"ratio":7,"weight":null,"tail":{"score":0,"version":null,"parent":null,"pinned":false,"text":"\xC4\xB7","tags":[… (429 bytes)` (from 901 B) | rename:upper unknown-property |
| 9 | Wide | x=JsonException: Wide requires 'words'. \| y=ok | `{"t\u0061i\u006c":{"t\u0065x\u0074":"\f","s\u0063or\u0065":2,"\u0070arent":null,"ve\u0072si\u006Fn":34,"tag\u0073":["\uD880\udc00\uda01\uDF3D\xF0\xAF\xBF\xBF"],… (754 bytes)` (from 1,561 B) | rename:upper |
| 8 | Wide | x=JsonException: Wide requires 'on'. \| y=ok | `{"name":"\xDD\x80","nick":null,"count":2,"limit":1,"id":1,"stamp":4,"ratio":0,"weight":null,"ON":false,"maybe":null,"words":["",""],"ints":[],"stamps":[null],"m… (476 bytes)` (from 862 B) | rename:upper |

### F~B XA-YR: 10,644 cases in 29 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 6,811 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'. | `{"text":"","tags":[],"score":5,"pinned":false,"version":4}` (from 68 B) | omit-null-property |
| 2,508 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'version'. | `{"pinned":true,"score":4,"tags":[],"parent":null,"text":""}` (from 66 B) | string-text trailing-comma drop-property |
| 224 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'?'version'. | `{"score":0,"tags":[],"text":"","pinned":false}` (from 62 B) | omit-null-property none drop-property |
| 173 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'nick'. | `{"name":"","":"","count":5,"limit":1,"id":6,"stamp":4,"ratio":7,"weight":6,"on":false,"maybe":true,"words":["",""],"ints":[],"stamps":[],"matrix":[],"labels":["… (303 bytes)` (from 576 B) | bad-utf8-name |
| 151 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'weight'. | `{"name":"","nick":null,"co\u0075nt":0,"limit":7,"i\u0064":1,"\u0073tamp":2,"r\u0061ti\u006F":7,"on":false,"may\u0062e":null,"\u0077o\u0072ds":[],"\u0069\u006E\u… (463 bytes)` (from 565 B) | drop-property none |
| 143 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'stamp'. | `{"name":"","nick":"","count":4,"limit":8,"id":1,"ratio":1,"weight":1,"on":false,"maybe":true,"words":[],"ints":[],"stamps":[],"matrix":[[]],"labels":["",null,"\xF4… (314 bytes)` (from 548 B) | omit-null-property unknown-property |
| 143 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'limit'. | `{"name":"\xE2\x80\x8B\xC2\xA0\xF0\x90\x80\x80","nick":null,"count":1,"":null,"id":9,"stamp":3,"ratio":1,"weight":null,"on":true, "maybe": null,"words" : [] , "i… (449 bytes)` (from 768 B) | rename:empty |
| 136 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'children'. | `{"name":"","nick":"","count":3,"limit":null,"id":6,"stamp":6,"ratio":0,"weight":1,"on":true,"maybe":true,"words":[],"ints":[],"stamps":[],"matrix":[],"labels":n… (328 bytes)` (from 548 B) | bad-utf8-name |
| 112 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'maybe'. | `{"name":"","nick":null,"count":4,"limit":null,"id":7,"stamp":7,"ratio":1,"weight":null,"on":true,"words":["\xE2\x80\x8B\xF0\xA0\x80\x80\xE1\x9F\xA2\xF3\x84\x9B\xA3… (315 bytes)` (from 499 B) | drop-property |
| 106 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'tail'. | `{"name":"","nick":"\ud880\uDC85","count":4,"limit":null,"id":9,"stamp":2,"ratio":9,"weight":2,"on":true,"maybe":true,"words":[],"ints":[ ] , "stamps":[ ] ,"matr… (338 bytes)` (from 609 B) | drop-property |
| 105 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'labels'. | `{"name":"","nick":"\xE9\x9D\x9F\xE4\x87\xB9\xE2\xA3\x9A\xF0\x97\xA6\x87","count":2,"limit":1,"id":6,"stamp":null,"ratio":4,"weight":null,"on":false,"maybe":fals… (333 bytes)` (from 474 B) | unknown-property drop-property leading-trailing-whitespace |
| 4 | Note | x=ok \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"text":"","tags":[],"":null,"score":7,"pinned":false,"VERSION":""}` (from 140 B) | type-swap rename:upper |
| 4 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'text'. | `{"":5,"tags":["\uD880\udc00"],"Parent":{"parent":null,"tags":[],"pinned":false,"score":4,"version":2},"pinned":false,"text":"","score":4}` (from 287 B) | rename:pascal none drop-property |
| 3 | Note | x=ok \| y=JsonException: The JSON value could not be converted to System.String. | `{"PA\u0052EN\u0054":{"\u0074\u0061gs":["\xA4"],"":null,"":3},"\u0073cor\u0065":6,"\u0074\u0061g\u0073":[],"pi\u006e\u006Ee\u0064":true,"":1,"te\u0078t":""}` (from 484 B) | number-text type-swap rename:upper |

### F~B RJ: 283,289 cases in 1,721 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 57,063 | Note | x=JsonException: '?' is an invalid start of a value. \| y=JsonException | `\x0C` (from 1 B) | duplicate-property root-7 whitespace-invalid:\x0C |
| 17,083 | Note | x=JsonException: '?' is invalid after a value. Expected either '?', '?', or '?'. \| y=JsonException | `{/` (from 6 B) | root-2 none comment |
| 14,931 | Note | x=JsonException: '?' is an invalid start of a property name. Expected a '?'. \| y=JsonException | `{\x7C` (from 2 B) | root-2 bit-flip |
| 13,341 | int | x=JsonException: '?' is invalid after a single JSON value. Expected end of data. \| y=JsonException | `1,` (from 2 B) | none none root-4 trailing:, |

### A~S XR-YA: 5,688 cases in 4 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 1,955 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` (from 4 B) | null-out none none |
| 1,531 | Note | x=JsonException: Expected a string but found Null. \| y=ok | `{"text":"","tags":["",null],"score":6,"pinned":true,"":8}` (from 94 B) | type-swap null-out drop-property whitespace-valid |
| 1,280 | Note[] | x=JsonException: Expected an array but found Null. \| y=ok | `null` (from 4 B) | none null-out none |
| 922 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` (from 4 B) | bad-name root-0 |

### A~S XA-YR: 13,341 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 13,341 | int | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `1,` (from 2 B) | none none root-4 trailing:, |

### A~S X-nonjson: 31,049 cases in 35 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 3,721 | string? | x=InvalidOperationException: Cannot transcode invalid UTF-# JSON text to UTF-# string. \| y=JsonException: The JSON value could not be converted to System.String. | `"\xFF"` (from 3 B) | bad-utf8-string |
| 2,269 | string? | x=InvalidOperationException: Cannot read invalid UTF-# JSON text as string. Invalid surrogate value: '?'. \| y=JsonException: The JSON value could not be converted to System.String. | `"\udc00"` (from 8 B) | none string-text none |
| 2,244 | Note | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"vers\u0069\u006f\u006E":1e9,` (from 58 B) | number-text truncate |
| 1,671 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":null` (from 22 B) | array-extra-item null-out type-swap truncate |
| 1,663 | string? | x=InvalidOperationException: Cannot read incomplete UTF-# JSON text as string with missing low surrogate. \| y=JsonException: The JSON value could not be converted to System.String. | `"\ud800"` (from 8 B) | none none string-text |
| 1,631 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a boolean. \| y=JsonException: The JSON value could not be converted to System.Boolean. | `{"":null,"pinned":null` (from 71 B) | null-out literal-text truncate |
| 1,610 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":[` (from 20 B) | duplicate-property rename:prefix truncate |
| 1,523 | Note | x=InvalidOperationException: Cannot get the value of a token type 'String' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":""` (from 64 B) | drop-property literal-text type-swap |

### A~S X-unwritable: 419 cases in 2 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 402 | Note | x=ok \| y=ok | `{"tags":[],"pinned":true,"score":1e999,"":null,"text":""}` (from 91 B) | reorder omit-null-property number-text |
| 17 | Note | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `{"text":"\uD87F\uDFFF","tags":[],"":null,"score":1E400,"pinned":true,"":null}\xBF` (from 146 B) | number-text trailing:\xEF\xBB\xBF |

### F~S XR-YA: 6,091 cases in 5 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 1,954 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` (from 4 B) | null-out none none |
| 1,525 | Note | x=JsonException: Expected a string but found Null at byte #. \| y=ok | `{"text":"","tags":["",null],"score":6,"pinned":true,"":8}` (from 94 B) | type-swap null-out drop-property whitespace-valid |
| 1,280 | Note[] | x=JsonException: Expected an array but found Null at byte #. \| y=ok | `null` (from 4 B) | none null-out none |
| 922 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` (from 4 B) | bad-name root-0 |
| 410 | Note | x=JsonException: The number at byte # is not a valid Double. \| y=ok | `{"tags":[],"pinned":true,"score":1e999,"":null,"text":""}` (from 91 B) | reorder omit-null-property number-text |

## Writer and round-trip checks (value stream)

| Class | Count | Key | Representative |
|---|---:|---|---|
| FAIL | 2,680 | readA(writeA(x)) != x; readA(writeA2(x)) != x; readF(writeF(x)) != x; readF(writeF2(x)) != x; readB(writeA(x)) != x | case 81800 string? value:invalid: A wrote "\uFFFD" ; B wrote "\uFFFD" |
| FAIL | 780 | A writes / B throws InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object ; A2 writes / B throws InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the … | case 4890 Note value:invalid: A wrote {"text":"d","tags":[],"parent":{"text":"d","tags":[],"parent":{"text":"d","tags":[],"parent":{"text":"d","tags":[],"pare… (4,612 bytes) ; B threw InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  |
| PASS | 108,507 | - | case 2231 int value:invalid:  |
| PASS-both-throw | 7,472 | B throws ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work whe | case 11 Wide value:invalid: A threw ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work whe ; B threw ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work whe |
| PASS-both-throw | 561 | B throws InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  | case 1050 Note value:invalid: A threw InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  ; B threw InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  |

## JavaScript JSON.parse checks (value stream)

| Class | Count | Key | Representative |
|---|---:|---|---|
| FAIL | 62,342 | integer above 2^53 rounded by JSON.parse | case 201021 Note value: A wrote {"text":"","tags":[],"parent":null,"score":4,"pinned":false,"version":9007199254740993} ; B wrote {"text":"","tags":[],"parent":null,"score":4,"pinned":false,"version":9007199254740993} |
| PASS | 49,625 | - | case 2231 int value:invalid:  |
