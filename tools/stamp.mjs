// Adds a fingerprint of each image to its address on the website (/assets/og.png?v=1a2b3c4d), so
// browsers fetch a new image as soon as it changes, while unchanged ones stay cached for a week.
// tools/shots.mjs runs this after rendering; run it yourself after changing anything in site/assets:
//   node tools/stamp.mjs
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const SITE = join(dirname(fileURLToPath(import.meta.url)), '..', 'site');

export function stamp() {
  for (const page of ['index.html', 'styles.css']) {
    const path = join(SITE, page);
    const before = readFileSync(path, 'utf8');
    const after = before.replace(/(\/assets\/)([\w.-]+\.(?:webp|png|jpg|svg|ico))(\?v=[0-9a-f]+)?/g, (all, dir, name) => {
      const file = join(SITE, 'assets', name);
      if (!existsSync(file)) return all;
      return dir + name + '?v=' + createHash('sha1').update(readFileSync(file)).digest('hex').slice(0, 8);
    });
    if (after !== before) { writeFileSync(path, after); console.log('stamped', 'site/' + page); }
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) stamp();
