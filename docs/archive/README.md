# Earlier marketing sets

Past headlines, examples and images, kept so we can reuse them. The live set is in `docs/img/` and `site/assets/`, rendered by `tools/shots.mjs`.

Each folder holds the rendered images (`hero.png`, `og.png`, the panel shots and, from set 2 on, the animated `demo.webp`) and the sources that made them (`hero.html`, `og.html`, `scenes.html`, and `mock-host.js` with the demo draft). To re-render a set, copy its sources back over `docs/` and `ui/mock-host.js`, then run `node tools/shots.mjs`. The full website of each set is in git at the commit listed.

| Folder | Headline | Example | Commit |
| --- | --- | --- | --- |
| `1-whole-picture` | Give your AI the whole picture. | Debugging a Colab notebook (made-up panel images). Version 1.3. | `6662012` |
| `2-show-everything` | Show your AI everything. | Fixing a sign-in button on a phone layout. Dropped: people read it as a phone app. | `634cdfb` |
| `3-ten-screenshots` | Ten screenshots. One paste. | Picking between two laptops. Dropped: people just ask the AI for a laptop, nobody screenshots shop pages, and the headline made Chipmunk sound like a screenshot clipboard. The dark panel shot in this set came out empty. | `d12804a` |

The current set, from November 2026, is back on "Give your AI the whole picture." with a bakery website example. Why is in `docs/PRODUCT.md` under "How we pitch it".
