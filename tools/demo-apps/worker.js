// Demo service for README screenshots: a queue worker that burns a little CPU per job.
const hold = [];
for (let i = 0; i < 24; i++) hold.push(Buffer.alloc(1024 * 1024, i));
const kinds = ['send-invoice', 'sync-inventory', 'resize-avatar', 'webhook-delivery', 'reindex-search'];
let job = 5100;
setInterval(() => {
  const end = Date.now() + 45;
  let x = 0;
  while (Date.now() < end) x += Math.sqrt(x + 1);
  job++;
  if (job % 3 === 0) console.log(`job #${job} ${kinds[job % kinds.length]} done in ${40 + (job % 11) * 7}ms`);
}, 300);
