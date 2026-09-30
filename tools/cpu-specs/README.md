# Processor figures for the checkup

`src/Mazesta.Core/Health/Checkup/cpu-specs.json` holds each listed processor's published figures: base clock, single-core boost, base/turbo
power, core and thread count, and the highest allowed temperature (Tjmax). Every row keeps the URL of the page its figures came from. The table
is built from two files in this folder and nothing else:

- `intel-raw.json` — Intel ARK specification pages (8th gen to Core Ultra, desktop, mobile and embedded), one object per page with the fields as ARK labels them.
- `amd-raw.json` — amd.com product pages (Ryzen desktop). AMD's site blocks fast reading, so it is read a few pages at a time.

Then `pwsh tools/cpu-specs/build.ps1` writes the JSON (it copies figures; it never estimates one).

## Which figure is which
- Intel boost is the **P-cores' Max Turbo Frequency** (or Turbo Boost 2.0 on older parts), not "Max Turbo Frequency" when that is a Turbo Boost
  Max 3.0 / Thermal Velocity Boost figure that only the best cores reach while cool. Base is the P-cores' base.
- Intel power: `pbp` Processor Base Power (TDP on older parts), `mtp` Maximum Turbo Power.
- AMD: "Max. Boost Clock", "Base Clock", "Default TDP", "Max. Operating Temperature (Tjmax)". AMD publishes no turbo power on the product page, so `mtp` is empty.

## Reading Intel ARK again
Open https://www.intel.com/content/www/us/en/ark.html in a browser and run in its console (about 5 minutes; progress in `window.__intel`):

```js
const series = {245528:'ultra-s3',241071:'ultra-s2',236803:'ultra-s1',236143:'14-i9',236170:'14-i7',236175:'14-i5',236176:'14-i3',230485:'13-i9',230486:'13-i7',230487:'13-i5',230488:'13-i3',217839:'12-i9',217837:'12-i7',217838:'12-i5',217840:'12-i3',202984:'11-i9',202986:'11-i7',202985:'11-i5',202987:'11-i3',195735:'10-i9',195734:'10-i7',195732:'10-i5',195733:'10-i3',186673:'9-i9',134907:'9-i7',134902:'9-i5',134901:'9-i3',134928:'8-i9',122593:'8-i7',122597:'8-i5',122588:'8-i3'};
const want = /^(Processor Number|Vertical Segment|Total Cores|# of Performance-cores|# of Efficient-cores|Total Threads|Max Turbo Frequency|Intel® Turbo Boost Max Technology 3\.0 Frequency|Intel® Turbo Boost Technology 2\.0 Frequency|Intel® Thermal Velocity Boost Frequency|Performance-core Max Turbo Frequency|Efficient-core Max Turbo Frequency|Performance-core Base Frequency|Efficient-core Base Frequency|Processor Base Frequency|Processor Base Power|Maximum Turbo Power|TDP|TJUNCTION|Launch Date|Code Name)$/;
const clean = (s) => s.replace(/<[^>]*>/g, '').replace(/‡/g, '').replace(/\s+/g, ' ').trim(), sleep = (ms) => new Promise(r => setTimeout(r, ms));
window.__intel = { done: false, skus: [], rows: [] };
(async () => { const S = window.__intel;
  for (const [id, tag] of Object.entries(series)) { const d = new DOMParser().parseFromString(await (await fetch(`/content/www/us/en/ark/products/series/${id}.html`)).text(), 'text/html');
    for (const a of d.querySelectorAll('a[href*="/products/sku/"][href$="/specifications.html"]')) if (!S.skus.some(x => x.href === a.getAttribute('href'))) S.skus.push({ tag, href: a.getAttribute('href'), name: a.textContent.trim().replace(/\s+/g, ' ') });
    await sleep(400); }
  for (const s of S.skus) { const d = new DOMParser().parseFromString(await (await fetch(s.href)).text(), 'text/html'); const row = { tag: s.tag, url: 'https://www.intel.com' + s.href, name: s.name };
    for (const b of d.querySelectorAll('button.copy-icon[aria-label^="Copy "]')) { const k = clean(b.getAttribute('aria-label').slice(5)); if (want.test(k) && !(k in row)) row[k] = b.getAttribute('data-copy-text'); }
    S.rows.push(row); await sleep(250); }
  S.done = true; })();
// when done: copy(JSON.stringify(window.__intel.rows, null, 1)) and save as intel-raw.json
```

## Reading amd.com
AMD's site refuses scripted requests (after a burst it answers "Access Denied" for a while), so its pages are opened one at a time in a
browser, a few seconds apart, and the text after "Base Clock", "Max. Boost Clock", "Default TDP", "Max. Operating Temperature (Tjmax)",
"# of CPU Cores" and "# of Threads" is taken. Newer desktop models have product pages
(`/en/products/processors/desktops/ryzen/<series>-series/amd-ryzen-<tier>-<model>.html`); every model, older and laptop ones included, has a
support page with the same figures (`/en/support/downloads/drivers.html/processors/ryzen/ryzen-<series>-series/amd-ryzen-<tier>-<model>.html`,
PRO models under `ryzen-pro/ryzen-pro-<series>-series`). Add each as one line of `amd-raw.json` (name, url, segment, and the six figures as the page
writes them).

Read on 2026-09-30: 486 Intel models (8th gen to Core Ultra series 3) and 81 AMD models (Ryzen 2000-9000 desktop, PRO 3400G, and common
Ryzen 5000-8000 laptop models). A model not in the table is judged from what the chip reports itself.
