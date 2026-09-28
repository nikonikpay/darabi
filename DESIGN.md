---
name: Mazesta Test (web edition)
description: A Persian-first PC diagnostics suite drawn as bench test gear - a graphite faceplate, flat panels with small corners, each part on its own channel colour, live numbers in recessed readout windows, the brand yellow as a marking.
colors:
  ink: "#15171a"
  ink-2: "#1c1f23"
  ink-3: "#252930"
  ink-4: "#2f343b"
  side: "#111316"
  well: "#0e1012"
  paper: "#ecebe6"
  paper-2: "#a9acb0"
  paper-3: "#737a82"
  rule: "#2a2e34"
  rule-2: "#3a4048"
  yellow: "#fdd400"
  yellow-soft: "rgb(253 212 0 / 0.12)"
  on-yellow: "#0c0c0c"
  pass: "#2ec08e"
  fail: "#ee4b3d"
  warn: "#f2a122"
  part-cpu: "#5ba8ff"
  part-gpu: "#b38bff"
  part-ram: "#35d0e0"
  part-storage: "#ff9a4d"
  part-net: "#ff78b9"
  part-board: "#d9ccb0"
  part-tool: "#9fb6cf"
typography:
  tile-value:
    fontFamily: "Archivo, Segoe UI, sans-serif"
    fontSize: "34px"
    fontWeight: 700
    lineHeight: 1
    fontFeature: "tnum"
    fontVariation: "'wdth' 68"
  page-title:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "24px"
    fontWeight: 800
    lineHeight: 1.3
  verdict:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "clamp(20px, 1.9vw, 28px)"
    fontWeight: 800
    lineHeight: 1.3
  card-title:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "15px"
    fontWeight: 750
    lineHeight: 1.3
  body:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "14.5px"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "12px"
    fontWeight: 500
    lineHeight: 1.5
  value:
    fontFamily: "Archivo, Segoe UI, sans-serif"
    fontSize: "13.5px"
    fontWeight: 600
    fontFeature: "tnum"
    fontVariation: "'wdth' 82"
  console:
    fontFamily: "Cascadia Mono, Consolas, monospace"
    fontSize: "12px"
    lineHeight: 1.6
rounded:
  control: "5px"
  well: "4px"
  card: "6px"
  stamp: "3px"
spacing:
  pad: "clamp(18px, 2vw, 32px)"
  gut: "16px"
  side-width: "236px"
  side-width-narrow: "76px"
  section: "30px"
---

# Design System: Mazesta Test (web edition)

This file governs the web edition (`src/Mazesta.Web/wwwroot`, hosted in WebView2) and the on-screen overlay (`src/Mazesta.Desktop/Views/OverlayWindow.xaml`), which follows it. The WPF edition and its own theme were retired on 2026-09-28 (git tag `wpf-edition-final`).

## Overview

**North star: "Bench test gear."** A technician glances at it across a bench, as at a scope or a bench meter: the numbers must read first, and the plate around them stays out of the way. The ground is a graphite faceplate (not black, never glowing); panels are flat, framed by a hairline, with 6px corners; live readings sit in recessed readout windows in their part's colour; shares are graduated scales. The brand yellow is a marking, not a light: the active page's notch and icon, the active tab's underline, primary actions, focus, the dashboard plate's top band.

Every part of the machine is a channel with its own colour (CPU blue, GPU violet, RAM cyan, storage orange, network pink, board sand), worn as a scope marks its channels: a solid channel key with the icon cut out in the plate's dark, a colour strip down the start edge of its tile and chart card, its trace, its scale. A technician finds the GPU by colour before reading a word.

What this is not (it was, and read as generated): near-black with coloured glows, the same 16px rounded card with a gradient wash everywhere, pale tinted icon squares, yellow pill tabs, glowing bars, cards that lift on hover.

The page reads right to left, with every Latin value isolated left to right and kept in Latin digits. Honesty is visible: a value that was not measured is drawn as a diagonal hatch with the words "not available", never as a zero.

## Colors

- **Surfaces** from darkest: `well` (readout windows, fields, chart screens: recessed with an inner shadow), `side` (the side bar, the status band), `ink` (the ground), `ink-2` (panels), `ink-3` (buttons, the active index row), `ink-4` (tracks, hover). No glows, no gradient washes; depth comes from the recess and a hairline of light on a panel's top edge.
- **Type:** `paper` primary (a warm white, like silkscreen), `paper-2` secondary, `paper-3` labels and captions.
- **Hairlines:** `rule` between rows and around panels; `rule-2` for buttons, fields and stronger edges.
- **Brand yellow:** the active index row's 3px notch and icon, the active tab's 2px underline, primary buttons (filled, ink text), the dashboard plate's top band, the run dock's top edge, focus rings, slider fills, the overlay's FPS trace.
- **State laws:** `pass` green only for passed (and the live dot), `fail` red only for failed, `warn` amber for neither-pass-nor-fail and cautions. States are stamps: a 3px-cornered frame in the colour with a small square. No part hue is green or red.
- **Part hues:** set once per container (`.p-cpu` sets `--hue`), read everywhere inside as `var(--hue)`; the canvas reads the same token (`hueOf`). The overlay uses the same hex values.

## Typography

Vazirmatn for words (800 for titles, 700–750 for panel titles, 400 for body); Archivo, condensed and tabular, for every number (`.lat` / `.num`: `direction: ltr; unicode-bidi: isolate`), and Archivo expanded for the few Latin tags (the brand line, the overlay's channel keys). Headings differ by weight and size only. Page titles are 24px: the page is the content, not a headline. The overlay window sets its numbers in Bahnschrift (the DIN face Windows ships), semi-condensed.

## Layout

- **Shell:** a side bar on the reading-start edge (236px, or 76px icons-only, toggled and remembered per viewer), the scrolling stage, and a 32px status band under it with the provider's live dot.
- **Families:** the side bar has eight entries: dashboard, monitoring, tests & benchmarks, hardware (system, CPU, GPU, storage, network), gaming & overlay, optimization (tuning, Windows tools), reports, settings. A family of several pages shows them as a sticky ruled tab strip across the page top (the open one underlined in yellow); its entry returns to the page last open in it. Ctrl+1 … Ctrl+8 open the families.
- **Page:** a small header (title, a one-line lede in `paper-3`, actions at the other end), then panels in grids of 12–14px gaps.

## Components

- **Plate (`.plane`):** the dashboard's status card and a part page's card of live tiles: a flat panel with a 3px band of its hue along the top edge, like a unit's rating label.
- **Live tile (`.tile`, `tiles.js`):** a meter channel: a 3px strip of the hue down its start edge, the label and a grey icon, the reading big in the hue inside a recessed readout window with its unit small, a second reading beside it, a graduated scale (ticks every tenth) for a share, and the device at the foot. Digits roll to new values. Tiles rise in, staggered; a tile that is a link lightens on hover, never lifts.
- **Panel (`.panel`):** `ink-2`, hairline border, 6px corners. Head: the channel key (30px, 4px corners, the hue solid with the icon in ink), title, a Latin subtitle, and a chevron that folds details open (grid rows 0fr → 1fr, nothing measured). Summary stats are small readout windows; the first value takes the hue.
- **Group:** a panel whose head folds its whole body (tests, benchmarks, monitoring devices), with a select-all tick and a count; the channel key blinks softly while one of its rows runs.
- **Controls:** buttons are raised `ink-3` with a `rule-2` edge and 5px corners; primary is yellow; stop is a red-tinted outline; quiet has no fill. Fields and segments are recessed wells. The switch is a slide switch: a recessed slot and a square cap that turns yellow. Sliders have an upright fader cap. Ticks are 3px-cornered.
- **Charts (`drawChart`):** a scope screen: the trace in its channel colour over a faint fill, on a recessed screen with a dotted graticule (ten divisions across, four down).
- **Tables (monitoring):** flat inside their panel; a charted row takes an 8 % hue wash and a 3px hue bar at its start edge.
- **Queues:** step numbers in Archivo, the running row's number and bar in the hue; the run controls sit in a dock at the bottom with a yellow top edge.
- **Toasts:** raised panels with a 3px accent edge (yellow, or the state colour).
- **Start-up:** a native loading panel until the page draws, then a card with a spinner, what is being read, the provider's status and a seconds counter.

## The overlay

One translucent plate (8px corners, a hairline edge), no cards inside it. On top, the **frame-rate block**: FPS big and white with "FPS" in yellow, the 1 % low and frame time beside it, and the last minute as a **trace on a scope screen** (`FrameChart` in the window, `frameChart` in the page's preview): a dark screen with dotted divisions, the yellow line over a faint fill, the 1 % low as a dashed white level, the newest point marked, scaled from zero so a stutter reads as a dip. It is drawn whether or not the FPS chart is on, since it is what the block is for. Under it, a **block per part**, divided by hairlines, under its channel key (the part's short name in ink on a solid tag of its hue): each reading as its label, its number white with the unit small in the hue, and a 2px scale for a share of a fixed top (load, temperature); a charted item draws its minute instead. One column or two. The page's preview draws the same thing.

## Motion

Easing `cubic-bezier(0.16, 1, 0.3, 1)`; 160ms for colour and border, 520ms for movement. Only opacity and transform animate: pages fade and rise in (a view transition), panels and tiles rise in staggered by `--i`, digits roll, bars scale. The only loops are the live dots (opacity) and the start-up spinner. Everything pauses while the window is hidden (`data-visible="false"`) and switches off under `prefers-reduced-motion` or `?still`. No WebGL or canvas animation: the app must stay light on old machines.

## Do's and Don'ts

- **Do** make the reading the biggest thing on a tile, in the part's hue, in its readout window, with its unit small.
- **Do** keep every value Latin and isolated; draw a missing one as the hatch with words.
- **Do** give every part one colour everywhere, set once with the part class, worn as its channel key and strip.
- **Do** keep motion on opacity and transform, once, and honour hidden windows, reduced motion and `?still`.
- **Don't** use green for anything but passed (and the live dot), or red for anything but failed.
- **Don't** bring back glows, gradient washes, pill tabs, tinted icon squares, big rounded corners or hover lifts.
- **Don't** lay yellow down as a whole plane; it is a marking.
- **Don't** draw the frame rate as bars.
- **Don't** add looping animation, timers that wake without need, or heavy libraries.