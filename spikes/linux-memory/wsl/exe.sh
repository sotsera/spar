#!/usr/bin/env bash
# Usage: bash wsl/exe.sh <gtk3|gtk4>   Prints the published executable's path.
if [ "$1" = gtk4 ]; then echo Mem.Linux/bin/gtk4/Release/net11.0/linux-x64/publish/Mem.Linux
else echo Mem.Linux/bin/Release/net11.0/linux-x64/publish/Mem.Linux; fi
