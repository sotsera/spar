# Fuzz run `jit-seed3`

- seed 3; cases requested 120,000; cases run 120,000; batch 4,000; workers 6
- fuzzing time 37.2 s (budget 420 s); total with minimisation 37.4 s
- runtime: .NET 11.0.0 (JIT)

Comparisons: `A~B` original generator vs System.Text.Json (bench options); `F~B` fixed generator vs B; `A~S`/`F~S` against the strict reference S. Classes: EQ both accept, equal; EQ-src equal to each other but not to the source value; NE both accept, values differ; RJ both reject; XR-YA left rejects, right accepts; XA-YR left accepts, right rejects; X-nonjson left throws something other than JsonException; X-unwritable left accepts a value that cannot be written back as JSON (an infinity).

## Case counts

| Comparison | Stream | EQ | NE | RJ | XR-YA | XA-YR | X-nonjson | X-unwritable | PASS | PASS-both-throw | FAIL | total |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A~B | Mutant | 11,781 | 54 | 47,876 | 1,324 | 4,650 | 6,248 | 67 | 0 | 0 | 0 | 72,000 |
| A~B | Valid | 24,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 24,000 |
| A~S | Mutant | 13,938 | 0 | 48,024 | 1,176 | 2,547 | 6,248 | 67 | 0 | 0 | 0 | 72,000 |
| A~S | Valid | 24,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 24,000 |
| F~B | Mutant | 11,781 | 54 | 56,585 | 1,477 | 2,103 | 0 | 0 | 0 | 0 | 0 | 72,000 |
| F~B | Valid | 24,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 24,000 |
| F~S | Mutant | 13,938 | 0 | 56,822 | 1,240 | 0 | 0 | 0 | 0 | 0 | 0 | 72,000 |
| F~S | Valid | 24,000 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 24,000 |
| JS | Value | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 9,810 | 0 | 12,628 | 22,438 |
| W | Value | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 21,724 | 1,562 | 714 | 24,000 |

## Incidents (crash or hang)

None.

## Representatives

Key: left outcome | right outcome (normalised message). The example is the shortest case in the group, minimised with ddmin while class and keys stay the same.

### A~B NE: 54 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 54 | Note | x=ok \| y=ok | `{"score" :9.559579260804598E+99 , "VERSION" : -9007199254740991,"pinned" : false , "tags":[] , "text":"\uDA6B\uDFB3'" , "parent":null}` | rename:upper |

### A~B XR-YA: 1,324 cases in 18 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 389 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` | null-out none none |
| 269 | Note | x=JsonException: Expected a string but found Null. \| y=ok | `{"text":"\xF0\xB0\x80\x80\xC7\xBA!","tags":["\/\xEF\xBB\xBF\xF0\x9D\x82\xB4\xEB\xA6\xB3\xF4\x8F\xBF\xBF",null],"parent":null,"score":-5E-324,"pinned":true,"vers… (112 bytes)` | null-out |
| 255 | Note[] | x=JsonException: Expected an array but found Null. \| y=ok | `null` | null-out |
| 204 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` | root-0 |
| 60 | Note | x=JsonException: Note requires 'text'. \| y=ok | `{\x0A\x0D "Text":\x0D\x0D"\uffff\u06d7\u0007",\x0A\x0D\x0A"tags":[]\x0D\x0D,"parent":null\x0A,"score":97e0,"pinned":true,"version":-2990714274680017539}` | rename:pascal |
| 54 | Note | x=JsonException: Note requires 'tags'. \| y=ok | `{"text":"\uDBFF\uDFFF","Tags":[],"parent":null,"score":1.1922552125689468E+271,"pinned":false,"version":-8595313678139402908}` | string-text rename:pascal |
| 40 | Note | x=JsonException: Note requires 'pinned'. \| y=ok | `{"score"\x0A \x0A:-3.4349711611009896E-118,"text":\x0A"","PINNED":false,"tags":\x0A\x09[]\x0A ,\x09\x0A"version":-9195399437841943760,"parent":null}` | rename:upper |
| 33 | Note | x=JsonException: Note requires 'score'. \| y=ok | `{"text":"M\t\xC5\x83\xF0\xB7\x91\x93",\x0A"tags":["{L8]V#M>}\"4D"],"parent":null,"Score":3.8328538480399998889e+002,"pinned"\x0A:\x09true\x09,\x09\x09"version"\x09… (150 bytes)` | rename:pascal |
| 4 | Wide | x=JsonException: Wide requires 'ratio'. \| y=ok | `{"n\u0061m\u0065":"\f\xF3\xA0\x80\x81\uDB5B\udd1a\ufffd\ud880\udc00","ni\u0063k":"\u003c&\xF4\x8F\xBF\xBF\f\xF0\x90\x80\x80","coun\u0074":493695903,"\u006cimi\u… (1,321 bytes)` | rename:upper rename:pascal |
| 3 | Wide | x=JsonException: Wide requires 'stamps'. \| y=ok | `{ "name": "h>T]:We+zzH)","nick" : null, "count":947,"limit":null,"id": -9223372036854775807 ,"stamp": 8180379545193225411, "ratio": -3.441005770484305E+158, "we… (2,939 bytes)` | rename:pascal |
| 3 | Wide | x=JsonException: Wide requires 'id'. \| y=ok | `{"name":"}p7Kp&])"\x09,"nick":null\x0D\x09\x0A\x09,\x09\x0A\x0D"count":255,"limit":1,"ID":9007199254740992, \x0D "stamp":null,"ratio":-4.588451476956941E+217,\x0A… (1,145 bytes)` | rename:upper |
| 2 | Wide | x=JsonException: Wide requires 'head'. \| y=ok | `{"items":[{"version":null,"tags":["","$Eq\xF1\xAE\x93\x9E\u0012L~\xC9\xB0\u005c/\xC6\xA7"],"parent":null,"text":"22s","score":790.3590513,"pinned":false},{"tags… (3,573 bytes)` | rename:pascal |
| 2 | Wide | x=JsonException: Wide requires 'words'. \| y=ok | ` { "name" :"\u0075\u0025\u0051?n&\u006ds^", "nick": null , "cou\u006e\u0074" : -2147483647, "l\u0069mit" :-434, "id" :-480884 , "sta\u006d\u0070":-250651 ,"\u00… (1,231 bytes)` | rename:pascal |
| 2 | Wide | x=JsonException: Wide requires 'count'. \| y=ok | `{ "weight" :1.2345678901234568E+20 ,"words": [ ">\ud87f\udfff\x608\x60r\u000e\ud9ca\udf71\u02B8" , "\uD83E\uDDD1\x7F\uDBDE\uDE88" ,"\uFFFDk\uD880\uDC00N\uD880\u… (1,809 bytes)` | rename:pascal |

### A~B XA-YR: 4,650 cases in 16 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 2,417 | int | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `0]` | type-swap none none trailing:] |
| 1,436 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'. | `{"text":"NaN","tags":[],"score":1e-300,"pinned":true,"version":-485175}` | omit-null-property type-swap |
| 526 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'version'. | `{"parent":null,"score":702.19577742,"tags":[],"pinned":false,"text":"\xCA\x9Fb"}` | reorder drop-property |
| 46 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'?'version'. | `{"text":"eI;N2r}","score":670.08,"versio":-967276,"tags":[],"pinned":true}` | rename:prefix omit-null-property |
| 36 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'limit'. | `{"words":\x0D\x0A[],"on":\x0D\x0A\x09\x0Dtrue,\x0D"name":"\ufffd>\uda44\uDFFF","children"\x0A\x0A:null,"items":[\x09\x0D\x0D\x0D],"nick":"R","stamp":-4812880557… (714 bytes)` | array-extra-item omit-null-property null-out |
| 35 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'nick'. | ` { "maybe" :true , "ratio":-6.653126284780044E+72 , "limit":null ,"stamp": -611500899982299374,"id":-2356553731790752831 , "labels" : null ,"words": [ ], "stamp… (651 bytes)` | omit-null-property reorder |
| 34 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'stamp'. | ` \x0A{"maybe" \x0D:null  ,\x0D \x0A\x0D"on":false,"head":{"parent":null,\x0A\x09\x09"tags"\x0D\x09\x09:[ \x0D\x0A\x0D],"pinned":true\x0D  ,\x0D"score":-38,\x0D\x09… (917 bytes)` | duplicate-property rename:space leading-trailing-whitespace |
| 27 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'weight'. | `{"name":"Gc=6sK'uiOa-", "nick": "^u\\amZQ\x7CIOA&\/N","count" : -247,"limit":-781 ,"id": 4904803613736290570 ,"stamp" :null , "ratio":12345.678 ,"\x80weight" :1… (757 bytes)` | bad-utf8-name |
| 27 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'children'. | `\x09\x0A\x09\x0A{"labels":["","\xD2\xA8\xF1\x9E\xBA\xAB","\xF3\xA7\x8D\xB3",">\xC2\xADN\xC2\x85\xD7\x8D\xC2\xA0\xE5\x90\x98~\xE4\xA7\xAE-\xF0\xB0\x80\x80\xF3\xA0… (931 bytes)` | bad-utf8-name |
| 22 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'labels'. | `{ "name":"\xF3\xB0\x80\x80", "nick" : null,"count":321091674,"limit" :null, "id":-902779 ,"stamp" :-456382, "ratio": 679.5257184093 ,"weight" :5E-324 , "on": fa… (644 bytes)` | byte-insert |
| 22 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'maybe'. | `{"head":{"score":-1.688494571854211E-186,"parent":null,"text":".\x7C)!av<FL","tags":[],"pinned":true,"version":768686},"stamps":[5527782883339088163,null,292194… (750 bytes)` | byte-insert |
| 16 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'tail'. | `{ "name" : "A\xD5\x9C<g\xF0\xB0\x80\x80" , "nick":"Ig=r[z","count":-1318505453 ,"limit" : 1 , "id": -44493 ,"stamp" :1149327335554241185, "ratio":2.382567013877… (979 bytes)` | bad-utf8-name |
| 3 | Note | x=ok \| y=JsonException: The JSON value could not be converted to Bench.Note. | ` \x0A{"tags": \x0A [],\x09\x0A\x09\x0D"score"\x0A:9.9999999999999995E-08, \x0A \x0A"pinned":true,"PARENT":[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[… (307 bytes)` | none rename:upper deep-wrap:array:62 leading-trailing-whitespace |
| 1 | Wide | x=ok \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | ` {"\u006e\u0061\u006D\u0065" : "\u0018\ud87f\uDFFF\udb40\udc01\u8928\u0051\u005B\u0049\u0039\u0404\uD880\udc00\u0079\u0079\uD87F\uDFFF\u0058\uD87F\udfff\uDB40\u… (7,453 bytes)` | duplicate-property rename:pascal |

### A~B X-nonjson: 6,248 cases in 97 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 716 | string? | x=InvalidOperationException: Cannot transcode invalid UTF-# JSON text to UTF-# string. \| y=JsonException: The JSON value could not be converted to System.String. | `"\x8A"` | string-text none bit-flip |
| 439 | string? | x=InvalidOperationException: Cannot read invalid UTF-# JSON text as string. Invalid surrogate value: '?'. \| y=JsonException: The JSON value could not be converted to System.String. | `"\udc00"` | none none string-text |
| 423 | Note | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"version":1e999,"parent":null,"pinned":true,"score":5.7010342894961975E+235,"tags":[]}` | drop-property number-text |
| 371 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"\u0074a\u0067s":[],"sco\u0072e":null,"v\u0065r\u` | omit-null-property null-out truncate |
| 338 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"pinned":   \x0Atrue,"score":\x0D \x09[[0]],"version":FALSE,"text":"*"\x09\x0D\x0D\x0A,"tags"\x0D:[\x09\x0A\x09\x0A]}` | deep-wrap:array:2 omit-null-property literal-text |
| 325 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a boolean. \| y=JsonException: The JSON value could not be converted to System.Boolean. | `{"pinned":null,"score":-42,"version":50667` | null-out null-out null-out truncate |
| 301 | string? | x=InvalidOperationException: Cannot read incomplete UTF-# JSON text as string with missing low surrogate. \| y=JsonException: The JSON value could not be converted to System.String. | `"\ud800"` | string-text none |
| 296 | int | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `[]` | root-1 none |
| 294 | int | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `null` | none null-out none |
| 280 | int | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Int#. | `0e0` | number-text |
| 248 | Wide | x=InvalidOperationException: Cannot get the value of a token type 'StartObject' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"\u0069\u0074\u0065\u006d\u0073":[],"\u0072\u0061\u0074\u0069\u006f":{},\x0A\x0D"\u006c\u0061\u0062\u0065\` | null-out array-extra-item duplicate-property truncate |
| 235 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"version":[null],"text":"","pinned"c:true,"tags":2147483648,"score":3.737883662471759e+75}` | type-swap type-swap omit-null-property byte-insert |
| 232 | int | x=InvalidOperationException: Cannot get the value of a token type 'StartObject' as a number. \| y=JsonException: The JSON value could not be converted to System.Int#. | `{` | root-2 truncate |
| 197 | Note | x=InvalidOperationException: Cannot get the value of a token type 'String' as a number. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"text":"\b\u029C","tags":[],"parent":null,"score":-5e-324,"pinned":false,"version":"null"} ` | none type-swap trailing:  |

### A~B X-unwritable: 67 cases in 7 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 54 | Note | x=ok \| y=ok | `{"text":"y8DpcQ","tags":[".\u0022m?z7hV"],"parent":null,"score":1e999,"pinned":true,"version":348427}` | number-text |
| 5 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'. | `{"\u0074ext":"\u0068e)y","ta\u0067\u0073":[],"s\u0063\u006f\u0072\u0065":1e999,"pinn\u0065\u0064":false,"versi\u006fn":30804}` | number-text null-out omit-null-property |
| 3 | Note | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | ` {"text" : ";\xF0\xAF\xBF\xBF\xF0\xAA\x85\x91\xF3\x99\xAB\x99\xE4\x80\xBCGeq?" , "tags":["wTJ","Ob"] ,"parent":null, "score":1.7976931348623159e308 , "pinned":t… (138 bytes)` | number-text trailing:, |
| 2 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'version'. | `{"tags"\x0A:\x0A[\x09 "","l1JuC",\x09\x0D\x0A "", \x0A \x0A"\x7F\u001c\u06e5\ud9d5\udfad\u0017\u04a6\uDB1C\udc38\u005Cu\\\u002F\u0677\u3438\u02E6\u072F \x60\udb… (339 bytes)` | unknown-property drop-property number-text |
| 1 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'?'version'. | `{"\u0074\u0065\u0078\u0074" :"\u003b\u04F5\u005A\uDA0C\udd2d\u0078\u0074\u200B","\u0074\u0061\u0067\u0073":["\u0024\u0025\u007a\u0029"]\x0D , "\u0073\u0063\u006… (279 bytes)` | null-out drop-property number-text byte-insert |
| 1 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'maybe'. | ` { "name" : ")7=\u200B\ufffe\r\u57C2\u001Ew\ud800\udc00\u000B\uD953\uDEAB\u6F9D\u067e\ud1c8\uDB21\udc8dL","nick": "P40ATq\\lf*N","count" : 65536 ,"limit" : 827 … (1,987 bytes)` | rename:space number-text |
| 1 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'nick'. | ` { "\u006e\u0061\u006D\u0065": "\u0019\uce91\u0057\u00a0\uD840\uDCFC\u7F8E\u0549\u007F\u0000\u007C\u0270\u0073\uDAAC\udc43\u0516\u0068\uac3b\ud93b\uDDE8\u006a\u… (6,128 bytes)` | number-text rename:prefix whitespace-valid |

### A~B RJ: 47,876 cases in 395 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 11,376 | Note | x=JsonException: '?' is an invalid start of a value. \| y=JsonException | `\x00` | drop-property root-7 none whitespace-invalid:\x00 |
| 3,606 | Note[] | x=JsonException: '?' is invalid after a value. Expected either '?', '?', or '?'. \| y=JsonException | `[//c\x0A]` | comment |
| 3,050 | Note | x=JsonException: '?' is an invalid start of a property name. Expected a '?'. \| y=JsonException | `{,` | root-6 root-2 trailing-comma truncate |
| 2,165 | string? | x=JsonException: Expected end of string, but instead reached end of data. \| y=JsonException | `"` | none none truncate |

### F~B NE: 54 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 54 | Note | x=ok \| y=ok | `{"score" :9.559579260804598E+99 , "VERSION" : -9007199254740991,"pinned" : false , "tags":[] , "text":"\uDA6B\uDFB3'" , "parent":null}` | rename:upper |

### F~B XR-YA: 1,477 cases in 20 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 388 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` | null-out none none |
| 269 | Note | x=JsonException: Expected a string but found Null at byte #. \| y=ok | `{"text":"\xF0\xB0\x80\x80\xC7\xBA!","tags":["\/\xEF\xBB\xBF\xF0\x9D\x82\xB4\xEB\xA6\xB3\xF4\x8F\xBF\xBF",null],"parent":null,"score":-5E-324,"pinned":true,"vers… (112 bytes)` | null-out |
| 255 | Note[] | x=JsonException: Expected an array but found Null at byte #. \| y=ok | `null` | null-out |
| 204 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` | root-0 |
| 99 | int | x=JsonException: Expected a number but found String at byte #. \| y=ok | `"1"` | none type-swap |
| 60 | Note | x=JsonException: Note requires 'text'. \| y=ok | `{\x0A\x0D "Text":\x0D\x0D"\uffff\u06d7\u0007",\x0A\x0D\x0A"tags":[]\x0D\x0D,"parent":null\x0A,"score":97e0,"pinned":true,"version":-2990714274680017539}` | rename:pascal |
| 56 | Note | x=JsonException: The number at byte # is not a valid Double. \| y=ok | `{"text":"y8DpcQ","tags":[".\u0022m?z7hV"],"parent":null,"score":1e999,"pinned":true,"version":348427}` | number-text |
| 53 | Note | x=JsonException: Note requires 'tags'. \| y=ok | `{"text":"\uDBFF\uDFFF","Tags":[],"parent":null,"score":1.1922552125689468E+271,"pinned":false,"version":-8595313678139402908}` | string-text rename:pascal |
| 40 | Note | x=JsonException: Note requires 'pinned'. \| y=ok | `{"score"\x0A \x0A:-3.4349711611009896E-118,"text":\x0A"","PINNED":false,"tags":\x0A\x09[]\x0A ,\x09\x0A"version":-9195399437841943760,"parent":null}` | rename:upper |
| 33 | Note | x=JsonException: Note requires 'score'. \| y=ok | `{"text":"M\t\xC5\x83\xF0\xB7\x91\x93",\x0A"tags":["{L8]V#M>}\"4D"],"parent":null,"Score":3.8328538480399998889e+002,"pinned"\x0A:\x09true\x09,\x09\x09"version"\x09… (150 bytes)` | rename:pascal |
| 4 | Wide | x=JsonException: Wide requires 'ratio'. \| y=ok | `{"n\u0061m\u0065":"\f\xF3\xA0\x80\x81\uDB5B\udd1a\ufffd\ud880\udc00","ni\u0063k":"\u003c&\xF4\x8F\xBF\xBF\f\xF0\x90\x80\x80","coun\u0074":493695903,"\u006cimi\u… (1,321 bytes)` | rename:upper rename:pascal |
| 3 | Wide | x=JsonException: Wide requires 'stamps'. \| y=ok | `{ "name": "h>T]:We+zzH)","nick" : null, "count":947,"limit":null,"id": -9223372036854775807 ,"stamp": 8180379545193225411, "ratio": -3.441005770484305E+158, "we… (2,939 bytes)` | rename:pascal |
| 3 | Wide | x=JsonException: Wide requires 'id'. \| y=ok | `{"name":"}p7Kp&])"\x09,"nick":null\x0D\x09\x0A\x09,\x09\x0A\x0D"count":255,"limit":1,"ID":9007199254740992, \x0D "stamp":null,"ratio":-4.588451476956941E+217,\x0A… (1,145 bytes)` | rename:upper |
| 2 | Wide | x=JsonException: Wide requires 'head'. \| y=ok | `{"items":[{"version":null,"tags":["","$Eq\xF1\xAE\x93\x9E\u0012L~\xC9\xB0\u005c/\xC6\xA7"],"parent":null,"text":"22s","score":790.3590513,"pinned":false},{"tags… (3,573 bytes)` | rename:pascal |

### F~B XA-YR: 2,103 cases in 15 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 1,334 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'. | `{"text":"NaN","tags":[],"score":1e-300,"pinned":true,"version":-485175}` | omit-null-property type-swap |
| 505 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'version'. | `{"parent":null,"score":702.19577742,"tags":[],"pinned":false,"text":"\xCA\x9Fb"}` | reorder drop-property |
| 45 | Note | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Note'?'parent'?'version'. | `{"text":"eI;N2r}","score":670.08,"versio":-967276,"tags":[],"pinned":true}` | rename:prefix omit-null-property |
| 35 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'limit'. | `{"words":\x0D\x0A[],"on":\x0D\x0A\x09\x0Dtrue,\x0D"name":"\ufffd>\uda44\uDFFF","children"\x0A\x0A:null,"items":[\x09\x0D\x0D\x0D],"nick":"R","stamp":-4812880557… (714 bytes)` | array-extra-item omit-null-property null-out |
| 34 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'nick'. | ` { "maybe" :true , "ratio":-6.653126284780044E+72 , "limit":null ,"stamp": -611500899982299374,"id":-2356553731790752831 , "labels" : null ,"words": [ ], "stamp… (651 bytes)` | omit-null-property reorder |
| 34 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'stamp'. | ` \x0A{"maybe" \x0D:null  ,\x0D \x0A\x0D"on":false,"head":{"parent":null,\x0A\x09\x09"tags"\x0D\x09\x09:[ \x0D\x0A\x0D],"pinned":true\x0D  ,\x0D"score":-38,\x0D\x09… (917 bytes)` | duplicate-property rename:space leading-trailing-whitespace |
| 27 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'weight'. | `{"name":"Gc=6sK'uiOa-", "nick": "^u\\amZQ\x7CIOA&\/N","count" : -247,"limit":-781 ,"id": 4904803613736290570 ,"stamp" :null , "ratio":12345.678 ,"\x80weight" :1… (757 bytes)` | bad-utf8-name |
| 27 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'children'. | `\x09\x0A\x09\x0A{"labels":["","\xD2\xA8\xF1\x9E\xBA\xAB","\xF3\xA7\x8D\xB3",">\xC2\xADN\xC2\x85\xD7\x8D\xC2\xA0\xE5\x90\x98~\xE4\xA7\xAE-\xF0\xB0\x80\x80\xF3\xA0… (931 bytes)` | bad-utf8-name |
| 21 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'labels'. | `{ "name":"\xF3\xB0\x80\x80", "nick" : null,"count":321091674,"limit" :null, "id":-902779 ,"stamp" :-456382, "ratio": 679.5257184093 ,"weight" :5E-324 , "on": fa… (644 bytes)` | byte-insert |
| 20 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'maybe'. | `{"head":{"score":-1.688494571854211E-186,"parent":null,"text":".\x7C)!av<FL","tags":[],"pinned":true,"version":768686},"stamps":[5527782883339088163,null,292194… (750 bytes)` | byte-insert |
| 15 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'tail'. | `{ "name" : "A\xD5\x9C<g\xF0\xB0\x80\x80" , "nick":"Ig=r[z","count":-1318505453 ,"limit" : 1 , "id": -44493 ,"stamp" :1149327335554241185, "ratio":2.382567013877… (979 bytes)` | bad-utf8-name |
| 3 | Note | x=ok \| y=JsonException: The JSON value could not be converted to Bench.Note. | ` \x0A{"tags": \x0A [],\x09\x0A\x09\x0D"score"\x0A:9.9999999999999995E-08, \x0A \x0A"pinned":true,"PARENT":[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[… (307 bytes)` | none rename:upper deep-wrap:array:62 leading-trailing-whitespace |
| 1 | Wide | x=ok \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | ` {"\u006e\u0061\u006D\u0065" : "\u0018\ud87f\uDFFF\udb40\udc01\u8928\u0051\u005B\u0049\u0039\u0404\uD880\udc00\u0079\u0079\uD87F\uDFFF\u0058\uD87F\udfff\uDB40\u… (7,453 bytes)` | duplicate-property rename:pascal |
| 1 | Wide | x=ok \| y=JsonException: JSON deserialization for type 'Bench.Wide'?'limit'?'stamp'. | `{"\u0063\u0068\u0069\u006C\u0064\u0072\u0065\u006E":[null],"\u0069\u0074\u0065\u006d\u0073":[],"\u0068\u0065\u0061\u0064":{"\u0073\u0063\u006F\u0072\u0065":471.… (2,225 bytes)` | omit-null-property rename:empty bad-utf8-name |

### F~B RJ: 56,585 cases in 411 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 11,364 | Note | x=JsonException: '?' is an invalid start of a value. \| y=JsonException | `\x00` | drop-property root-7 none whitespace-invalid:\x00 |
| 3,603 | Note[] | x=JsonException: '?' is invalid after a value. Expected either '?', '?', or '?'. \| y=JsonException | `[//c\x0A]` | comment |
| 3,040 | Note | x=JsonException: '?' is an invalid start of a property name. Expected a '?'. \| y=JsonException | `{,` | root-6 root-2 trailing-comma truncate |
| 2,547 | int | x=JsonException: '?' is invalid after a single JSON value. Expected end of data. \| y=JsonException | `0]` | type-swap none none trailing:] |

### A~S XR-YA: 1,176 cases in 4 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 394 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` | null-out none none |
| 320 | Note | x=JsonException: Expected a string but found Null. \| y=ok | `{"text":"\xF0\xB0\x80\x80\xC7\xBA!","tags":["\/\xEF\xBB\xBF\xF0\x9D\x82\xB4\xEB\xA6\xB3\xF4\x8F\xBF\xBF",null],"parent":null,"score":-5E-324,"pinned":true,"vers… (112 bytes)` | null-out |
| 258 | Note[] | x=JsonException: Expected an array but found Null. \| y=ok | `null` | null-out |
| 204 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` | root-0 |

### A~S XA-YR: 2,547 cases in 1 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 2,547 | int | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | `0]` | type-swap none none trailing:] |

### A~S X-nonjson: 6,248 cases in 36 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 724 | string? | x=InvalidOperationException: Cannot transcode invalid UTF-# JSON text to UTF-# string. \| y=JsonException: The JSON value could not be converted to System.String. | `"\x8A"` | string-text none bit-flip |
| 443 | string? | x=InvalidOperationException: Cannot read invalid UTF-# JSON text as string. Invalid surrogate value: '?'. \| y=JsonException: The JSON value could not be converted to System.String. | `"\udc00"` | none none string-text |
| 436 | Note | x=FormatException: Either the JSON value is not in a supported format, or is out of bounds for the Int# type. \| y=JsonException: The JSON value could not be converted to System.Nullable'#[System.Int#]. | `{"version":1e999,"parent":null,"pinned":true,"score":5.7010342894961975E+235,"tags":[]}` | drop-property number-text |
| 383 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"\u0074a\u0067s":[],"sco\u0072e":null,"v\u0065r\u` | omit-null-property null-out truncate |
| 345 | Note | x=InvalidOperationException: Cannot get the value of a token type 'StartArray' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"pinned":   \x0Atrue,"score":\x0D \x09[[0]],"version":FALSE,"text":"*"\x09\x0D\x0D\x0A,"tags"\x0D:[\x09\x0A\x09\x0A]}` | deep-wrap:array:2 omit-null-property literal-text |
| 333 | Note | x=InvalidOperationException: Cannot get the value of a token type 'Null' as a boolean. \| y=JsonException: The JSON value could not be converted to System.Boolean. | `{"pinned":null,"score":-42,"version":50667` | null-out null-out null-out truncate |
| 307 | string? | x=InvalidOperationException: Cannot read incomplete UTF-# JSON text as string with missing low surrogate. \| y=JsonException: The JSON value could not be converted to System.String. | `"\ud800"` | string-text none |
| 299 | Note | x=InvalidOperationException: Cannot get the value of a token type 'String' as a number. \| y=JsonException: The JSON value could not be converted to System.Double. | `{"score":"null"\x0A,\x09"version":1.5\x0A\x0A,\x0D\x0D"text":"\uDBFF\udfff","parent":null,"tags":[],"pinned":true}` | type-swap reorder type-swap |

### A~S X-unwritable: 67 cases in 2 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 64 | Note | x=ok \| y=ok | `{"text":"y8DpcQ","tags":[".\u0022m?z7hV"],"parent":null,"score":1e999,"pinned":true,"version":348427}` | number-text |
| 3 | Note | x=ok \| y=JsonException: '?' is invalid after a single JSON value. Expected end of data. | ` {"text" : ";\xF0\xAF\xBF\xBF\xF0\xAA\x85\x91\xF3\x99\xAB\x99\xE4\x80\xBCGeq?" , "tags":["wTJ","Ob"] ,"parent":null, "score":1.7976931348623159e308 , "pinned":t… (138 bytes)` | number-text trailing:, |

### F~S XR-YA: 1,240 cases in 5 groups

| Count | Target | Key | Example (minimised) | Ops of the representative |
|---:|---|---|---|---|
| 393 | Note | x=JsonException: Expected an object for Note but found Null. \| y=ok | `null` | null-out none none |
| 320 | Note | x=JsonException: Expected a string but found Null at byte #. \| y=ok | `{"text":"\xF0\xB0\x80\x80\xC7\xBA!","tags":["\/\xEF\xBB\xBF\xF0\x9D\x82\xB4\xEB\xA6\xB3\xF4\x8F\xBF\xBF",null],"parent":null,"score":-5E-324,"pinned":true,"vers… (112 bytes)` | null-out |
| 258 | Note[] | x=JsonException: Expected an array but found Null at byte #. \| y=ok | `null` | null-out |
| 204 | Wide | x=JsonException: Expected an object for Wide but found Null. \| y=ok | `null` | root-0 |
| 65 | Note | x=JsonException: The number at byte # is not a valid Double. \| y=ok | `{"text":"y8DpcQ","tags":[".\u0022m?z7hV"],"parent":null,"score":1e999,"pinned":true,"version":348427}` | number-text |

## Writer and round-trip checks (value stream)

| Class | Count | Key | Representative |
|---|---:|---|---|
| FAIL | 561 | readA(writeA(x)) != x; readA(writeA2(x)) != x; readF(writeF(x)) != x; readF(writeF2(x)) != x; readB(writeA(x)) != x | case 49851 string? value:invalid: A wrote "\uFFFD" ; B wrote "\uFFFD" |
| FAIL | 153 | A writes / B throws InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object ; A2 writes / B throws InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the … | case 6930 Note value:invalid: A wrote {"text":"d","tags":[],"parent":{"text":"d","tags":[],"parent":{"text":"d","tags":[],"parent":{"text":"d","tags":[],"pare… (4,612 bytes) ; B threw InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  |
| PASS | 21,724 | - | case 191 int value:  |
| PASS-both-throw | 1,465 | B throws ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work whe | case 21 Wide value:invalid: A threw ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work whe ; B threw ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. To make it work whe |
| PASS-both-throw | 97 | B throws InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  | case 6340 Note value:invalid: A threw InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  ; B threw InvalidOperationException: CurrentDepth (#) is equal to or larger than the maximum allowed depth of #. Cannot write the next JSON object  |

## JavaScript JSON.parse checks (value stream)

| Class | Count | Key | Representative |
|---|---:|---|---|
| FAIL | 12,628 | integer above 2^53 rounded by JSON.parse | case 32630 Note value: A wrote {"text":"","tags":[],"parent":null,"score":92,"pinned":true,"version":-9007199254740993} ; B wrote {"text":"","tags":[],"parent":null,"score":92,"pinned":true,"version":-9007199254740993} |
| PASS | 9,810 | - | case 191 int value:  |
