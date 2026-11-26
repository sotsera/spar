#!/usr/bin/env bash
source "$EVAL_LIB"
replace Pantry/Greetings/GreetingService.cs '        var strict = Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        if (strict.GetByteCount(trimmed) > 50)' '        if (trimmed.Length > 50)'
replace Pantry/Greetings/GreetingService.cs 'using System.Text;
using Spar;' 'using Spar;'
