const fs = require("fs");
const path = require("path");
const opentype = require("opentype.js");

const WIDTH = 1200;
const HEIGHT = 630;
const PAD_X = 80;
const TEXT_WIDTH = WIDTH - PAD_X * 2;

const BG = "#2B2B2B";
const ACCENT = "#F5D200";
const INK = "#FFFFFF";
const MUTED = "#B8B8B8";

const LOGO_PATH =
  "M501 1Q529 1 556.5 1.0Q584 1 612 1Q612 58 612 114Q612 171 612.0 228.0Q612 285 612 341Q612 398 612 455Q556 455 500.0 455.0Q444 455 388 455Q388 435 388.0 414.5Q388 394 388 374Q424 374 460.0 374.0Q496 374 532 374Q532 301 532.0 228.5Q532 156 532 83Q531 83 529.5 83.0Q528 83 527 83Q496 83 466.0 83.0Q436 83 405 83Q403 83 401.0 83.5Q399 84 398 86Q381 102 364.5 118.5Q348 135 332 151Q331 152 329.0 152.5Q327 153 325 153Q316 154 306.5 154.0Q297 154 288 153Q286 153 284.0 152.5Q282 152 281 151Q265 134 248.5 118.0Q232 102 215 86Q214 85 212.0 84.0Q210 83 209 83Q177 83 145.5 83.0Q114 83 83 83Q82 83 81.5 83.0Q81 83 80 83Q80 155 80.0 227.5Q80 300 80 373Q116 373 152.0 373.0Q188 373 224 373Q224 394 224.0 414.0Q224 434 224 455Q168 455 112.0 455.0Q56 455 0 455Q0 398 0 341Q0 285 0.0 228.0Q0 171 0 114Q0 58 0 1Q1 1 2.5 1.0Q4 1 5 1Q63 1 122.0 1.0Q181 1 240 1Q242 1 244.0 1.5Q246 2 247 4Q261 17 274.5 30.5Q288 44 302 58Q303 59 304.0 60.0Q305 61 306 62Q307 61 308.0 60.5Q309 60 310 59Q323 46 335.5 33.5Q348 21 360 8Q364 4 368.5 2.0Q373 0 378 0Q409 1 439.5 1.0Q470 1 501 1Z";

const fonts = {};

/**
 * Text is emitted as glyph outlines rather than <text>, so the PNG never
 * depends on fonts installed in the Cloud Functions image.
 */
function font(name) {
  if (!fonts[name]) {
    const buf = fs.readFileSync(path.join(__dirname, "og-fonts", `NotoSerif-${name}.ttf`));
    fonts[name] = opentype.parse(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength));
  }
  return fonts[name];
}

function measure(fontName, text, size) {
  return font(fontName).getAdvanceWidth(text, size);
}

function textPath(fontName, text, x, baseline, size, fill) {
  const d = font(fontName).getPath(text, x, baseline, size).toPathData(2);
  return `<path d="${d}" fill="${fill}"/>`;
}

function wrapWords(fontName, text, size, maxWidth) {
  const words = text.split(/\s+/).filter(Boolean);
  const lines = [];
  let line = "";
  for (const word of words) {
    const candidate = line ? `${line} ${word}` : word;
    if (!line || measure(fontName, candidate, size) <= maxWidth) {
      line = candidate;
    } else {
      lines.push(line);
      line = word;
    }
  }
  if (line) lines.push(line);
  return lines;
}

function truncateToWidth(fontName, text, size, maxWidth) {
  let out = text;
  while (out.length > 1 && measure(fontName, `${out}…`, size) > maxWidth)
    out = out.slice(0, -1).trimEnd();
  return `${out.replace(/[\s,;:.!?-]+$/, "")}…`;
}

/** Largest size that fits the first line in at most 3 lines; ellipsize as a last resort. */
function layoutFirstLine(firstLine) {
  const text = String(firstLine || "").replace(/\s+/g, " ").trim();
  for (const [size, maxLines] of [[64, 2], [54, 3], [46, 3]]) {
    const lines = wrapWords("Italic", text, size, TEXT_WIDTH);
    if (lines.length <= maxLines) return { size, lines };
  }
  const size = 46;
  const lines = wrapWords("Italic", text, size, TEXT_WIDTH).slice(0, 3);
  lines[2] = truncateToWidth("Italic", lines[2], size, TEXT_WIDTH);
  return { size, lines };
}

/** Shrinks the hymn number if an unusually long id would overflow. */
function fitSize(fontName, text, preferred, maxWidth) {
  const w = measure(fontName, text, preferred);
  return w <= maxWidth ? preferred : Math.floor((preferred * maxWidth) / w);
}

function buildOgSvg(number, firstLine) {
  const num = `#${number}`;
  const logoH = 52;
  const logoW = Math.round((612 / 455) * logoH);
  const logoScale = logoH / 455;

  const parts = [
    `<rect width="${WIDTH}" height="${HEIGHT}" fill="${BG}"/>`,
    `<g transform="translate(${PAD_X} 64) scale(${logoScale.toFixed(5)})"><path d="${LOGO_PATH}" fill="${ACCENT}"/></g>`,
    textPath("Bold", "MobiHymn", PAD_X + logoW + 20, 106, 40, INK),
  ];

  if (firstLine) {
    const { size, lines } = layoutFirstLine(firstLine);
    const lineHeight = Math.round(size * 1.28);
    const numberSize = fitSize("Bold", num, lines.length > 2 ? 128 : 150, TEXT_WIDTH);
    const numberBaseline = lines.length > 2 ? 290 : 320;
    const firstBaseline = numberBaseline + Math.round(size * 1.55);
    parts.push(textPath("Bold", num, PAD_X, numberBaseline, numberSize, ACCENT));
    lines.forEach((l, i) => {
      parts.push(textPath("Italic", l, PAD_X, firstBaseline + i * lineHeight, size, INK));
    });
  } else {
    parts.push(textPath("Bold", num, PAD_X, 360, fitSize("Bold", num, 170, TEXT_WIDTH), ACCENT));
    parts.push(textPath("Italic", "Your pocket hymn companion", PAD_X, 450, 48, MUTED));
  }

  const footer = "mobihymn.web.app";
  const footerSize = 26;
  parts.push(
    textPath("Regular", footer, WIDTH - PAD_X - measure("Regular", footer, footerSize), HEIGHT - 44, footerSize, MUTED)
  );
  parts.push(`<rect y="${HEIGHT - 14}" width="${WIDTH}" height="14" fill="${ACCENT}"/>`);

  return `<svg xmlns="http://www.w3.org/2000/svg" width="${WIDTH}" height="${HEIGHT}" viewBox="0 0 ${WIDTH} ${HEIGHT}">${parts.join("")}</svg>`;
}

async function renderOgPng(number, firstLine) {
  const sharp = require("sharp");
  const svg = buildOgSvg(number, firstLine);
  return sharp(Buffer.from(svg)).png({ compressionLevel: 9 }).toBuffer();
}

module.exports = { buildOgSvg, renderOgPng, OG_WIDTH: WIDTH, OG_HEIGHT: HEIGHT };
