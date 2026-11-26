#!/usr/bin/env bash
source "$EVAL_LIB"
replace Notebook/frontend/src/notebook.ts '    setCurrent({ kind: "none" });
  }, []);' '    setCurrent({ kind: "none" });
    await refresh();
  }, [refresh]);'
