// Games: the same page as the programs, for the games list: each game judged on its publisher's own tiers, with the publisher's target where it
// names one. No frame rate is predicted for this computer.
import { mountList } from "./apps.js";

export function mount(el) { return mountList(el, true); }
