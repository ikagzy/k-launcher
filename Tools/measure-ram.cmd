@echo off
chcp 65001 >nul
set "LOG=%~dp0ram.log"
echo [%date% %time%] %~1>> "%LOG%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$p = Get-Process KLauncher -ErrorAction SilentlyContinue; if (-not $p) { '  K-Launcher calismiyor' } else { $p | ForEach-Object { '  PID {0}  Calisma kumesi {1:N1} MB  Ozel bellek (commit) {2:N1} MB  Tutamac {3}' -f $_.Id, ($_.WorkingSet64/1MB), ($_.PrivateMemorySize64/1MB), $_.HandleCount } }" >> "%LOG%"
