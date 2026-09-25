#!/usr/bin/env bash
# Tests ampm2's Unix code path (Unix sockets, peer pid, ps metrics, login-shell PATH) against a REAL
# pm2 daemon on Linux (WSL). Everything lives in ~/.ampm2-test; `wsl-test.sh clean` removes it.
#   wsl bash /mnt/d/Projects/ampm2/tools/wsl-test.sh [clean]
set -euo pipefail
T="$HOME/.ampm2-test"
if [ "${1:-}" = "clean" ]; then
  [ -x "$T/node/bin/node" ] && PM2_HOME="$T/pm2home" PATH="$T/node/bin:$T/bin:$PATH" pm2 kill >/dev/null 2>&1 || true
  rm -rf "$T"; echo "removed $T"; exit 0
fi
mkdir -p "$T"
cd "$T"
C=/mnt/d/Projects/ampm2/.wsl-cache   # downloaded on Windows (WSL may have no internet)
if [ ! -x node/bin/node ]; then mkdir -p node && tar -xJf "$C/node.tar.xz" -C node --strip-components=1; fi
export PATH="$T/node/bin:$T/bin:$PATH"
PM2_PKG="${PM2_PKG:-$(ls -d /mnt/c/nvm/v*/node_modules/pm2 "/mnt/c/Program Files/nodejs/node_modules/pm2" "/mnt/c/Users/$USER/AppData/Roaming/npm/node_modules/pm2" 2>/dev/null | head -1 || true)}"   # your Windows pm2 package (ls fails on the paths that do not exist)
if [ ! -x pm2/bin/pm2 ]; then cp -r "$PM2_PKG" "$T/pm2"; chmod +x "$T/pm2/bin/"*; fi   # pm2 is plain JS
mkdir -p "$T/bin"; ln -sf "$T/pm2/bin/pm2" "$T/bin/pm2"
if [ ! -x dotnet/dotnet ]; then mkdir -p dotnet && tar -xzf "$C/dotnet.tar.gz" -C dotnet; fi
export PM2_HOME="$T/pm2home"
APPS=/mnt/d/Projects/ampm2/tools/test-apps
cat > "$T/ecosystem.json" <<JSON
{ "apps": [
  { "name": "api", "script": "$APPS/api.js", "cwd": "$APPS", "env": { "PORT": "39871" } },
  { "name": "worker", "script": "$APPS/worker.js", "cwd": "$APPS", "exec_mode": "cluster", "instances": 2 },
  { "name": "crasher", "script": "$APPS/crasher.js", "cwd": "$APPS", "min_uptime": 5000, "max_restarts": 3 },
  { "name": "idle", "script": "$APPS/api.js", "cwd": "$APPS", "env": { "PORT": "39872" }, "namespace": "tools" }
] }
JSON
pm2 delete all >/dev/null 2>&1 || true
pm2 start "$T/ecosystem.json" >/dev/null
pm2 stop idle >/dev/null
sleep 5
echo "pm2 $(pm2 -v) running in $PM2_HOME: $(ls "$PM2_HOME" | tr '\n' ' ')"
AMPM2_SELFTEST_ACTIONS=1 AMPM2_PROFILE=wsltest "$T/dotnet/dotnet" /mnt/d/Projects/ampm2/publish-linux-test/ampm2.dll --selftest "$T/report.txt" || true
cat "$T/report.txt"

# in-app update (macOS path): the self-test stages a swap of a fake ampm2.app; check it once the process has exited
S="$T/swap"; rm -rf "$S"; mkdir -p "$S"
AMPM2_SELFTEST_SWAP="$S" AMPM2_PROFILE=wsltest "$T/dotnet/dotnet" /mnt/d/Projects/ampm2/publish-linux-test/ampm2.dll --selftest "$T/swap-report.txt" >/dev/null 2>&1 || true
grep 'mac bundle' "$T/swap-report.txt" || echo "FAIL  mac bundle swap: no report"
for i in $(seq 1 50); do [ -f "$S/relaunched" ] && break; sleep 0.2; done
A="$S/Applications"
if grep -q 'new build' "$A/ampm2.app/Contents/MacOS/ampm2" 2>/dev/null && [ "$(cat "$S/relaunched" 2>/dev/null)" = "--updated" ] \
   && [ -z "$(ls -A "$A" | grep -v '^ampm2.app$')" ]; then
  echo "PASS  mac bundle swapped after exit, reopened with --updated, staging removed"
else
  echo "FAIL  mac bundle swap: $(ls -A "$A" | tr '\n' ' ') relaunched=$(cat "$S/relaunched" 2>/dev/null)"
fi
rm -rf "$S"
