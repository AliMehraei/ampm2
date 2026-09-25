// The real pm2 CLI, pointed at the test daemon's pipes (see test-daemon.js).
// ampm2 uses it when AMPM2_PM2 points to tools\test-pm2.cmd, so Add / Save / Flush can be
// tested end to end without touching the daemon on \\.\pipe\rpc.sock.
'use strict';
const path = require('path');
const os = require('os');
const fs = require('fs');
const { execSync } = require('child_process');

const ROOT = process.env.PM2_ROOT || path.join(execSync('npm root -g').toString().trim(), 'pm2');
process.env.PM2_HOME = process.env.AMPM2_TEST_HOME || path.join(os.tmpdir(), 'ampm2-test-pm2');
const cst = require(path.join(ROOT, 'constants.js'));
cst.DAEMON_RPC_PORT = '\\\\.\\pipe\\ampm2-test-rpc.sock';
cst.DAEMON_PUB_PORT = '\\\\.\\pipe\\ampm2-test-pub.sock';
cst.INTERACTOR_RPC_PORT = '\\\\.\\pipe\\ampm2-test-interactor.sock';
if (!fs.readdirSync('\\\\.\\pipe\\').includes('ampm2-test-rpc.sock')) {
  console.error('[test-pm2] test daemon is not running; refusing so no daemon gets spawned');
  process.exit(2);
}
process.argv = [process.argv[0], path.join(ROOT, 'bin', 'pm2'), ...process.argv.slice(2)];
require(path.join(ROOT, 'lib', 'binaries', 'CLI.js'));
