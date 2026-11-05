const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.join(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8').replace(/^\uFEFF/, '');
const version = read('version.txt').trim();
assert.match(version, /^\d+\.\d+\.\d+$/);
assert.equal(JSON.parse(read('package.json')).version, version);
const lock = JSON.parse(read('package-lock.json'));
assert.equal(lock.version, version);
assert.equal(lock.packages[''].version, version);
const raw = read('src/dictionary.json');
const dictionary = JSON.parse(raw);
const keys = [...raw.matchAll(/^\s*("(?:[^"\\]|\\.)*")\s*:/gm)].map(match => JSON.parse(match[1]));
assert.equal(new Set(keys).size, keys.length, 'Dictionary keys must be unique');
assert.ok(keys.length > 0);
for (const [original, translated] of Object.entries(dictionary)) {
  assert.ok(original.trim() && typeof translated === 'string' && translated.trim());
}
for (const file of ['LICENSE', 'SECURITY.md', 'CONTRIBUTING.md', 'CODE_OF_CONDUCT.md', 'CHANGELOG.md', 'docs/assets/banner.svg']) {
  assert.ok(fs.existsSync(path.join(root, file)), `Missing project file: ${file}`);
}
assert.match(read('LICENSE'), /MIT License/);
for (const match of read('README.md').matchAll(/\]\(([^)#]+)(?:#[^)]*)?\)/g)) {
  if (!/^https?:/.test(match[1])) assert.ok(fs.existsSync(path.join(root, decodeURIComponent(match[1]))), `Broken local link: ${match[1]}`);
}
console.log(JSON.stringify({projectChecks: 'passed', version, dictionaryEntries: keys.length}));
