// Sample app for ampm2 testing: burns a little CPU and holds some memory.
const hold = [];
for (let i = 0; i < 40; i++) hold.push(Buffer.alloc(1024 * 1024, i));
setInterval(() => {
  const end = Date.now() + 60;
  let x = 0;
  while (Date.now() < end) x += Math.sqrt(x + 1);
  if (Math.random() < 0.2) console.log(`processed batch, checksum ${x.toFixed(0)}`);
}, 250);
