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
