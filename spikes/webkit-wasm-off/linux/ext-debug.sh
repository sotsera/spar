#!/usr/bin/env bash
# Diagnoses why the web process extension does not report: exported symbol, linkage, and a run with
# G_MESSAGES_DEBUG for the extension loader. Usage: bash linux/ext-debug.sh
set -u
work="$HOME/spar-spikes/webkit-wasm-off"
nm -D --defined-only "$work/ext/libwasmoff.so" | grep webkit_
ldd "$work/ext/libwasmoff.so" | grep -E 'webkit|javascriptcore|not found'
cc --version | head -1
