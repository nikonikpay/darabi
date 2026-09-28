---
name: Mazesta Test (web edition)
description: A Persian-first PC diagnostics suite as a calm dark instrument panel - live numbers big in each part's own hue on quiet cards, the brand yellow as the one accent.
colors:
  ink: "#0a0a0f"
  ink-2: "#121219"
  ink-3: "#1a1a23"
  ink-4: "#23232e"
  side: "#0d0d13"
  paper: "#f1f1f5"
  paper-2: "#b3b3c1"
  paper-3: "#777787"
  rule: "rgb(255 255 255 / 0.07)"
  rule-2: "rgb(255 255 255 / 0.14)"
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
    fontSize: "36px"
    fontWeight: 750
    lineHeight: 1
    fontFeature: "tnum"
    fontVariation: "'wdth' 72"
  page-title:
    fontFamily: "Vazirmatn, Segoe UI, sans-serif"
    fontSize: "23px"
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
  control: "10px"
  field: "8px"
  card: "16px"
  hero: "20px"
  pill: "999px"
spacing:
  pad: "clamp(18px, 2vw, 32px)"
  gut: "16px"
  side-width: "236px"
  side-width-narrow: "76px"
  section: "30px"
---

# Design System: Mazesta Test (web edition)

This file governs the web edition (`src/Mazesta.Web/wwwroot`, hosted in WebView2) and the on-screen overlay (`src/Mazesta.Desktop/Views/OverlayWindow.xaml`), which follows it. The rest of the WPF edition (`Themes/Dark.xaml`) is an earlier design in the same brand colours.

## Overview

**North star: "The instrument panel in the dark."** A technician glances at it across a bench: the numbers must read first. The ground is a near-black with two faint glows; content sits on quiet cards; the live numbers are big, Latin and coloured in their part's hue, with their unit small beside them; labels are small and grey. The brand yellow is the one accent: the active page, primary actions, focus, progress, the live mark by each page title.

Every part of the machine has its own hue (CPU blue, GPU violet, RAM cyan, storage orange, network pink, board sand) and wears it wherever it appears: its tile, its card's icon and wash, its chart line, its test group, its overlay card. A technician finds the GPU by colour before reading a word.

The page reads right to left, with every Latin value isolated left to right and kept in Latin digits. Honesty is visible: a value that was not measured is drawn as a diagonal hatch with the words "not available", never as a zero.

## Colors

- **Surfaces** from darkest: `ink` (the ground), `side` (the side bar), `ink-2` (cards), `ink-3` (buttons, raised cells), `ink-4` (tracks, hover). Two static radial glows on the ground (yellow top corner, violet opposite) so the dark is not flat; they never move.
- **Type:** `paper` primary, `paper-2` secondary, `paper-3` labels and captions.
- **Hairlines:** `rule` (7 %) between rows and around cards; `rule-2` (14 %) for fields and stronger edges.
- **Brand yellow:** the active side-bar entry (a soft yellow wash, yellow text and a glowing 3px bar at the reading-start edge), the active tab and primary buttons (filled, ink text), the page title's live dot, focus rings, sliders, the FPS card of the overlay.
- **State laws:** `pass` green only for passed (and the live dot), `fail` red only for failed, `warn` amber for neither-pass-nor-fail and cautions. No part hue is green or red.
- **Part hues:** set once per container (`.p-cpu` sets `--hue`), read everywhere inside as `var(--hue)`; the canvas reads the same token (`hueOf`). The overlay uses the same hex values.

## Typography

Vazirmatn for words (800 for titles, 600–750 for card titles, 400 for body); Archivo, condensed and tabular, for every number (`.lat` / `.num`: `direction: ltr; unicode-bidi: isolate`). Headings differ by weight and size only. Page titles are small (23px): the page is the content, not a headline.

## Layout

- **Shell:** a side bar on the reading-start edge (236px, or 76px icons-only, toggled and remembered per viewer), the scrolling stage, and a 34px status band under it with the provider's live dot.
- **Families:** the side bar has eight entries: dashboard, monitoring, tests & benchmarks, hardware (system, CPU, GPU, storage, network), gaming & overlay, optimization (tuning, Windows tools), reports, settings. A family of several pages shows them as a sticky pill tab strip at the top of each; its entry returns to the page last open in it. Ctrl+1 … Ctrl+8 open the families.
- **Page:** a small header (title with the yellow live dot, a one-line lede in `paper-3`, actions at the other end), then cards in grids of 14–16px gaps.

## Components

- **Hero card (`.plane`):** a dark card lit by its hue (a diagonal wash and one soft radial glow in a corner). The dashboard's status card (live kicker, verdict, machine, four quick actions, shop and date) and a part page's card of live tiles.
- **Live tile (`.tile`, `tiles.js`):** label and part icon at the top, the main reading big in the hue with its unit small, a second reading beside it (load, upload), a 5px bar for a share (load, used space) with a soft glow in the hue, and the device at the foot. Digits roll to new values. Tiles rise in, staggered; they lift 2px on hover when they are links.
- **Card (`.panel`):** `ink-2` with a faint hue wash at the top, a 1px hairline border that takes the hue on hover, one soft shadow. Head: a 36px icon tile (the hue at 15 % with the icon in the hue), title, a Latin subtitle, and a chevron that folds details open (grid rows 0fr → 1fr, nothing measured). Summary stats are small raised cells; the first value takes the hue.
- **Group:** a card whose head folds its whole body (tests, benchmarks, monitoring devices), with a select-all tick and a count pill; the icon tile pulses while one of its rows runs.
- **Controls:** buttons are raised `ink-3` with 10px corners; primary is yellow; stop is a red-tinted outline; quiet has no fill. Fields are boxed (8px, `rule-2`) with a yellow focus ring. The switch is a filled track with a round knob. Segments are pill groups on `ink`. Pills are tinted with a leading dot.
- **Tables (monitoring):** flat inside their card; a charted row takes a 10 % hue wash and a 3px hue bar at its start edge.
- **Queues:** step numbers in Archivo, the running row's number and bar in the hue; the run controls sit in a floating dock (blurred, rounded) at the bottom.
- **Toasts:** raised dark cards with a 3px accent edge (yellow, or the state colour).
- **Start-up:** a native loading panel until the page draws, then a card with a spinner, what is being read, the provider's status and a seconds counter.

## The overlay

Built like the page: a translucent dark panel (16px corners). On top, the **frame-rate card**: FPS big and white with "FPS" in yellow, the 1 % low and frame time beside it, and the last 30 readings as rounded yellow bars (the newest at full strength) - drawn whether or not the FPS chart is on, since it is what the card is for. Under it, a **card per part** in its hue title, each reading as its label, its number big and white with the unit small in the hue, and a 3px bar for a share of a fixed top (load, temperature); a charted item draws its minute instead. One column or two. The page's preview draws the same thing.

## Motion

Easing `cubic-bezier(0.16, 1, 0.3, 1)`; 160ms for colour and border, 520ms for movement. Only opacity and transform animate: pages fade and rise in (a view transition), cards and tiles rise in staggered by `--i`, digits roll, bars scale. The only loops are the live dots and the start-up spinner. Everything pauses while the window is hidden (`data-visible="false"`) and switches off under `prefers-reduced-motion` or `?still`. No WebGL or canvas animation: the app must stay light on old machines.

## Do's and Don'ts

- **Do** make the reading the biggest thing on a tile, in the part's hue, with its unit small.
- **Do** keep every value Latin and isolated; draw a missing one as the hatch with words.
- **Do** give every part one hue everywhere, set once with the part class.
- **Do** keep motion on opacity and transform, once, and honour hidden windows, reduced motion and `?still`.
- **Don't** use green for anything but passed (and the live dot), or red for anything but failed.
- **Don't** lay yellow down as a whole plane; it is the accent.
- **Don't** add big headlines: a page title is 23px.
- **Don't** add looping animation, timers that wake without need, or heavy libraries.
