// Writes a script for Android's `uinput` shell tool that registers a multi-touch screen and plays one
// gesture on it (adb shell input can only do one finger). Usage:
//   node touch-gen.js tap2 x1 y1 x2 y2 > g.json     two-finger tap
//   node touch-gen.js drag x y1 y2 [ms] > g.json    one-finger vertical drag
//   node touch-gen.js tap x y > g.json              one finger
// then: adb push g.json /data/local/tmp/ && adb shell uinput /data/local/tmp/g.json
// The gesture starts 3 s after registration (the device needs a moment to be picked up).
const ai = (code, max) => ({ code, info: { value: 0, minimum: 0, maximum: max, fuzz: 0, flat: 0, resolution: 0 } });
const out = [{
    id: 2, command: "register", name: "Princess Test Touch", vid: 4660, pid: 22136, bus: "usb",
    // EV_SYN/EV_KEY/EV_ABS, BTN_TOUCH, ABS_X/Y + MT slot/x/y/tracking id, INPUT_PROP_DIRECT
    configuration: [{ type: 100, data: [0, 1, 3] }, { type: 101, data: [330] }, { type: 103, data: [0, 1, 47, 53, 54, 57] }, { type: 110, data: [1] }],
    abs_info: [ai(0, 1919), ai(1, 1079), ai(47, 9), ai(53, 1919), ai(54, 1079), ai(57, 65535)],
}];
const inj = e => out.push({ id: 2, command: "inject", events: e });
const wait = ms => out.push({ id: 2, command: "delay", duration: ms });
const [mode, ...a] = process.argv.slice(2);
const n = a.map(Number);
wait(3000);
if (mode === "tap2") {
    const [x1, y1, x2, y2] = n;
    inj([3, 47, 0, 3, 57, 101, 3, 53, x1, 3, 54, y1, 1, 330, 1, 3, 0, x1, 3, 1, y1, 0, 0, 0]); wait(60);
    inj([3, 47, 1, 3, 57, 102, 3, 53, x2, 3, 54, y2, 0, 0, 0]); wait(150);
    inj([3, 47, 1, 3, 57, -1, 0, 0, 0]); wait(40);
    inj([3, 47, 0, 3, 57, -1, 1, 330, 0, 0, 0, 0]);
} else if (mode === "drag") {
    const [x, y1, y2, ms = 600] = n;
    inj([3, 47, 0, 3, 57, 201, 3, 53, x, 3, 54, y1, 1, 330, 1, 3, 0, x, 3, 1, y1, 0, 0, 0]); wait(50);
    const steps = 20;
    for (let i = 1; i <= steps; i++) {
        const y = Math.round(y1 + (y2 - y1) * i / steps);
        inj([3, 47, 0, 3, 54, y, 3, 1, y, 0, 0, 0]); wait(ms / steps);
    }
    wait(100);
    inj([3, 47, 0, 3, 57, -1, 1, 330, 0, 0, 0, 0]);
} else if (mode === "tap") {
    const [x, y] = n;
    inj([3, 47, 0, 3, 57, 301, 3, 53, x, 3, 54, y, 1, 330, 1, 3, 0, x, 3, 1, y, 0, 0, 0]); wait(100);
    inj([3, 47, 0, 3, 57, -1, 1, 330, 0, 0, 0, 0]);
} else throw new Error("mode: tap2 | drag | tap");
wait(1500);
console.log(out.map(x => JSON.stringify(x)).join("\n"));
