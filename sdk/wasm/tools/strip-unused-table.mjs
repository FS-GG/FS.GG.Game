import fs from "node:fs";

const [input, output] = process.argv.slice(2);
if (!input || !output) throw new Error("usage: strip-unused-table.mjs INPUT OUTPUT");
const bytes = fs.readFileSync(input);
if (bytes.length < 8 || bytes.subarray(0, 8).toString("hex") !== "0061736d01000000") throw new Error("not wasm v1");

function u32(offset) {
  let value = 0, shift = 0, count = 0;
  while (count++ < 5) {
    if (offset >= bytes.length) throw new Error("truncated LEB");
    const part = bytes[offset++]; value |= (part & 0x7f) << shift;
    if ((part & 0x80) === 0) return [value >>> 0, offset];
    shift += 7;
  }
  throw new Error("oversized LEB");
}

const chunks = [bytes.subarray(0, 8)];
let offset = 8, removed = false;
while (offset < bytes.length) {
  const start = offset, id = bytes[offset++];
  const [length, payload] = u32(offset); offset = payload + length;
  if (offset > bytes.length) throw new Error("section exceeds module");
  if (id === 9) throw new Error("refusing table strip with element section");
  if (id === 10 && bytes.subarray(payload, offset).includes(0x11)) throw new Error("refusing table strip with call_indirect opcode byte");
  if (id === 4) {
    const [count, afterCount] = u32(payload);
    if (count !== 1 || afterCount >= offset || bytes[afterCount] !== 0x70) throw new Error("unexpected table section");
    removed = true;
  } else chunks.push(bytes.subarray(start, offset));
}
if (!removed) throw new Error("compiler output did not contain the expected unused table");
fs.writeFileSync(output, Buffer.concat(chunks));
