// Emulator-only: the arm64 translator (ndk_translation) has no SEVL/WFE, which Unity's spin locks use.
// Rewrites those words to NOP inside executable PT_LOAD segments of an ELF64 shared object.
// usage: node nop-wfe.js <in.so> <out.so>
const fs = require('fs');
const [src, dst] = process.argv.slice(2);
const b = fs.readFileSync(src);
if (b.readUInt32BE(0) !== 0x7f454c46 || b[4] !== 2) throw new Error('not ELF64');
const phoff = Number(b.readBigUInt64LE(0x20));
const phentsize = b.readUInt16LE(0x36), phnum = b.readUInt16LE(0x38);
const SEVL = 0xd50320bf, WFE = 0xd503205f, NOP = 0xd503201f;
let n = 0;
for (let i = 0; i < phnum; i++) {
  const p = phoff + i * phentsize;
  const type = b.readUInt32LE(p), flags = b.readUInt32LE(p + 4);
  if (type !== 1 || !(flags & 1)) continue; // PT_LOAD with PF_X
  const off = Number(b.readBigUInt64LE(p + 8)), size = Number(b.readBigUInt64LE(p + 0x20));
  for (let o = off; o + 4 <= off + size; o += 4) {
    const w = b.readUInt32LE(o);
    if (w === SEVL || w === WFE) { b.writeUInt32LE(NOP, o); n++; }
  }
}
fs.writeFileSync(dst, b);
console.log(`${src}: ${n} sevl/wfe -> nop`);
