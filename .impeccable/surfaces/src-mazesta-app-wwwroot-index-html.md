---
version: 1
slug: "src-mazesta-app-wwwroot-index-html"
primary_target: "src/Mazesta.App/wwwroot/index.html"
related_targets: []
---

# Surface: Mazesta Web edition (WebView2 host, all pages)

Scope: a separate executable (Mazesta.App) whose whole UI is one web app over the existing core. Mode: Operate. Audience: technicians at the
bench and the customer looking at the screen. Task: read live sensors, run tests and benchmarks, tune the GPU, make and print reports.
Constraints: Persian RTL first with Latin numerals intact; honesty rules (missing is shown as missing, never 0); offline; nothing animates
while hidden; software-rendered machines must stay usable (motion on opacity/transform only, reduced-motion respected).

Chosen direction and memorable moment: the poster opening on the dashboard, where the machine's verdict plane slides in and the giant
temperature numerals count to their live value, then settle into a quiet grid.

Unresolved: the web edition's settings for language switching reuse the shared config; the overlay and tray stay in the WPF edition for now.

## Direction contract

THESIS: The machine's state is set as an Iranian-modernist poster on a strict grid; the category's rounded card dashboard with gauges and
glow is refused. Type, planes and rules do all the work.

OWN-WORLD: Brand yellow #FDD400 as whole planes, not accents; ink black ground; paper off-white for type and rules; a heavy Persian face and
a condensed grotesk for numerals; hairline column rules, numbered indices, registration crosses; green only for passed, red only for failed,
diagonal hatching only for not measured.

STORY: The technician reads the machine's state at poster distance, drills into a part, runs work, and leaves with a report whose verdict
matches what the screen said.

FIRST VIEWPORT: A 12-column poster. Right edge: a numbered index (۰۱ … ۱۳) as navigation. Top two thirds: a yellow plane with the verdict
line and the CPU and GPU temperatures as numerals about 180 px tall, their min/max as small rules beneath. Lower band: ruled columns for
CPU, GPU, RAM, storage, network with dense tabular figures. Primary action (run tests) is a black slab button on the yellow plane.

FORM: Tehran modernist grid, position 6 of the grounded list, seed key 7f3b80cd (re-roll round 1). Raises: numbered step margin
(orizuru), type-only hierarchy (alphabet storm), state colour laws (arcade), one part per viewport (feed). Signature interaction: numerals
roll to live values and the yellow plane wipes between pages along the grid (view transitions).

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
