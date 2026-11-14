# What each encoding looks like in a log

Written by `Bridge.Bench samples` (TAB shown as `→`).

## A

```text
h k3j9x2ab 1
o k3j9x2ab 1
q 1234 bench save 3d1faabdfca06185 00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01 {"title":"Buy milk","body":"Two litres, semi-skimmed","tags":["home","shop"]}
r 1234 {"ok":true,"value":{"id":"1234","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026-11-14T10:20:30.1234567Z"}}
s 1234 bench rows 3d1faabdfca06185 - 1000 8 {"count":100000}
i 1234 0 [{"id":"9007199254740993","name":"row number 0","qty":0,"price":"0.50","createdAt":"2026-11-14T10:20:30.1234567","active":true,"code":"AB-0","ratio":0,"note":"lorem ipsum dolor sit amet","tag":"red"},{"id":"9007199254740994","name":"row number 1","qty":1,"price":"0.51","createdAt":"2026-11-14T10:20:30.1234567","active":false,"code":"AB-1","ratio":0.010309278350515464,"note":"lorem ipsum dolor sit amet","tag":"green"}]
k 1234 4
x 1234
c 1234
r 1234 {"ok":false,"failure":{"kind":"response-serialization","execution":"completed"}}
n 1234 contract-mismatch {"detail":"3d1faabdfca06185"}
```

## B

```text
{"type":"hello","doc":"k3j9x2ab","protocol":1}
{"type":"open","doc":"k3j9x2ab","protocol":1}
{"type":"call","id":1234,"contract":"bench","command":"save","fingerprint":"3d1faabdfca06185","traceparent":"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01","arg":{"title":"Buy milk","body":"Two litres, semi-skimmed","tags":["home","shop"]}}
{"type":"result","id":1234,"ok":true,"value":{"id":"1234","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026-11-14T10:20:30.1234567Z"}}
{"type":"stream","id":1234,"contract":"bench","command":"rows","fingerprint":"3d1faabdfca06185","batch":1000,"credits":8,"arg":{"count":100000}}
{"type":"items","id":1234,"dropped":0,"items":[{"id":"9007199254740993","name":"row number 0","qty":0,"price":"0.50","createdAt":"2026-11-14T10:20:30.1234567","active":true,"code":"AB-0","ratio":0,"note":"lorem ipsum dolor sit amet","tag":"red"},{"id":"9007199254740994","name":"row number 1","qty":1,"price":"0.51","createdAt":"2026-11-14T10:20:30.1234567","active":false,"code":"AB-1","ratio":0.010309278350515464,"note":"lorem ipsum dolor sit amet","tag":"green"}]}
{"type":"credits","id":1234,"credits":4}
{"type":"cancel","id":1234}
{"type":"cancelled","id":1234}
{"type":"result","id":1234,"ok":false,"failure":{"kind":"response-serialization","execution":"completed"}}
{"type":"not-executed","id":1234,"code":"contract-mismatch","detail":"3d1faabdfca06185"}
```

## C

```text
{"type":"hello","doc":"k3j9x2ab","protocol":1}
{"type":"open","doc":"k3j9x2ab","protocol":1}
{"type":"call","id":1234,"contract":"bench","command":"save","fingerprint":"3d1faabdfca06185","traceparent":"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"}→{"title":"Buy milk","body":"Two litres, semi-skimmed","tags":["home","shop"]}
{"type":"result","id":1234}→{"ok":true,"value":{"id":"1234","title":"Buy milk","body":"Two litres, semi-skimmed","createdAt":"2026-11-14T10:20:30.1234567Z"}}
{"type":"stream","id":1234,"contract":"bench","command":"rows","fingerprint":"3d1faabdfca06185","batch":1000,"credits":8}→{"count":100000}
{"type":"items","id":1234,"dropped":0}→[{"id":"9007199254740993","name":"row number 0","qty":0,"price":"0.50","createdAt":"2026-11-14T10:20:30.1234567","active":true,"code":"AB-0","ratio":0,"note":"lorem ipsum dolor sit amet","tag":"red"},{"id":"9007199254740994","name":"row number 1","qty":1,"price":"0.51","createdAt":"2026-11-14T10:20:30.1234567","active":false,"code":"AB-1","ratio":0.010309278350515464,"note":"lorem ipsum dolor sit amet","tag":"green"}]
{"type":"credits","id":1234,"credits":4}
{"type":"cancel","id":1234}
{"type":"cancelled","id":1234}
{"type":"result","id":1234}→{"ok":false,"failure":{"kind":"response-serialization","execution":"completed"}}
{"type":"not-executed","id":1234,"code":"contract-mismatch","detail":"3d1faabdfca06185"}
```

