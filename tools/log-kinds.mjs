// Groups the downloaded log-error file (system \t time \t line; from the site's "خلاصهٔ خطاهای لاگ" page) by what each line says, numbers as #.
// Usage: node tools/log-kinds.mjs file.tsv [textFilter] [topN=60]   -> sorted by how many systems share the message.
import { readFileSync } from 'node:fs';
const [file, filter = '', top = '60'] = process.argv.slice(2);
if (!file) { console.error('usage: node tools/log-kinds.mjs file.tsv [textFilter] [topN]'); process.exit(1); }
const kinds = new Map(), all = new Set();
for (const ln of readFileSync(file, 'utf8').split('\n')) {
    const [sys, , line] = ln.split('\t'); const m = line && /^\S+ (ERR|WRN|FTL) (.+)$/.exec(line);
    if (!m || (filter && !m[2].toLowerCase().includes(filter.toLowerCase()))) continue;
    all.add(sys); const text = m[2].trim().slice(0, 200).replace(/\d+(\.\d+)?/g, '#'), key = m[1] + '|' + text;
    const k = kinds.get(key) ?? { level: m[1], text, n: 0, systems: new Set(), example: m[2].trim().slice(0, 300) };
    k.n++; k.systems.add(sys); kinds.set(key, k);
}
const rows = [...kinds.values()].sort((a, b) => b.systems.size - a.systems.size || b.n - a.n).slice(0, +top);
console.log(`${kinds.size} kinds from ${all.size} systems`);
for (const k of rows) console.log(`${String(k.systems.size).padStart(5)} sys ${String(k.n).padStart(6)} x  ${k.level}  ${k.text}\n        e.g. ${k.example}\n        ${[...k.systems].slice(0, 5).join(' ')}`);
