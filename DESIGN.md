---
name: Mazesta Test (web edition)
description: A Persian-first PC diagnostics suite set as a Tehran modernist poster on an ink ground, with brand yellow as whole planes.
colors:
  ink: "#0c0c0c"
  ink-2: "#141412"
  ink-3: "#1c1c19"
  paper: "#f3f1ea"
  paper-2: "#c9c6bb"
  paper-3: "#8f8c82"
  rule: "rgb(243 241 234 / 0.13)"
  rule-2: "rgb(243 241 234 / 0.3)"
  yellow: "#fdd400"
  yellow-deep: "#e2bd00"
  on-yellow: "#0c0c0c"
  on-yellow-2: "#4d4000"
  pass: "#2ec08e"
  fail: "#ee4b3d"
  warn: "#f2a122"
typography:
  display-numeral:
    fontFamily: "Archivo, Segoe UI, sans-serif"
    fontSize: "clamp(64px, min(11vw, 15.5vh), 196px)"
    fontWeight: 850
    lineHeight: 0.86
    letterSpacing: "-0.02em"
    fontFeature: "tnum"
    fontVariation: "'wdth' 62"
  headline-page:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "clamp(34px, 4.2vw, 64px)"
    fontWeight: 900
    lineHeight: 1
    letterSpacing: "-0.015em"
  headline-verdict:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "clamp(26px, min(3.2vw, 5.6vh), 52px)"
    fontWeight: 900
    lineHeight: 1.12
    letterSpacing: "-0.015em"
  headline-plane:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "clamp(24px, 2.6vw, 40px)"
    fontWeight: 900
    lineHeight: 1.15
  title:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "21px"
    fontWeight: 800
    lineHeight: 1.25
  title-small:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "15.5px"
    fontWeight: 700
    lineHeight: 1.35
  body:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "12.5px"
    fontWeight: 400
    lineHeight: 1.6
  value:
    fontFamily: "Archivo, Segoe UI, sans-serif"
    fontSize: "14.5px"
    fontWeight: 600
    letterSpacing: "0.01em"
    fontFeature: "tnum"
    fontVariation: "'wdth' 78"
  figure:
    fontFamily: "Archivo, Segoe UI, sans-serif"
    fontSize: "34px"
    fontWeight: 800
    lineHeight: 1.05
    fontFeature: "tnum"
    fontVariation: "'wdth' 70"
  index-number:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 800
    lineHeight: 1
  console:
    fontFamily: "Cascadia Mono, Consolas, monospace"
    fontSize: "12.5px"
    lineHeight: 1.55
rounded:
  none: "0px"
spacing:
  pad: "clamp(20px, 2.4vw, 40px)"
  gut: "24px"
  index-width: "232px"
  section: "44px"
  columns-top: "34px"
  page-bottom: "72px"
components:
  slab:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
    rounded: "{rounded.none}"
    padding: "13px 22px"
  button:
    backgroundColor: "transparent"
    textColor: "{colors.paper}"
    rounded: "{rounded.none}"
    padding: "8px 15px"
  button-primary:
    backgroundColor: "{colors.yellow}"
    textColor: "{colors.on-yellow}"
    rounded: "{rounded.none}"
    padding: "8px 15px"
  button-primary-hover:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.on-yellow}"
  button-go:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "8px 15px"
  button-go-hover:
    backgroundColor: "{colors.yellow}"
    textColor: "{colors.ink}"
  button-quiet:
    backgroundColor: "transparent"
    textColor: "{colors.paper-2}"
    padding: "8px 8px"
  field:
    backgroundColor: "transparent"
    textColor: "{colors.paper}"
    rounded: "{rounded.none}"
    padding: "6px 2px"
  pill-pass:
    backgroundColor: "transparent"
    textColor: "{colors.pass}"
    padding: "1px 9px"
  pill-fail:
    backgroundColor: "transparent"
    textColor: "{colors.fail}"
    padding: "1px 9px"
  pill-warn:
    backgroundColor: "transparent"
    textColor: "{colors.warn}"
    padding: "1px 9px"
  pill-run:
    backgroundColor: "transparent"
    textColor: "{colors.yellow}"
    padding: "1px 9px"
  pill-none:
    backgroundColor: "transparent"
    textColor: "{colors.paper-3}"
    padding: "1px 9px"
  plane:
    backgroundColor: "{colors.yellow}"
    textColor: "{colors.on-yellow}"
    rounded: "{rounded.none}"
    padding: "clamp(20px, 2.4vw, 36px) clamp(24px, 3vw, 44px)"
  index-item:
    backgroundColor: "transparent"
    textColor: "{colors.paper-2}"
    padding: "7px 24px"
  index-item-current:
    backgroundColor: "{colors.yellow}"
    textColor: "{colors.on-yellow}"
  table-row-selected:
    backgroundColor: "{colors.yellow}"
    textColor: "{colors.on-yellow}"
  toast:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    padding: "10px 16px"
  console:
    backgroundColor: "{colors.ink-2}"
    textColor: "{colors.paper-2}"
    typography: "{typography.console}"
    padding: "14px 16px"
    height: "360px"
---

# Design System: Mazesta Test (web edition)

This file governs the web edition only (`src/Mazesta.Web/wwwroot`, hosted in WebView2). The WPF edition (`src/Mazesta.Desktop`, `Themes/Dark.xaml`) is a separate, earlier design in the same brand colours and is not governed by this file.

## Overview

**Creative North Star: "The Tehran Modernist Poster"**

The machine's state is set as an Iranian-modernist poster on a strict grid. Type, flat planes and hairline rules do all the work: brand yellow arrives as whole planes, never as an accent sprinkled on controls; the ground is ink black; type and rules are paper off-white. The category's rounded card dashboard with gauges and glow is refused. There are no corners, no cards, and almost no shadows. Hierarchy comes from weight and scale alone: a heavy Persian face for words, a condensed grotesk for numerals, and giant temperature figures that roll to their live values.

The page reads right to left first, with every Latin value isolated left to right and kept in Latin digits. Density is poster-like at the top (one yellow plane, two giant numerals) and dense beneath (ruled columns of tabular figures). Honesty is visible: a value that was not measured is drawn as a diagonal hatch with the words "not available", never as a zero.

**Key Characteristics:**
- Yellow (#fdd400) as whole planes: the dashboard and component hero plane, the current index item, the selected table row.
- Zero radius everywhere; structure is carried by 1px hairlines and 2px paper rules on column tops.
- A numbered index (Persian two-digit numbers) on the reading-start edge as navigation; Ctrl+1 … Ctrl+0 open the first ten.
- Registration crosses at the four corners of every yellow plane.
- State colour laws: green only for passed, red only for failed, the hatch only for not measured.
- Motion is typographic: rolling numerals, a plane wipe between pages, rules drawing in. All of it switches off under reduced motion or `?still`.

## Colors

A three-material palette (ink, paper, yellow) with three narrowly-scoped state colours.

### Primary
- **Brand Yellow** (`yellow`): the poster plane (dashboard, component pages), the current index item, the selected monitoring row, primary buttons, the range and progress fill, the curve editor's tuned line and chart line, focus outlines, text selection, the running-state pill. Its signature use is as area; as a line it marks focus, progress and live data.
- **Deep Yellow** (`yellow-deep`): defined as a darker step of the brand yellow; reserved, currently unused by components.
- **Plane Ink** (`on-yellow`): all type, rules and registration marks on yellow.
- **Plane Umber** (`on-yellow-2`): secondary text on yellow (min/max range line, muted plane labels, the index number of the current item, the hatch on yellow).

### Neutral
- **Ink** (`ink`): the page ground, slab buttons, sticky table headers and the queue dock.
- **Ink Raised** (`ink-2`): the console well only.
- **Ink Hover** (`ink-3`): table row hover, the banner, select option lists, the curve tooltip.
- **Paper** (`paper`): primary type, the 2px column-top rules, the page-head underline, the toast.
- **Paper Muted** (`paper-2`): secondary type, idle index names, key column in key/value lists.
- **Paper Faint** (`paper-3`): captions, index numbers, idle queue step numbers, chart axes.
- **Hairline** (`rule`, paper at 13%): every row separator and the index border.
- **Rule Strong** (`rule-2`, paper at 30%): section-head underlines, button and field strokes, table header underline.

### State
- **Pass Green** (`pass`): only a passed result (test outcome Passed, report badge Passed, a clean tuning step).
- **Fail Red** (`fail`): only a failed result (test outcome Failed, report badge Failed, a tuning step that was not clean).
- **Caution Amber** (`warn`): results that are neither pass nor fail (Cancelled, Unsupported, Incomplete) and the demo-data notice in the status band.

### Named Rules
**The Whole Plane Rule.** Yellow is laid down as a plane or a full-row fill. It is never a glow, a gradient border or a tinted card.

**The State Law Rule.** Green means passed and nothing else; red means failed and nothing else; the diagonal hatch means not measured and always carries the words. A test that did not run gets `pill-none` or amber, never green.

**The Missing Is Drawn Rule.** An unavailable value renders as the hatch (135deg paper lines at 22% on ink, ink lines at 28% on yellow, 1px every 6px) with the localized "not available" text. Never 0, never a dash.

## Typography

**Display Font:** Vazirmatn (variable, self-hosted woff2, OFL) with Segoe UI fallback
**Numeral Font:** Archivo (variable width 62–125%, self-hosted ttf, OFL) with Segoe UI fallback
**Console Font:** Cascadia Mono, then Consolas (system; command output only)

**Character:** A heavy Persian sans at 800–900 carries every heading, set tight; a condensed grotesk carries every number, always tabular and left to right. The contrast between wide Persian words and narrow Latin figures is the poster's voice. Both faces use `font-display: block` so nothing flashes in a fallback.

### Hierarchy
- **Display numeral** (Archivo 850, clamp(64px, min(11vw, 15.5vh), 196px), line-height 0.86, width 62%): the CPU and GPU temperatures on the dashboard plane, with a unit at 0.26em. Component pages reuse it at clamp(40px, 5.2vw, 88px). The monitoring chart's big value uses the same face at 64px.
- **Page headline** (Vazirmatn 900, clamp(34px, 4.2vw, 64px), line-height 1): one per page, over a 2px paper rule.
- **Verdict** (Vazirmatn 900, clamp(26px, min(3.2vw, 5.6vh), 52px), line-height 1.12, max 26ch, balanced): the machine's standing on the dashboard plane.
- **Plane headline** (Vazirmatn 900, clamp(24px, 2.6vw, 40px)): component plane titles; a Latin variant in Archivo at width 80%.
- **Title** (Vazirmatn 800, 21px) and **small title** (700, 15.5px; 17px/900 as a column head, 20px/900 in the auto-tuning pair).
- **Body** (Vazirmatn 400, 15px, line-height 1.6): ledes are 14.5px, paper-muted, max 70ch.
- **Label** (12–12.5px): captions, table headers, queue option labels, the status band.
- **Value** (Archivo 600, width 78%, tabular, 0.01em): every sensor reading and unit, 13.5–14.5px in lists. Table cells use Archivo at width 85%.
- **Figure** (Archivo 800, 34px, width 70%): benchmark metrics; the tuning live strip uses 850 at clamp(28px, 2.8vw, 44px), width 66%.
- **Index / step number** (Vazirmatn 800; 13px in the index, 26px as a queue step): Persian digits, faint until active.

### Named Rules
**The Latin Island Rule.** Every value, unit, model name, version or path is wrapped in the `lat` or `num` role: Archivo, `direction: ltr`, `unicode-bidi: isolate`, tabular figures. Persian digits are reserved for indices, dates and prose.

**The Weight Is Hierarchy Rule.** Headings differ by weight and size only. No colour, no underline accents, no uppercase eyebrows above headlines.

## Layout

The shell is a two-column grid: a 232px index on the reading-start edge (right in RTL) with a hairline on its inline-end, and a scrolling stage beside it; a 38px status band runs under the stage. The page has padding clamp(20px, 2.4vw, 40px), 72px at the foot, and a maximum width of 1640px. The gutter is 24px; sections sit 44px apart.

The dashboard's first viewport is the yellow plane (verdict, machine names, two giant numerals each over a min/max range rule on a 0–100 °C scale, actions, a colophon with shop and date on a rule at its foot), then one row of ruled part columns (auto-fit, min 118px, 18px gap) that stays a single row at every allowed width (1024px and up). Specifications use fixed-measure columns (auto-fill, min 300px) so a label stays near its value; long lists use a 3-column masonry. Monitoring and tuning split into a main column and a sticky side column (minmax(360px, 34%) and minmax(320px, 1fr)); below 1180px both stack to one column and the tuning live strip drops from 6 to 3 columns.

**The Reading-Start Rule.** Layout uses logical properties (`inline-start`, `inline-end`) so the index, rules and fills follow `dir`. Latin values align left inside their isolated run.

## Elevation & Depth

The system is flat. Depth is conveyed by material (yellow plane over ink), by weight of rule (1px hairline, 1.5px on-yellow rule, 2px paper rule), and by sticky position (table header, chart column, queue dock), not by shadow. The single shadow is on the toast, which floats over content.

### Shadow Vocabulary
- **Toast lift** (`box-shadow: 0 10px 30px rgb(0 0 0 / 0.45)`): transient toasts only.

### Named Rules
**The Flat Poster Rule.** No surface casts a shadow at rest. A new surface earns separation with a rule or a plane, never with elevation.

## Shapes

Every corner is square (0px): buttons, fields, pills, checkboxes, switches, planes, toasts. The only rounding is the scrollbar thumb. Boundaries are rules rather than boxes: a column is a 2px paper rule on top with no side or bottom border; a field is a bottom stroke only; a pill is a 1px stroke in its state colour. Recurring geometry: registration crosses (18px, circle plus cross, 55% opacity) at plane corners; the range rule with a 3px span bar and a 2px tick; checkbox ticks drawn with borders; select arrows drawn with two yellow gradient triangles.

## Components

### Buttons
Blunt, typographic, square.
- **Slab:** the primary action on a yellow plane. Ink ground, paper text, 800 weight, 15px, padding 13px 22px, with an arrow icon that slides 5px toward the reading direction on hover; on a plane its hover darkens to #2a2a26. Presses down 1px.
- **Button (outline):** transparent, 1px `rule-2` stroke, 600 weight, 13.5px, padding 8px 15px; hover turns the stroke paper.
- **Primary:** yellow fill, ink text, 800; hover swaps to paper.
- **Go:** paper fill, ink text, 800; hover swaps to yellow. Used to start a queue.
- **Stop:** paper-muted stroke; hover fills paper with ink text.
- **Quiet:** no stroke, paper-muted text; hover underlines at 4px offset. Quiet + primary is a yellow-filled toggle-on state.
- **Disabled:** 35% opacity. Transitions are 160ms on colour and border.

### Chips (state pills)
- **Style:** transparent ground, 1px stroke in `currentColor`, 12px/700 text, padding 1px 9px.
- **Variants:** pass (green), fail (red), warn (amber), run (yellow), none (paper-faint), mapped from test outcomes and report badges.

### Cards / Containers
There are no cards. The two containers are:
- **Poster plane:** yellow ground, ink type, padding clamp(20px, 2.4vw, 36px) clamp(24px, 3vw, 44px), registration crosses at the corners, internal divisions drawn with 1.5px ink rules.
- **Ruled column:** a 2px paper rule on top, 12px padding above its content, no box. Its head pairs a 17px/900 title with a faint link to the part's page that turns yellow on hover.

### Inputs / Fields
- **Style:** transparent, bottom stroke only (1px `rule-2`), padding 6px 2px; Latin fields centre their text.
- **Hover / Focus:** stroke to paper-muted on hover, yellow on focus (no outline).
- **Check:** 18px square, 1.5px stroke; checked fills yellow with an ink tick.
- **Switch:** 38×20 square track; the knob slides with the slow ease; on, the track fills yellow.
- **Range:** 2px track filled yellow from the reading-start edge to `--fill`; a 14px yellow thumb ringed in ink that scales 1.25 on hover.
- **Global focus:** `2px solid` yellow outline, 3px offset.

### Navigation
- **Numbered index:** each row is a two-digit Persian number (13px/800, faint) and a name (14.5px/600, paper-muted), padding 7px 24px, grouped by 1px separators after items 5 and 9. Hover brightens to paper and draws a 2px yellow underline from the reading-start edge with the slow ease. The current page is a full-width yellow bar with ink text and an umber number.
- **Status band:** 38px, 12.5px paper-muted, provider state, interval, quiet overlay and pause toggles.

### Tables (monitoring)
13.5px rows at 5px 8px with hairline separators; sticky 12px header on ink. Hardware groups open with a 2px paper rule and an Archivo 900 name; sections are labelled in 12px/700 yellow. Row hover is ink-hover; the selected row is a full yellow bar with ink text, and the hatch inverts to ink lines on it.

### Queues (tests, benchmarks)
Each row: a 26px/800 Persian step number, a check, an 800-weight name, controls at the inline-end; a hairline beneath. The active row turns its number yellow and draws a 1px yellow line outside its reading-start edge. Progress is a 3px track with a yellow bar scaled from the reading-start edge (500ms). A sticky dock with a 2px paper rule holds the run controls.

### Rolling numerals (signature)
Each digit is a column of 0–9 that translates to its value over 900ms on the house ease; only changed digits move, so a steady reading stays still. A missing value replaces the column with the "not available" words in Vazirmatn 800 at a reduced size.

### Charts and the curve editor
Canvas history chart: 1px `rule` grid, 11px Archivo axes in paper-faint, a 2px yellow line over a yellow wash fading from 22% to 0, broken at gaps longer than 8s; an empty series prints "not available". The V/F curve editor (SVG): stock curve dashed paper-faint, tuned curve 2.5px yellow, square-ended ink points ringed in yellow that fill on hover, a dashed yellow power cap with a yellow tag.

### Console
Real command output in Cascadia Mono 12.5px/1.55 on `ink-2`, 360px tall, under a 2px paper rule.

### Toasts and banner
Toasts: paper ground, ink 600 text, the one shadow, rising 12px in over 420ms, removed after 7s; `pass`/`fail` kinds take the state colours. Banner: ink-hover strip with a 1px yellow bottom rule for provider degradation.

### Motion

Easing is `cubic-bezier(0.16, 1, 0.3, 1)` throughout; fast is 160ms (colour, border), slow is 620ms (underline draw, switch knob, slab arrow). Every animation is on opacity, transform or clip-path.
- **Page change:** a view transition on the stage: the old page fades out in 240ms, the new one wipes in with a clip-path from the right edge over 520ms. Skipped when the document is hidden, under reduced motion, or with `?still`.
- **Plane entrance:** the yellow plane wipes in by clip-path over 820ms.
- **Rule entrance:** columns, page heads and section heads rise 8px and fade in over 700ms, staggered 45ms by `--i`.
- **Range rule:** span and tick glide 600ms to new min/max/current positions.
- **Off switches:** `prefers-reduced-motion: reduce` and the `?still` flag (`data-still` on the root) remove all animations, transitions and view-transition animations.

## Do's and Don'ts

### Do:
- **Do** lay yellow (#fdd400) down as a whole plane or a full-width bar, with ink (#0c0c0c) type on it.
- **Do** put every value in the Latin role (Archivo, tabular, isolated LTR) and every heading in Vazirmatn at 800–900.
- **Do** draw a missing value as the hatch with the words "not available".
- **Do** separate content with rules: 1px hairlines between rows, a 2px paper rule on top of a column or page head.
- **Do** keep every corner at 0px.
- **Do** use logical properties so the index, fills and underlines start at the reading-start edge in both RTL and LTR.
- **Do** keep motion on opacity, transform and clip-path, and honour reduced motion and `?still`.

### Don't:
- **Don't** use green for anything but a passed result, or red for anything but a failed one.
- **Don't** show 0, a dash or a placeholder for an unmeasured value.
- **Don't** build rounded cards, gauges, glows or drop shadows; the toast is the only lifted surface.
- **Don't** set Latin figures in Vazirmatn or let them render as Persian digits.
- **Don't** add uppercase letter-spaced eyebrows or kickers above headlines; the plane's shop and date sit in a colophon at its foot.
- **Don't** animate while the window is hidden or add timers that wake without need.
