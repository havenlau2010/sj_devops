const { execSync } = require('child_process');
const fs = require('fs');

const run = (cmd) => {
  try {
    return execSync(cmd).toString().trim();
  } catch (e) {
    return 'Not Found';
  }
};

const info = [
  `Node: ${process.version}`,
  `NPM: ${run('npm --version')}`,
  `Yarn: ${run('yarn --version')}`,
  `PNPM: ${run('pnpm --version')}`
].join('\n');

fs.writeFileSync('versions.txt', info);
console.log('Versions written to versions.txt');
