/**
 * Record group-board demo clip (requires signed-in session).
 *
 * Auth options (first match wins):
 *   1. tools/demo-record/.auth/storage.json  (Playwright storageState)
 *   2. MOBIHYMN_DEMO_EMAIL + MOBIHYMN_DEMO_PASSWORD env vars
 *
 * Save a session after manual login:
 *   node record-board-demo.mjs --save-auth
 *
 * Record:
 *   node record-board-demo.mjs
 */
import { chromium, devices } from "playwright";
import path from "path";
import fs from "fs";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.resolve(__dirname, "../../docs/app-preview/videos");
const AUTH_DIR = path.join(__dirname, ".auth");
const STORAGE = path.join(AUTH_DIR, "storage.json");
const BASE = process.env.MOBIHYMN_URL || "http://127.0.0.1:5297";
const phone = devices["iPhone 13"];
const saveAuthOnly = process.argv.includes("--save-auth");

fs.mkdirSync(OUT, { recursive: true });
fs.mkdirSync(AUTH_DIR, { recursive: true });

async function dismissOverlays(page) {
  for (const name of [/^Skip$/i, /Maybe Later/i]) {
    try {
      const btn = page.getByRole("button", { name });
      if (await btn.isVisible({ timeout: 1500 })) {
        await btn.click();
        await page.waitForTimeout(400);
      }
    } catch { /* ignore */ }
  }
  // Notifications prompt banner (×)
  try {
    const dismissNotif = page.locator(".notif-prompt-dismiss").first();
    if (await dismissNotif.isVisible({ timeout: 800 })) {
      await dismissNotif.click();
      await page.waitForTimeout(300);
    }
  } catch { /* ignore */ }
  await page.evaluate(() => {
    const gate = document.getElementById("mh-pwa-gate");
    if (gate) gate.setAttribute("hidden", "");
    document.documentElement.classList.remove("mh-gate-active");
    document.querySelectorAll(".community-signin-host.is-open").forEach((el) => {
      el.classList.remove("is-open");
      el.style.display = "none";
    });
    document.querySelectorAll(".notif-prompt-bar").forEach((el) => el.remove());
  });
}

async function waitAppReady(page) {
  await page.waitForFunction(
    () => {
      const app = document.getElementById("app");
      return !!app && (app.innerText || "").length > 20;
    },
    null,
    { timeout: 90000 }
  );
  await dismissOverlays(page);
  await page.waitForTimeout(400);
}

/** Firebase auth restore from storageState can take several seconds. */
async function waitForSignedIn(page, timeoutMs = 20000) {
  await page.waitForFunction(
    () => {
      const email = window.__fb?.auth?.currentUser?.email;
      if (email) return true;
      const links = Array.from(document.querySelectorAll("a")).map((a) =>
        (a.textContent || "").trim()
      );
      return links.some((t) => /^Account$/i.test(t) || /^Sign out$/i.test(t));
    },
    null,
    { timeout: timeoutMs }
  );
  await dismissOverlays(page);
}

async function ensureSignedIn(page, context) {
  await page.goto(BASE + "/read/1", { waitUntil: "domcontentloaded", timeout: 90000 });
  await waitAppReady(page);

  try {
    await waitForSignedIn(page, fs.existsSync(STORAGE) ? 25000 : 5000);
    return;
  } catch {
    /* fall through to password login */
  }

  const email = process.env.MOBIHYMN_DEMO_EMAIL || "";
  const password = process.env.MOBIHYMN_DEMO_PASSWORD || "";
  if (!email || !password) {
    throw new Error(
      "Not signed in. Either:\n" +
        "  • Place a session at tools/demo-record/.auth/storage.json, or\n" +
        "  • Set MOBIHYMN_DEMO_EMAIL and MOBIHYMN_DEMO_PASSWORD, or\n" +
        "  • Run: node record-board-demo.mjs --save-auth"
    );
  }

  await page.goto(BASE + "/login", { waitUntil: "domcontentloaded" });
  await waitAppReady(page);
  await page.locator('input[type="email"]').fill(email);
  await page.locator('input[type="password"]').first().fill(password);
  await page.getByRole("button", { name: /^(Log in|Sign in)$/i }).click();
  await waitForSignedIn(page, 30000);
  await context.storageState({ path: STORAGE });
  console.log(`[demo] saved auth → ${STORAGE}`);
}

async function saveAuthInteractive() {
  const browser = await chromium.launch({ headless: false, channel: "chrome" });
  const context = await browser.newContext({
    ...phone,
    colorScheme: "dark",
  });
  const page = await context.newPage();
  await page.addInitScript(() => {
    window.__mhStandalone = true;
    window.mobihymnBoot = { isStandalone: () => true };
  });

  console.log("[demo] Log in in the browser window, then return here…");
  await page.goto(BASE + "/login", { waitUntil: "domcontentloaded" });
  await waitAppReady(page);
  await waitForSignedIn(page, 300000);
  await page.waitForTimeout(1500);
  await context.storageState({ path: STORAGE });
  console.log(`[demo] saved auth → ${STORAGE}`);
  await browser.close();
}

async function recordBoard() {
  const clipDir = path.join(OUT, "_raw", "08-group-board");
  fs.mkdirSync(clipDir, { recursive: true });

  const browser = await chromium.launch({ headless: true, channel: "chrome" });
  const contextOpts = {
    ...phone,
    recordVideo: { dir: clipDir, size: { width: 390, height: 844 } },
    colorScheme: "dark",
    // Granted notifications → no “enable alerts” banner during the demo.
    permissions: ["notifications"],
  };
  if (fs.existsSync(STORAGE)) {
    contextOpts.storageState = STORAGE;
    console.log("[demo] using saved auth storage");
  }
  const context = await browser.newContext(contextOpts);
  const page = await context.newPage();
  await page.addInitScript(() => {
    window.__mhStandalone = true;
    window.mobihymnBoot = { isStandalone: () => true };
  });

  console.log("[demo] recording 08-group-board…");
  await ensureSignedIn(page, context);

  await page.goto(BASE + "/read/215", { waitUntil: "domcontentloaded" });
  await waitAppReady(page);
  await waitForSignedIn(page, 25000);
  await dismissOverlays(page);
  await page.waitForTimeout(800);

  // Exact toolbar Board button (getByTitle("Board") also matched unrelated controls)
  const boardBtn = page.locator('button.tb-icon[title="Board"]');
  await boardBtn.waitFor({ state: "visible", timeout: 15000 });
  await dismissOverlays(page);
  await boardBtn.click();

  const pane = page.locator(".board-pane-host.is-open .board-pane");
  await pane.waitFor({ state: "visible", timeout: 15000 });
  await page.waitForTimeout(1200);

  const needLogin = await page.getByText(/Sign in to plan hymns/i).isVisible().catch(() => false);
  if (needLogin) {
    throw new Error("Board still requires sign-in — auth session missing or expired.");
  }

  // Groups list → first group (only if still on group picker)
  const groupRow = page.locator(".board-pane-host.is-open .board-pane-row").first();
  if (await groupRow.isVisible({ timeout: 2000 }).catch(() => false)) {
    await groupRow.click();
    await page.waitForTimeout(2000);
  }

  // If a setlist detail (hymn list) was restored, go back to the setlist boards
  const detailBack = page.locator(
    '.board-pane-host.is-open .board-pane-detail-header button[title="Back"]'
  );
  if (await detailBack.isVisible({ timeout: 1500 }).catch(() => false)) {
    await detailBack.click();
    await page.waitForTimeout(1600);
  }

  // Linger on setlist boards
  const setlistCards = page.locator(
    ".board-pane-host.is-open .board-pane-setlist .board-swipe-front.board-pane-card"
  );
  await setlistCards.first().waitFor({ state: "visible", timeout: 12000 });
  console.log(`[demo] setlists visible: ${await setlistCards.count()}`);
  await page.waitForTimeout(2400);

  // Tap a board that has hymns
  let target = setlistCards.filter({ hasText: /[1-9]\d*\s+hymns?/i }).first();
  if (!(await target.count())) target = setlistCards.first();
  await target.scrollIntoViewIfNeeded().catch(() => {});
  await page.waitForTimeout(400);
  await target.click();

  // Show hymn list
  const hymnCards = page.locator(".board-pane-host.is-open .board-pane-hymn-card");
  await hymnCards.first().waitFor({ state: "visible", timeout: 15000 });
  console.log(`[demo] hymns visible: ${await hymnCards.count()}`);
  await page.waitForTimeout(2000);

  const body = page.locator(".board-pane-host.is-open .board-pane-body").first();
  if (await body.count()) {
    await body.evaluate((el) => {
      el.scrollTop = Math.min(el.scrollHeight, 280);
    }).catch(() => {});
    await page.waitForTimeout(1800);
  } else {
    await page.waitForTimeout(2000);
  }

  await page.waitForTimeout(1000);
  await context.close();
  await browser.close();

  const files = fs.readdirSync(clipDir).filter((f) => f.endsWith(".webm"));
  if (!files.length) throw new Error("No video for 08-group-board");
  const dest = path.join(OUT, "08-group-board.webm");
  if (fs.existsSync(dest)) fs.unlinkSync(dest);
  fs.renameSync(path.join(clipDir, files[0]), dest);
  console.log(`[demo] wrote ${dest}`);
}

async function main() {
  if (saveAuthOnly) {
    await saveAuthInteractive();
    return;
  }
  await recordBoard();
}

main().catch((err) => {
  console.error(err.message || err);
  process.exit(1);
});
