## Item 4: null and undefined
- JSON.stringify(undefined) -> undefined (typeof undefined)
- JSON.stringify(null) -> null
- JSON.stringify({ a: undefined, b: null }) -> {"b":null}
- JSON.stringify([undefined, null]) -> [null,null]
- JSON.parse('{"ok":true}').value -> undefined
- 'value' in JSON.parse('{"ok":true}') -> false
- 'value' in JSON.parse('{"ok":true,"value":null}') -> true
- JSON.parse('') -> THROWS SyntaxError: Unexpected end of JSON input
- decode voidOk as void -> {}
- decode nullableNull as nullable -> {"value":null}
- decode nullableValue as nullable -> {"value":"x"}
- decode resultNullableNull as nullable -> {"value":null}
- decode resultError as nullable -> {"error":{"code":"notFound"}}
- decode nullableNull as value -> THROWS Error: protocol: null for a non-nullable result
- decode nullableNull as void -> THROWS Error: protocol: void command returned a value

## Item 5: duplicate properties in JSON.parse
- JSON.parse('{"a":1,"a":2}').a -> 2
- JSON.parse('{"a":1,"\\u0061":2}').a -> 2
- JSON.parse('{"__proto__":{"x":1}}'): own key, prototype untouched -> own true, inherited x undefined
- Object.assign({}, that).x (copy with [[Set]]) -> 1
- { ...that }.x (spread defines own) -> undefined

## Item 6: the C# dispatch prototype's responses, as a client prototype classifies them
- ok: success "fine"
- NaN: failure kind=response-serialization, execution=completed -> ResponseSerializationError (never retried)
- depth-65: failure kind=response-serialization, execution=completed -> ResponseSerializationError (never retried)
- lone-surrogate: success "a�"
- user-getter-throws: failure kind=response-serialization, execution=completed -> ResponseSerializationError (never retried)
- invalid-UTF-8-bytes: success "�"
- JSON.parse of the 278,560 bytes an unbuffered writer had flushed -> THROWS SyntaxError: Expected ',' or ']' after array element in JSON at position 278560 (line 1 column 278561)
