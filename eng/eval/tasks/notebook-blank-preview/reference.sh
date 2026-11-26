#!/usr/bin/env bash
source "$EVAL_LIB"
replace Notebook/frontend/src/components/Preview.tsx '            const language = code!.properties!.className![0]!.replace("language-", "");
            return (' '            const language = code?.properties?.className?.[0]?.replace("language-", "");
            if (language === undefined) {
              return <pre>{children}</pre>;
            }

            return ('
