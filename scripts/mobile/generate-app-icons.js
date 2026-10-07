#!/usr/bin/env node
// Regenerate the mobile app icons and splash image from the Armada logo (src/Armada.Server/wwwroot/img/logo.png).
// Run from anywhere after `npm ci` in src/Armada.Mobile:  node scripts/mobile/generate-app-icons.js
// Outputs (committed): src/Armada.Mobile/assets/images/{icon,adaptive-foreground,adaptive-monochrome,splash-icon}.png
const path = require('path');

const root = path.resolve(__dirname, '..', '..');
const mobile = path.join(root, 'src', 'Armada.Mobile');
const Jimp = require(path.join(mobile, 'node_modules', 'jimp-compact'));

const LOGO = path.join(root, 'src', 'Armada.Server', 'wwwroot', 'img', 'logo.png');
const OUT = path.join(mobile, 'assets', 'images');
const BRAND_BG = 0x111827ff; // app icon background (dark slate); the logo is light grey

async function compose(size, logoFraction, background, tint) {
  const logo = await Jimp.read(LOGO);
  const target = Math.round(size * logoFraction);
  logo.scaleToFit(target, target);
  if (tint !== null) {
    logo.scan(0, 0, logo.bitmap.width, logo.bitmap.height, function recolor(x, y, idx) {
      this.bitmap.data[idx] = (tint >>> 24) & 0xff;
      this.bitmap.data[idx + 1] = (tint >>> 16) & 0xff;
      this.bitmap.data[idx + 2] = (tint >>> 8) & 0xff;
    });
  }
  const canvas = new Jimp(size, size, background);
  canvas.composite(logo, Math.round((size - logo.bitmap.width) / 2), Math.round((size - logo.bitmap.height) / 2));
  return canvas;
}

async function main() {
  // iOS / store icon: opaque, logo at 62% so the rounded mask never clips it.
  await (await compose(1024, 0.62, BRAND_BG, null)).writeAsync(path.join(OUT, 'icon.png'));
  // Android adaptive foreground: transparent, logo inside the 66% safe zone.
  await (await compose(1024, 0.5, 0x00000000, null)).writeAsync(path.join(OUT, 'adaptive-foreground.png'));
  // Android 13 themed (monochrome) icon: white silhouette, the launcher tints it.
  await (await compose(1024, 0.5, 0x00000000, 0xffffffff)).writeAsync(path.join(OUT, 'adaptive-monochrome.png'));
  // Splash: transparent logo; the splash background color is set in app.config.ts.
  await (await compose(512, 0.9, 0x00000000, null)).writeAsync(path.join(OUT, 'splash-icon.png'));
  console.log('icons written to ' + OUT);
}

main().catch((err) => { console.error(err); process.exit(1); });
