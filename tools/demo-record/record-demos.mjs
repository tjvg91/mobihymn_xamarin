/**
 * Record short mobile demo clips of MobiHymn (local host).
 * Usage: node record-demos.mjs
 * Requires: host at http://localhost:5297 and `npm i playwright` in this folder.
 * Uses installed Google Chrome (channel: chrome) — no Chromium download needed.
 */
import { chromium, devices } from "playwright";
import path from "path";
import fs from "fs";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.resolve(__dirname, "../../docs/app-preview/videos");
const BASE = process.env.MOBIHYMN_URL || "http://localhost:5297";
const phone = devices["iPhone 13"];

fs.mkdirSync(OUT, { recursive: true });

async function dismissOverlays(page) {
  const skip = page.getByRole("button", { name: /^Skip$/i });
  try {
    if (await skip.isVisible({ timeout: 4000 })) {
      await skip.click();
      await page.waitForTimeout(700);
    }
  } catch {
    /* already past intro */
  }

  const later = page.getByRole("button", { name: /Maybe Later/i });
  try {
    if (await later.isVisible({ timeout: 2500 })) {
      await later.click();
      await page.waitForTimeout(500);
    }
  } catch {
    /* prompt not shown */
  }

  // Hard-hide leftover overlays so clicks aren't intercepted.
  await page.evaluate(() => {
    document.querySelectorAll(".community-signin-host.is-open").forEach((el) => {
      el.classList.remove("is-open");
      el.style.display = "none";
    });
  });
}

async function waitAppReady(page) {
  await page.waitForFunction(
    () => {
      const gate = document.getElementById("mh-pwa-gate");
      const gated = gate && !gate.hasAttribute("hidden");
      const app = document.getElementById("app");
      return !!app && !gated && (app.innerText || "").length > 20;
    },
    null,
    { timeout: 90000 }
  );
  await page.evaluate(() => {
    const gate = document.getElementById("mh-pwa-gate");
    if (gate) gate.setAttribute("hidden", "");
    document.documentElement.classList.remove("mh-gate-active");
  });
  await page.waitForTimeout(900);
}

async function recordClip(name, run) {
  const clipDir = path.join(OUT, "_raw", name);
  fs.mkdirSync(clipDir, { recursive: true });

  const browser = await chromium.launch({
    headless: true,
    channel: "chrome",
  });
  const context = await browser.newContext({
    ...phone,
    recordVideo: { dir: clipDir, size: { width: 390, height: 844 } },
    colorScheme: "dark",
  });
  const page = await context.newPage();

  await page.addInitScript(() => {
    window.__mhStandalone = true;
    window.mobihymnBoot = { isStandalone: () => true };
  });

  console.log(`[demo] recording ${name}…`);
  await page.goto(BASE + "/", { waitUntil: "domcontentloaded", timeout: 90000 });
  await waitAppReady(page);
  await dismissOverlays(page);
  await run(page);
  await page.waitForTimeout(1000);
  await context.close();
  await browser.close();

  const files = fs.readdirSync(clipDir).filter((f) => f.endsWith(".webm"));
  if (!files.length) throw new Error(`No video for ${name}`);
  const dest = path.join(OUT, `${name}.webm`);
  if (fs.existsSync(dest)) fs.unlinkSync(dest);
  fs.renameSync(path.join(clipDir, files[0]), dest);
  console.log(`[demo] wrote ${dest}`);
  return dest;
}

async function main() {
  await recordClip("01-open-hymn", async (page) => {
    await page.goto(BASE + "/read/215", { waitUntil: "domcontentloaded" });
    await waitAppReady(page);
    await dismissOverlays(page);
    await page.waitForTimeout(2800);
    await page.mouse.wheel(0, 480);
    await page.waitForTimeout(1400);
    await page.mouse.wheel(0, 480);
    await page.waitForTimeout(1600);
  });

  await recordClip("02-search", async (page) => {
    await page.goto(BASE + "/search", { waitUntil: "domcontentloaded" });
    await waitAppReady(page);
    await dismissOverlays(page);
    const input = page.locator(".search-bar-input, input[aria-label='Search hymns']").first();
    await input.click();
    await input.fill("amazing");
    await page.waitForTimeout(500);
    await page.keyboard.press("Enter");
    await page.waitForTimeout(3200);
    const hit = page.locator(".search-hit-main, .search-group-hymn, .search-hit").first();
    if (await hit.count()) {
      await hit.click();
      await page.waitForTimeout(2800);
    }
  });

  await recordClip("03-number-entry", async (page) => {
    await page.goto(BASE + "/number", { waitUntil: "domcontentloaded" });
    await waitAppReady(page);
    await dismissOverlays(page);
    await page.waitForTimeout(900);
    for (const digit of ["7", "7"]) {
      const key = page.locator("button", { hasText: new RegExp(`^${digit}$`) }).first();
      if (await key.count()) {
        await key.click();
        await page.waitForTimeout(400);
      }
    }
    const go = page.getByRole("button", { name: /open|go|enter|ok|done/i }).first();
    if (await go.count()) {
      await go.click();
      await page.waitForTimeout(2800);
    } else {
      await page.waitForTimeout(1600);
    }
  });

  console.log("[demo] done — clips in docs/app-preview/videos/");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
