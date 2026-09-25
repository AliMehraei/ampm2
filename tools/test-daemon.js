// Runs a REAL pm2 daemon (your installed pm2) on private test pipes, with a few sample apps,
// so ampm2 can be tested without touching the daemon on \\.\pipe\rpc.sock.
//
//   node tools\test-daemon.js            (Ctrl+C or create %TEMP%\ampm2-test-pm2\stop to end)
//   set AMPM2_RPC_PIPE=ampm2-test-rpc.sock & set AMPM2_PUB_PIPE=ampm2-test-pub.sock & ampm2.exe
//
// On Windows pm2 hard-codes \\.\pipe\rpc.sock, so this patches the shared constants object
// before the daemon and the API client read it.
'use strict';
const path = require('path');
const fs = require('fs');
const os = require('os');
const { execSync } = require('child_process');

const ROOT = process.env.PM2_ROOT || path.join(execSync('npm root -g').toString().trim(), 'pm2');
// AMPM2_TEST_HOME: pm2 home for this daemon. AMPM2_DEMO_DIR: demo mode (realistic sample services copied
// into that folder, used for the README screenshots: tools\screenshots.ps1).
const HOME = process.env.AMPM2_TEST_HOME || path.join(os.tmpdir(), 'ampm2-test-pm2');
const DEMO = process.env.AMPM2_DEMO_DIR || '';
fs.mkdirSync(HOME, { recursive: true });
try { fs.unlinkSync(path.join(HOME, 'stop')); } catch {}
process.env.PM2_HOME = HOME;

const cst = require(path.join(ROOT, 'constants.js'));
cst.DAEMON_RPC_PORT = '\\\\.\\pipe\\ampm2-test-rpc.sock';
cst.DAEMON_PUB_PORT = '\\\\.\\pipe\\ampm2-test-pub.sock';
cst.INTERACTOR_RPC_PORT = '\\\\.\\pipe\\ampm2-test-interactor.sock';

const Daemon = require(path.join(ROOT, 'lib', 'Daemon.js'));
const daemon = new Daemon({
  rpc_socket_file: cst.DAEMON_RPC_PORT,
  pub_socket_file: cst.DAEMON_PUB_PORT,
  pid_file: path.join(HOME, 'pm2.pid'),
  ignore_signals: true,
});
daemon.start();

const APPS = path.join(__dirname, 'test-apps');
// Connect only once our pipe exists: if the API client failed to ping it would spawn a default
// daemon on \\.\pipe\rpc.sock, which is exactly the stray-daemon problem ampm2 cleans up.
const waitForPipe = (cb, tries = 0) => {
  const ok = fs.readdirSync('\\\\.\\pipe\\').includes('ampm2-test-rpc.sock');
  if (ok) return cb();
  if (tries > 50) { console.error('test daemon pipe never appeared'); process.exit(1); }
  setTimeout(() => waitForPipe(cb, tries + 1), 200);
};
waitForPipe(() => {
  const PM2 = require(ROOT);
  const pm2 = new PM2.custom({});
  pm2.connect((err) => {
    if (err) { console.error('connect', err); process.exit(1); }
    let apps = [
      { name: 'api', script: path.join(APPS, 'api.js'), cwd: APPS, env: { PORT: '39871' } },
      { name: 'worker', script: path.join(APPS, 'worker.js'), cwd: APPS, exec_mode: 'cluster', instances: 2 },
      { name: 'crasher', script: path.join(APPS, 'crasher.js'), cwd: APPS, min_uptime: 5000, max_restarts: 3 },
      { name: 'idle', script: path.join(APPS, 'api.js'), cwd: APPS, env: { PORT: '39872' }, namespace: 'tools' },
    ];
    let stopAfter = ['idle'];
    if (DEMO) {
      // realistic-looking services, each in its own folder under AMPM2_DEMO_DIR
      const src = path.join(__dirname, 'demo-apps');
      const put = (dir, file) => {
        const d = path.join(DEMO, dir);
        fs.mkdirSync(d, { recursive: true });
        fs.copyFileSync(path.join(src, file), path.join(d, file));
        return d;
      };
      const d = {
        api: put('shop-api', 'server.js'), web: put('storefront', 'server.js'), jobs: put('jobs', 'worker.js'),
        mail: put('mailer', 'index.js'), rep: put('reports', 'nightly.js'), img: put('image-resizer', 'server.js'),
      };
      apps = [
        { name: 'shop-api', script: path.join(d.api, 'server.js'), cwd: d.api, env: { PORT: '39881', ROLE: 'api' }, max_memory_restart: '500M' },
        { name: 'storefront', script: path.join(d.web, 'server.js'), cwd: d.web, env: { PORT: '39882', ROLE: 'web' } },
        { name: 'jobs', script: path.join(d.jobs, 'worker.js'), cwd: d.jobs, exec_mode: 'cluster', instances: 2 },
        { name: 'image-resizer', script: path.join(d.img, 'server.js'), cwd: d.img, env: { PORT: '39883', ROLE: 'images' }, namespace: 'media' },
        { name: 'mailer', script: path.join(d.mail, 'index.js'), cwd: d.mail, min_uptime: 5000, max_restarts: 3 },
        { name: 'reports', script: path.join(d.rep, 'nightly.js'), cwd: d.rep, cron_restart: '0 2 * * *' },
      ];
      stopAfter = ['reports'];
    }
    let n = 0;
    for (const a of apps) pm2.start(a, (e) => {
      if (e) console.error('start', a.name, e.message || e);
      if (++n === apps.length) {
        pm2.stop(stopAfter[0], () => console.log('READY test daemon on', cst.DAEMON_RPC_PORT));
      }
    });
    const stop = () => pm2.delete('all', () => { console.log('stopped'); process.exit(0); });
    process.on('SIGINT', stop);
    setInterval(() => { if (fs.existsSync(path.join(HOME, 'stop'))) stop(); }, 1000);
  });
});
