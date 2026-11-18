// Asset authoring only: requires sharp. Normal .NET builds use the generated ICO.
// Keep the SVG as the editable source and generate all Windows DPI sizes together.
const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require('sharp');

(async () => {
  const root = path.resolve(__dirname, '..');
  const source = path.join(root, 'src', 'ProxyGAyer', 'Assets', 'ProxyGAyer.svg');
  const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
  const frames = await Promise.all(sizes.map(size => sharp(source).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + sizes.length * 16);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  sizes.forEach((size, index) => {
    const entry = 6 + index * 16;
    header[entry] = size === 256 ? 0 : size;
    header[entry + 1] = size === 256 ? 0 : size;
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(frames[index].length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += frames[index].length;
  });
  await fs.writeFile(path.join(root, 'src', 'ProxyGAyer', 'Assets', 'ProxyGAyer.ico'), Buffer.concat([header, ...frames]));
  await sharp(source).resize(512, 512).png().toFile(path.join(root, 'docs', 'assets', 'app-icon.png'));
  console.log('Generated Windows ICO: ' + sizes.join(', ') + ' px; PNG preview: docs/assets/app-icon.png');
})().catch(error => { console.error(error); process.exitCode = 1; });
