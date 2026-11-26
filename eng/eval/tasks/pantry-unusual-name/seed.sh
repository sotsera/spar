#!/usr/bin/env bash
# The greeting's length limit counts bytes with an ASCII encoder that throws on any other letter: a name such as José
# fails the command, and the page says the app did not answer.
source "$EVAL_LIB"
replace Pantry/Greetings/GreetingService.cs 'using Spar;' 'using System.Text;
using Spar;'
replace Pantry/Greetings/GreetingService.cs '        var trimmed = name.Trim();' '        var trimmed = name.Trim();

        // The status line has room for a 50-character name.
        var strict = Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        if (strict.GetByteCount(trimmed) > 50)
        {
            trimmed = trimmed[..50];
        }'
