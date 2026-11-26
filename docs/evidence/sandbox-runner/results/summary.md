| session | server | start | exec-ready | start→exec | connect | minimizes | last-minimize | stop | close | left-after-close | total |
|---|---|---|---|---|---|---|---|---|---|---|---|
| cold-1 | 1 | 1082 | 336 | 1438.1 | 2934 | 3 | 1553.5 | 856 | 22 | 0 | 48155 |
| cold-2 | 1 | 1072 | 324 | 1426.6 | 3063 | 3 | 1557.6 | 832 | 76 | 0 | 49323 |
| cold-3 | 1 | 1213 | 435 | 1682.5 | 2518 | 3 | 1809.9 | 929 | 28 | 0 | 49122 |
| deadline | 1 | 1112 | 384 | 1513.3 | 2524 | 3 | 1672.8 | 925 | 81 | 0 | 14731 |
| prestaged-1 | 1 | 1293 | 389 | 1704.8 | 4466 | 3 | 2095.3 | 960 | 93 | 0 | 51905 |
| prestaged-2 | 1 | 1121 | 352 | 1496.5 | 2843 | 3 | 1524.9 | 868 | 77 | 0 | 48700 |
| prestaged-3 | 1 | 981 | 323 | 1323.3 | 2896 | 3 | 1546.7 | 948 | 29 | 0 | 48646 |
| probe | 1 | 1236 | 465 | 1722.2 | 3173 | 3 | 1999.5 | 822 | 19 | 0 | 20521 |
| reuse | 1 | 1163 | 379 | 1562.8 | 2716 | 3 | 1447.7 | 965 | 33 | 0 | 131959 |
| s01-system |  | 1251 | 338 | 1610.5 | - | - | - | 589 | 21 | 0 | 3786 |
| s02-both |  | 1100 | 338 | 1458.4 | 2230 | 3 | 1556.2 | 833 | 23 | 0 | 66705 |
| s03-both |  | 1099 | 339 | 1464.5 | 3566 | 2 | 1410 | 839 | 21 | 0 | 48276 |
| system-only-1 | 1 | 1023 | 334 | 1376.6 | - | - | - | 563 | 12 | 0 | 3519 |
| system-only-2 | 1 | 1002 | 387 | 1406.9 | - | - | - | 603 | 14 | 0 | 3591 |
| system-only-3 | 1 | 1069 | 341 | 1434.2 | - | - | - | 559 | 17 | 0 | 3549 |
| system-window | 1 | 1085 | 372 | 1476.3 | - | - | - | 651 | 17 | 0 | 4188 |
| two |  |  |  |  | - | - | - | 563 |  |  |  |

| session | id | as | copy | visible | visible-probes | run | exit | timedOut | collect | exit-file | tests |
|---|---|---|---|---|---|---|---|---|---|---|---|
| cold-1 | j01-c4cb62 | System | 13 | 554 | 1 | 1123 | 0 | false | 15 | 0 | 79/79 passed, 0 failed |
| cold-1 | j02-67c764 | ExistingLogin | 9 | 338 | 1 | 40543 | 0 | false | 12 | 0 | 15/15 passed, 0 failed |
| cold-2 | j01-3e1693 | System | 24 | 373 | 1 | 1118 | 0 | false | 26 | 0 | 79/79 passed, 0 failed |
| cold-2 | j02-991264 | ExistingLogin | 9 | 416 | 1 | 41654 | 0 | false | 15 | 0 | 15/15 passed, 0 failed |
| cold-3 | j01-973d6a | System | 9 | 495 | 1 | 1099 | 0 | false | 17 | 0 | 79/79 passed, 0 failed |
| cold-3 | j02-fd08e6 | ExistingLogin | 11 | 519 | 1 | 41430 | 0 | false | 12 | 0 | 15/15 passed, 0 failed |
| deadline | j01-d55bfb | ExistingLogin | 10 | 561 | 1 | 8056 |  | true | 244 | 1 | no trx |
| prestaged-1 | j01-8faec1 | System | 6 | 742 | 1 | 1525 | 0 | false | 24 | 0 | 79/79 passed, 0 failed |
| prestaged-1 | j02-7b7d15 | ExistingLogin | 7 | 600 | 1 | 41412 | 0 | false | 17 | 0 | 15/15 passed, 0 failed |
| prestaged-2 | j01-d1ad5f | System | 7 | 478 | 1 | 1116 | 0 | false | 17 | 0 | 79/79 passed, 0 failed |
| prestaged-2 | j02-f6fd18 | ExistingLogin | 8 | 471 | 1 | 41032 | 0 | false | 13 | 0 | 15/15 passed, 0 failed |
| prestaged-3 | j01-5e05e5 | System | 5 | 355 | 1 | 960 | 0 | false | 18 | 0 | 79/79 passed, 0 failed |
| prestaged-3 | j02-830f8e | ExistingLogin | 6 | 341 | 1 | 41453 | 0 | false | 16 | 0 | 15/15 passed, 0 failed |
| probe | j01-9f4d7a | ExistingLogin | 10 | 548 | 1 | 3840 | 0 | false | 10056 |  | no trx |
| reuse | j01-2e1265 | System | 8 | 420 | 1 | 917 | 1 | false | 17 | 1 | 78/79 passed, 1 failed |
| reuse | j02-919a90 | ExistingLogin | 9 | 393 | 1 | 41256 | 0 | false | 14 | 0 | 15/15 passed, 0 failed |
| reuse | j03-2434fb | System | 7 | 343 | 1 | 779 | 0 | false | 15 | 0 | 79/79 passed, 0 failed |
| reuse | j04-40f373 | ExistingLogin | 9 | 339 | 1 | 39956 | 0 | false | 12 | 0 | 15/15 passed, 0 failed |
| reuse | j05-f84e27 | System | 7 | 389 | 1 | 826 | 0 | false | 13 | 0 | 79/79 passed, 0 failed |
| reuse | j06-dcad15 | ExistingLogin | 6 | 302 | 1 | 40298 | 0 | false | 14 | 0 | 15/15 passed, 0 failed |
| s01-system | j01-5f886a | System | 9 | 323 | 1 | 898 | 1 | false | 24 | ECHO is off. | 78/79 passed, 1 failed |
| s02-both | j01-e00538 | System | 18 | 556 | 1 | 470 | 255 | false | 30017 |  | no trx |
| s02-both | j02-b09fe0 | ExistingLogin | 16 | 332 | 1 | 429 | 255 | false | 30009 |  | no trx |
| s03-both | j01-3ec115 | System | 15 | 522 | 1 | 1076 | 0 | false | 19 | 0 | 79/79 passed, 0 failed |
| s03-both | j02-3ca4d9 | ExistingLogin | 9 | 329 | 1 | 40103 | 0 | false | 11 | 0 | 15/15 passed, 0 failed |
| system-only-1 | j01-972a16 | System | 7 | 344 | 1 | 923 | 0 | false | 14 | 0 | 79/79 passed, 0 failed |
| system-only-2 | j01-f7c66b | System | 6 | 316 | 1 | 936 | 0 | false | 14 | 0 | 79/79 passed, 0 failed |
| system-only-3 | j01-e6bb5a | System | 6 | 319 | 1 | 911 | 0 | false | 13 | 0 | 79/79 passed, 0 failed |
| system-window | j01-ca9c35 | System | 7 | 397 | 1 | 1281 | 1 | false | 12 | 1 | 0/1 passed, 1 failed |
