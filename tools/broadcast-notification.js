#!/usr/bin/env node
/*
 * Send a push notification to every registered device (users/{uid}/fcmTokens),
 * including devices where MobiHymn is closed. Only signed-in users who allowed
 * notifications have a token. Tapping it opens the app.
 *
 * Dry run (counts tokens):
 *   node tools/broadcast-notification.js --title "..." --body "..."
 * Send:
 *   node tools/broadcast-notification.js --title "..." --body "..." --send
 * Options:
 *   --platform web|mobile|all   (default all)
 *
 * Auth: Application Default Credentials (gcloud auth application-default login).
 */
const path = require("path");
const functionsModules = path.join(__dirname, "..", "firebase", "functions", "node_modules");
const { initializeApp, applicationDefault } = require(path.join(functionsModules, "firebase-admin", "lib", "app"));
const { getFirestore } = require(path.join(functionsModules, "firebase-admin", "lib", "firestore"));
const { getMessaging } = require(path.join(functionsModules, "firebase-admin", "lib", "messaging"));

const PROJECT_ID = "mobihymn";
const TAG = "mobihymn-broadcast";
const STALE_TOKEN_CODES = new Set([
  "messaging/registration-token-not-registered",
  "messaging/invalid-registration-token",
  "messaging/mismatched-credential",
]);

function arg(name, fallback = "") {
  const i = process.argv.indexOf(`--${name}`);
  return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : fallback;
}

const title = arg("title");
const body = arg("body");
const platformFilter = arg("platform", "all").toLowerCase();
const send = process.argv.includes("--send");

if (!title || !body) {
  console.error('Usage: node tools/broadcast-notification.js --title "..." --body "..." [--platform web|mobile|all] [--send]');
  process.exit(1);
}
if (!["web", "mobile", "all"].includes(platformFilter)) {
  console.error("--platform must be web, mobile or all");
  process.exit(1);
}

function buildMessage(token, isWeb) {
  if (isWeb) {
    // boardPath "/" makes the service worker's click handler open the app home.
    return {
      token,
      data: { type: "broadcast", title, body, boardPath: "/" },
      webpush: {
        headers: { Urgency: "high", TTL: "86400" },
        notification: { title, body, icon: "/icon-192.png", badge: "/icon-192.png", tag: TAG, renotify: true },
      },
    };
  }
  // No data payload: the phone app's tap handler opens the board panel when data is present.
  return {
    token,
    android: {
      priority: "high",
      notification: { title, body, tag: TAG, priority: "high", defaultSound: true },
    },
    apns: {
      headers: { "apns-priority": "10" },
      payload: { aps: { alert: { title, body }, sound: "default" } },
    },
  };
}

async function main() {
  initializeApp({ credential: applicationDefault(), projectId: PROJECT_ID });
  const db = getFirestore();

  const snap = await db.collectionGroup("fcmTokens").get();
  const seen = new Set();
  const targets = [];
  const users = new Set();
  const counts = { web: 0, mobile: 0 };
  for (const doc of snap.docs) {
    const row = doc.data() || {};
    const token = row.token;
    if (typeof token !== "string" || !token || seen.has(token)) continue;
    seen.add(token);
    const platform = typeof row.platform === "string" ? row.platform : "Web";
    const isWeb = /^web$/i.test(platform);
    if (platformFilter === "web" && !isWeb) continue;
    if (platformFilter === "mobile" && isWeb) continue;
    counts[isWeb ? "web" : "mobile"]++;
    users.add(doc.ref.parent.parent ? doc.ref.parent.parent.id : "");
    targets.push({ ref: doc.ref, message: buildMessage(token, isWeb) });
  }

  console.log(`Title: ${title}`);
  console.log(`Body:  ${body}`);
  console.log(`${targets.length} device(s) for ${users.size} user(s): ${counts.web} web, ${counts.mobile} phone`);
  if (!send) {
    console.log("Dry run - add --send to deliver.");
    return;
  }

  let ok = 0;
  let failed = 0;
  let removed = 0;
  for (let i = 0; i < targets.length; i += 500) {
    const batch = targets.slice(i, i + 500);
    const res = await getMessaging().sendEach(batch.map((t) => t.message));
    for (let j = 0; j < res.responses.length; j++) {
      const r = res.responses[j];
      if (r.success) {
        ok++;
        continue;
      }
      failed++;
      const code = (r.error && (r.error.code || (r.error.errorInfo && r.error.errorInfo.code))) || "";
      if (STALE_TOKEN_CODES.has(code)) {
        await batch[j].ref.delete().catch(() => {});
        removed++;
      }
    }
  }
  console.log(`Sent ${ok}, failed ${failed} (${removed} expired token(s) removed).`);
}

main().catch((err) => {
  console.error(err && err.message ? err.message : err);
  process.exit(1);
});
