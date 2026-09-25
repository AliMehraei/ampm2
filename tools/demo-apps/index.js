// Demo service for README screenshots: crashes on start so pm2 shows it as errored.
console.log('mailer starting, connecting to smtp.example.com:587');
setTimeout(() => {
  console.error(new Error('connect ECONNREFUSED 10.0.0.25:587 (SMTP relay unavailable)').stack);
  process.exit(1);
}, 700);
