@echo off
echo [batch] running the app directly
"C:\spar\in\153233\app.exe" --ui none --min-runtime 999.0.0.0 --log "C:\spar\out\console\direct.jsonl"
echo [batch] direct errorlevel=%errorlevel%
echo direct=%errorlevel% > "C:\spar\out\console\cmd-exit.txt.tmp"
start "" /wait "C:\spar\in\153233\app.exe" --ui none --min-runtime 999.0.0.0 --log "C:\spar\out\console\wait.jsonl"
echo [batch] start /wait errorlevel=%errorlevel%
echo wait=%errorlevel% >> "C:\spar\out\console\cmd-exit.txt.tmp"
move /y "C:\spar\out\console\cmd-exit.txt.tmp" "C:\spar\out\console\cmd-exit.txt" > nul
