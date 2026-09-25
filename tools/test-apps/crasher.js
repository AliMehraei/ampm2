// Sample app for ampm2 testing: crashes quickly so pm2 marks it errored.
console.log('crasher starting');
setTimeout(() => { console.error(new Error('ECONNREFUSED 127.0.0.1:5432 (database unavailable)').stack); process.exit(1); }, 800);
