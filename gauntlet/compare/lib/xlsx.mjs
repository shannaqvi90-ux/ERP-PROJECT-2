// Just enough of the .xlsx format to check an exported spreadsheet: read the zip's entries
// (stored or deflated) and the first worksheet's rows as text. No dependency.
import fs from 'node:fs';
import zlib from 'node:zlib';

/** Every entry of a zip file: name -> Buffer (via the central directory). */
export function unzip(file) {
  const buf = fs.readFileSync(file);
  let eocd = buf.length - 22;
  while (eocd >= 0 && buf.readUInt32LE(eocd) !== 0x06054b50) eocd--;
  if (eocd < 0) throw new Error(`${file}: not a zip file`);
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  const entries = {};
  for (let i = 0; i < count; i++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error(`${file}: bad central directory`);
    const method = buf.readUInt16LE(p + 10);
    const size = buf.readUInt32LE(p + 20);
    const nameLen = buf.readUInt16LE(p + 28);
    const extraLen = buf.readUInt16LE(p + 30);
    const commentLen = buf.readUInt16LE(p + 32);
    const local = buf.readUInt32LE(p + 42);
    const name = buf.toString('utf8', p + 46, p + 46 + nameLen);
    const dataStart = local + 30 + buf.readUInt16LE(local + 26) + buf.readUInt16LE(local + 28);
    const raw = buf.subarray(dataStart, dataStart + size);
    entries[name] = method === 0 ? raw : zlib.inflateRawSync(raw);
    p += 46 + nameLen + extraLen + commentLen;
  }
  return entries;
}

const decode = s => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, '&');

/** Rows of the first worksheet as arrays of cell text (shared and inline strings resolved). */
export function readFirstSheet(file) {
  const z = unzip(file);
  const shared = [];
  const sst = z['xl/sharedStrings.xml']?.toString('utf8') || '';
  for (const si of sst.match(/<si>[\s\S]*?<\/si>/g) || []) {
    shared.push(decode((si.match(/<t[^>]*>([\s\S]*?)<\/t>/g) || []).map(t => t.replace(/<[^>]+>/g, '')).join('')));
  }
  const sheetName = Object.keys(z).filter(n => /^xl\/worksheets\/sheet\d+\.xml$/.test(n)).sort()[0];
  const xml = z[sheetName].toString('utf8');
  return (xml.match(/<row[\s\S]*?<\/row>/g) || []).map(row => (row.match(/<c [^>]*?(?:\/>|>[\s\S]*?<\/c>)/g) || []).map(c => {
    const type = (c.match(/ t="([^"]+)"/) || [])[1];
    const v = (c.match(/<v>([\s\S]*?)<\/v>/) || [])[1];
    if (type === 's') return shared[Number(v)] ?? '';
    if (type === 'inlineStr') return decode((c.match(/<t[^>]*>([\s\S]*?)<\/t>/) || [])[1] || '');
    return v === undefined ? '' : decode(v);
  }));
}
