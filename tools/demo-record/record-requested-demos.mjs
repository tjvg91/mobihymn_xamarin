/**
 * Record demo clips: numpad, voice number entry, MIDI controls, Selah AI.
 * Host: http://localhost:5297 (or MOBIHYMN_URL)
 *   node record-requested-demos.mjs
 */
import { chromium, devices } from "playwright";
import path from "path";
import fs from "fs";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const OUT = path.resolve(__dirname, "../../docs/app-preview/videos");
const BASE = process.env.MOBIHYMN_URL || "http://127.0.0.1:5297";
const phone = devices["iPhone 13"];

fs.mkdirSync(OUT, { recursive: true });

async function dismissOverlays(page) {
  for (const name of [/^Skip$/i, /Maybe Later/i]) {
    try {
      const btn = page.getByRole("button", { name });
      if (await btn.isVisible({ timeout: 2500 })) {
        await btn.click();
        await page.waitForTimeout(500);
      }
    } catch { /* ignore */ }
  }
  await page.evaluate(() => {
    const gate = document.getElementById("mh-pwa-gate");
    if (gate) gate.setAttribute("hidden", "");
    document.documentElement.classList.remove("mh-gate-active");
    document.querySelectorAll(".community-signin-host.is-open").forEach((el) => {
      el.classList.remove("is-open");
      el.style.display = "none";
    });
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
  await page.waitForTimeout(600);
}

async function recordClip(name, run) {
  const clipDir = path.join(OUT, "_raw", name);
  fs.mkdirSync(clipDir, { recursive: true });

  const browser = await chromium.launch({ headless: true, channel: "chrome" });
  const context = await browser.newContext({
    ...phone,
    recordVideo: { dir: clipDir, size: { width: 390, height: 844 } },
    colorScheme: "dark",
    permissions: ["microphone"],
  });
  const page = await context.newPage();

  await page.addInitScript(() => {
    window.__mhStandalone = true;
    window.mobihymnBoot = { isStandalone: () => true };

    // Fake speech recognition so voice demos work headless.
    class FakeRec {
      constructor() {
        this.lang = "en-US";
        this.interimResults = false;
        this.maxAlternatives = 1;
        this._timer = null;
      }
      start() {
        clearTimeout(this._timer);
        this._timer = setTimeout(() => {
          try {
            const transcript = window.__mhFakeTranscript || "seventy seven";
            const event = {
              results: [[{ transcript, confidence: 0.96 }]],
              resultIndex: 0,
            };
            event.results[0].isFinal = true;
            this.onresult && this.onresult(event);
          } catch { /* ignore */ }
          try { this.onend && this.onend(); } catch { /* ignore */ }
        }, 1600);
      }
      stop() {
        clearTimeout(this._timer);
        try { this.onend && this.onend(); } catch { /* ignore */ }
      }
      abort() { this.stop(); }
    }
    window.SpeechRecognition = FakeRec;
    window.webkitSpeechRecognition = FakeRec;
  });

  console.log(`[demo] recording ${name}…`);
  await page.goto(BASE + "/", { waitUntil: "domcontentloaded", timeout: 90000 });
  await waitAppReady(page);
  await run(page);
  await page.waitForTimeout(900);
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
  const only = (process.env.DEMO_ONLY || "").split(",").map((s) => s.trim()).filter(Boolean);
  const want = (name) => !only.length || only.includes(name);

  // 1) Numpad entry → open hymn
  if (want("04-numpad-entry"))
  await recordClip("04-numpad-entry", async (page) => {
    await page.goto(BASE + "/number", { waitUntil: "domcontentloaded" });
    await waitAppReady(page);
    await page.getByTitle("Keyboard").click();
    await page.waitForTimeout(700);
    for (const d of ["2", "1", "5"]) {
      await page.locator(".numpad-key", { hasText: new RegExp(`^${d}$`) }).click();
      await page.waitForTimeout(380);
    }
    await page.waitForTimeout(500);
    await page.getByRole("button", { name: "Open" }).click();
    await page.waitForTimeout(3200);
  });

  // 2) Voice number entry (fake STT → "seventy seven"); switching to voice auto-starts listen
  if (want("05-voice-entry"))
  await recordClip("05-voice-entry", async (page) => {
    await page.goto(BASE + "/number", { waitUntil: "domcontentloaded" });
    await waitAppReady(page);
    await page.evaluate(() => { window.__mhFakeTranscript = "seventy seven"; });
    await page.getByTitle("Voice").click();
    // Auto-listen kicks off; fake STT resolves after ~1.6s → opens #77
    await page.waitForTimeout(5500);
    await page.waitForTimeout(2200);
  });
  // 3) MIDI player settings (docked panel — avoid fullscreen overlay that intercepts taps)
  if (want("06-midi-settings"))
  await recordClip("06-midi-settings", async (page) => {
    await page.goto(BASE + "/read/1", { waitUntil: "domcontentloaded" });
    await waitAppReady(page);
    await page.waitForTimeout(2000);

    const midiToggle = page.getByRole("button", { name: /MIDI controls/i }).first();
    await midiToggle.waitFor({ state: "visible", timeout: 20000 });
    await midiToggle.click();
    await page.waitForTimeout(1400);

    // Docked (non-expanded) panel only
    const panel = page.locator(".read-midi-panel.is-open:not(.is-expanded)").first();
    await panel.waitFor({ state: "visible", timeout: 10000 });

    const instrument = panel.getByLabel("Instrument").first();
    if (await instrument.count()) {
      await instrument.selectOption({ index: 1 }).catch(() => {});
      await page.waitForTimeout(1000);
    }

    const faster = panel.getByRole("button", { name: "Faster" }).first();
    await faster.click({ force: true });
    await page.waitForTimeout(450);
    await faster.click({ force: true });
    await page.waitForTimeout(700);

    const upKey = panel.getByRole("button", { name: /Up a semitone/i }).first();
    if (await upKey.count()) {
      await upKey.click({ force: true });
      await page.waitForTimeout(800);
    }

    const muteAlto = panel.getByRole("button", { name: /^Mute Alto$/i }).first();
    if (await muteAlto.count()) {
      await muteAlto.click({ force: true });
      await page.waitForTimeout(900);
    }

    const play = page.getByRole("button", { name: /^Play$/i }).first();
    if (await play.count()) {
      await play.click();
      await page.waitForTimeout(2800);
      const pause = page.getByRole("button", { name: /^Pause$/i }).first();
      if (await pause.count()) await pause.click();
      await page.waitForTimeout(800);
    } else {
      await page.waitForTimeout(1800);
    }
  });

  // 4) Selah AI chat / recommendations
  if (want("07-selah-ai"))
  await recordClip("07-selah-ai", async (page) => {
    await page.goto(BASE + "/agent", { waitUntil: "domcontentloaded" });
    await waitAppReady(page);
    await page.waitForTimeout(1200);

    const box = page.locator("textarea, input[type='text'], .selah-composer-field input, .selah-composer textarea").first();
    await box.click();
    const prompt = "Suggest 3 joyful Easter hymns for a morning service";
    await box.fill(prompt);
    await page.waitForTimeout(600);

    const send = page.getByRole("button", { name: /send|ask|go/i }).first();
    if (await send.count()) await send.click();
    else await page.keyboard.press("Enter");

    // Wait for reply / suggestions
    await page.waitForTimeout(8000);
    const suggestion = page.locator(".selah-suggestion-main, .selah-suggestion, .selah-hit").first();
    if (await suggestion.count()) {
      await suggestion.click();
      await page.waitForTimeout(2500);
    } else {
      await page.waitForTimeout(2000);
    }
  });

  console.log("[demo] done — clips 04–07 in docs/app-preview/videos/");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
