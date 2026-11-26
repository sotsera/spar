#!/usr/bin/env bash
# A Linux x64 NativeAOT executable of Pantry, and its size in bytes in publish-size.txt.
source "$EVAL_LIB"
native() {
    local file
    file=$(find Pantry/bin -type f -name Pantry -path '*linux-x64*' -path '*publish*' | head -1)
    test -n "$file" && [ "$(head -c 4 "$file" | od -An -c | tr -d ' \n')" = '177ELF' ] && ! test -f "$(dirname "$file")/Pantry.dll" && echo "$file"
}
export -f native
check native-executable bash -c 'native'
check size-reported bash -c 'file=$(native) && test "$(tr -d " \r\n" < publish-size.txt)" = "$(stat -c %s "$file")"'
