# Verification - the lights after sleep, effects for every device, firewall and antivirus (slice 14)

What was run on the owner's machine (Ryzen + ASUS PRIME B550M-A + RTX 3090, Windows 11), with `Mazesta-Admin.exe --selftest=...` started elevated and nobody at the screen. Anything not listed here was not run.

## Firewall and antivirus (`--selftest=shield`, `Data\logs\selftest-shield.txt`)

A copy of the app's own exe, in a temporary folder, with a curly quote and spaces in its name, was used as the program to block.

| Step | Result |
|---|---|
| Firewall profiles read | Domain on, Private on, **Public off** (the page says so: a block does nothing in a profile that is off) |
| Block (both directions) | rules made, read back: inbound and outbound, 2 rules |
| Block again | still one entry (the same program has the same rule names) |
| Unblock | both rules gone, read back |
| Defender state | defender true, real-time true, Tamper Protection **on**, Windows Defender the only product, 27 exclusions already present |
| Exclude a folder | accepted by Defender with Tamper Protection on, read back in the list |
| Remove the exclusion | gone, read back |
| Exclude a whole drive | refused (`TooBroad`) unless the user confirms |
| Right-click entries | written under `HKCU\Software\Classes\exefile\shell\MazestaFirewallBlock` and `Directory\shell\MazestaAvExclude`, read back, removed |

The right-click action itself (`Mazesta-Admin.exe --fw-block "<exe>"`, started elevated) made the Inbound and Outbound block rules for a file with spaces in its name; its message box was up when the rules were read. The test rules were removed afterwards.

Not run: the same on a computer whose antivirus is not Windows Defender (the page then names the product and says the list is Defender's); a non-Persian Windows; the right-click entries as Windows 11's "Show more options" menu shows them (the registry keys are the ordinary shell verbs).

## The lights (`--selftest=lights`, `Data\logs\selftest-lights.txt`)

Four devices were found: two ENE memory sticks (5 LEDs), the RTX 3090's one LED, the board (70 LEDs).

| Step | Result |
|---|---|
| Write down how the lights are, put them out, put them back from the note | all 4 devices came back in the mode and colours they had (`OK` for each) |
| End the lights' server and start it again (what a wake does) | connected after 5.5 s, the same 4 devices found again |
| Rainbow for every device | memory and board by their own Rainbow; the graphics card has no Rainbow, so the app drove it: its colours were changing within 0.45 s |
| Colour cycle, Breathing, Flashing | every device has its own (the card's flashing is its "Strobe"), none needed the app |
| The colour spectrum "sunset" as a still gradient | memory and board went from #ff512f at the first LED to #dd2476 at the last; the card (one LED) took the first colour |
| The tray started alone (`MazestaTray.exe`) | connected and put the scene on after its 15 s start delay; written in `Data\logs\tray-lights.log` |

Not run: an actual sleep and wake of the computer (the PC was not put to sleep with nobody there to wake it): the pieces a wake runs - the note, the restore, the server restart, the second pass - were each run, but not driven by Windows' own sleep event. The tray now writes what it does to `logs\tray-lights.log`, so the next time the lights do not come back after sleep that file says where it stopped. Putting the lights out was sent, but the devices keep their last colours in what they report, so "out" was not seen on the LEDs themselves.

## Texture size

Measured by exporting the garden again with every texture 2048 x 2048 (see the commit message of the lights change).

## Second round: right-click menu, every blocked program, antivirus and firewall switches

Run on the owner's machine (`--selftest=shield`, then the app started by it):
- The firewall list now shows every enabled blocking rule that names a program, not only this app's: 23 programs (Adobe, Crimson Desert and others, outgoing only), all marked "made by others".
- Block, block again, unblock, exclude, remove exclusion and the too-broad refusal all passed again; Defender reads: real-time on, behaviour monitoring on, Tamper Protection on, no restore task, no policy.
- The two right-click entries are now written at the app's first start (they were behind a switch that began off, which is why "Show more options" showed nothing); read back from `HKCU\Software\Classes\exefile\shell\MazestaFirewallBlock` and `Directory\shell\MazestaAvExclude`.

Not run, on purpose or for lack of means: turning the real antivirus or firewall off (it changes the owner's security; the scripts, the read-back and the refusal path are covered by unit tests with a stand-in Windows only); the start-up task that puts the antivirus back after a restart (never created for real; a restart was not made); the tray menu entries and the `--av-stop` / `--fw-off` / `--fw-on` boxes (built, not clicked); the entries in Windows 11's "Show more options" menu itself (only the registry was read).
