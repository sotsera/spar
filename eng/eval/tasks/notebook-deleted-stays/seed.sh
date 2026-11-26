#!/usr/bin/env bash
# Deleting a page no longer refreshes the page list.
source "$EVAL_LIB"
replace Notebook/frontend/src/notebook.ts '    setCurrent({ kind: "none" });
    await refresh();
  }, [refresh]);' '    setCurrent({ kind: "none" });
  }, []);'
