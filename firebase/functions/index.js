const { onDocumentWritten } = require("firebase-functions/v2/firestore");
const { onRequest } = require("firebase-functions/v2/https");
const { initializeApp, getApps } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");
const { getMessaging } = require("firebase-admin/messaging");
const { getStorage } = require("firebase-admin/storage");

if (getApps().length === 0) {
  initializeApp({
    projectId: "mobihymn",
    storageBucket: "mobihymn.appspot.com",
  });
}

const HYMN_UPSTREAM = "http://157.230.9.81";
const STORAGE_BUCKET = "mobihymn.appspot.com";
const MIDI_PATH_TEMPLATE = "midi/h{n}.mid";

function midiObjectPath(number) {
  const n = String(number || "").trim();
  if (!/^\d{1,6}$/.test(n)) return null;
  return MIDI_PATH_TEMPLATE.replace("{n}", n);
}

async function sendMidiBytes(res, method, bytes) {
  if (!bytes || bytes.length === 0) {
    res.status(404).end();
    return;
  }
  res.setHeader("Content-Type", "audio/midi");
  res.setHeader("Content-Length", String(bytes.length));
  res.setHeader("Cache-Control", "private, max-age=3600");
  if (method === "HEAD") {
    res.status(200).end();
    return;
  }
  res.status(200).send(bytes);
}

/**
 * Same-origin hymn API proxy for Firebase Hosting (mirrors MobiHymn4.Web.Host).
 * /api/hymn/... → http://157.230.9.81/hymn/...
 */
exports.hymnProxy = onRequest(
  {
    region: "us-central1",
    cors: false,
    timeoutSeconds: 300,
    memory: "512MiB",
    invoker: "public",
  },
  async (req, res) => {
    try {
      const prefix = "/api/hymn";
      let pathAndQuery = req.originalUrl || req.url || "/";
      if (pathAndQuery.startsWith(prefix))
        pathAndQuery = pathAndQuery.slice(prefix.length);
      if (!pathAndQuery.startsWith("/"))
        pathAndQuery = "/" + pathAndQuery;

      const upstreamPath = "/hymn" + pathAndQuery;
      const target = HYMN_UPSTREAM + upstreamPath;

      const headers = {};
      const contentType = req.get("content-type");
      if (contentType) headers["content-type"] = contentType;
      const accept = req.get("accept");
      if (accept) headers["accept"] = accept;
      const range = req.get("range");
      if (range) headers["range"] = range;

      const init = {
        method: req.method,
        headers,
        redirect: "manual",
      };
      if (req.method !== "GET" && req.method !== "HEAD" && req.rawBody) {
        init.body = req.rawBody;
      }

      const upstream = await fetch(target, init);
      // Buffer + strip encoding headers so Firebase Hosting CDN can serve the response.
      // Forwarding gzip/chunked streams from the origin causes Hosting "Internal Error".
      const skip = new Set([
        "transfer-encoding",
        "connection",
        "keep-alive",
        "content-encoding",
        "content-length",
      ]);
      res.status(upstream.status);
      upstream.headers.forEach((value, key) => {
        if (!skip.has(key.toLowerCase()))
          res.setHeader(key, value);
      });

      if (req.method === "HEAD") {
        res.end();
        return;
      }

      const bytes = Buffer.from(await upstream.arrayBuffer());
      res.setHeader("Content-Length", String(bytes.length));
      res.send(bytes);
    } catch (e) {
      console.error("hymnProxy failed", e);
      if (!res.headersSent)
        res.status(502).send("Upstream hymn proxy failed");
    }
  }
);

/**
 * MIDI download proxy — browsers cannot read Storage media (403/CORS).
 * Prefer: /api/midi?n=<hymnNumber>  (Admin SDK, bypasses Storage rules)
 * Legacy: /api/midi?u=<firebasestorage download URL>
 */
exports.midiProxy = onRequest(
  {
    region: "us-central1",
    cors: false,
    timeoutSeconds: 60,
    memory: "256MiB",
    invoker: "public",
  },
  async (req, res) => {
    try {
      if (req.method !== "GET" && req.method !== "HEAD") {
        res.status(405).send("Method not allowed");
        return;
      }

      const n = typeof req.query.n === "string" ? req.query.n : "";
      const objectPath = midiObjectPath(n);
      if (objectPath) {
        try {
          const bucket = getStorage().bucket(STORAGE_BUCKET);
          const [bytes] = await bucket.file(objectPath).download();
          await sendMidiBytes(res, req.method, bytes);
          return;
        } catch (e) {
          const code = e?.code;
          if (code === 404 || code === "ENOENT" || /No such object/i.test(String(e?.message || ""))) {
            res.status(404).end();
            return;
          }
          throw e;
        }
      }

      const u = typeof req.query.u === "string" ? req.query.u : "";
      let uri;
      try {
        uri = new URL(u);
      } catch {
        res.status(400).send("Missing hymn number or download URL.");
        return;
      }

      if (uri.protocol !== "https:") {
        res.status(400).send("Invalid URL");
        return;
      }
      if (uri.hostname !== "firebasestorage.googleapis.com") {
        res.status(400).send("Invalid host");
        return;
      }
      const prefix = `/v0/b/${STORAGE_BUCKET}/`;
      if (!uri.pathname.startsWith(prefix)) {
        res.status(400).send("Invalid bucket");
        return;
      }

      const upstream = await fetch(uri.toString(), { method: "GET", redirect: "follow" });
      if (upstream.status === 404) {
        res.status(404).end();
        return;
      }
      if (!upstream.ok) {
        res.status(upstream.status).end();
        return;
      }

      const bytes = Buffer.from(await upstream.arrayBuffer());
      await sendMidiBytes(res, req.method, bytes);
    } catch (e) {
      console.error("midiProxy failed", e);
      if (!res.headersSent)
        res.status(502).send("MIDI proxy failed");
    }
  }
);

/**
 * Phase 4 — notify group members when a hymn list (board) changes.
 * Skips the actor for FCM.
 * Always writes in-app notification docs for other members (badges).
 * Skips FCM / tray for muted users (Account mute, group mute, or role default).
 * Sets suppressPush on muted docs so the app never posts a local tray either.
 */
exports.onBoardWrite = onDocumentWritten(
  "groups/{groupId}/boards/{listId}",
  async (event) => {
    const groupId = event.params.groupId;
    const listId = event.params.listId;
    const after = event.data?.after?.data();
    if (!after) {
      return; // deleted
    }

    const db = getFirestore();
    const groupSnap = await db.collection("groups").doc(groupId).get();
    const groupName = groupSnap.exists ? groupSnap.data().name || "Your group" : "Your group";
    const actorUid = after.updatedBy || "";
    const actorName = await resolveActorName(db, actorUid, groupId);

    const membersSnap = await db.collection("groups").doc(groupId).collection("members").get();
    if (membersSnap.empty) {
      return;
    }

    const listLabel = formatListLabel(after.name, listId);
    const change = describeHymnChange(event.data?.before?.data(), after);
    if (change.silent) {
      return; // reorder / section-move only — no content change
    }
    const who = actorName || "Someone";
    const title = `${who} ${change.verb} ${change.object}`;
    const body = `${groupName} · ${listLabel}`;
    const newHymnCount = Math.max(1, change.count);
    const unmutedRoles = ["pastor", "worshipLeader", "projector", "accompaniment"];
    const channelId = "com.tjapps.mobihymn.board";

    const sends = [];
    for (const memberDoc of membersSnap.docs) {
      const uid = memberDoc.id;
      if (!uid || uid === actorUid) {
        continue;
      }

      const userSnap = await db.collection("users").doc(uid).get();
      if (!userSnap.exists) {
        continue;
      }

      const user = userSnap.data() || {};
      const roles = Array.isArray(user.roles) ? user.roles : [];
      const member = memberDoc.data() || {};
      // Explicit Account mute always wins. Otherwise use role default (leadership unmuted).
      const userMuted = user.notificationsMuted === true
        || (!user.notificationsPreferenceSet
          && !roles.some((r) => unmutedRoles.includes(r)));
      const groupMuted = member.notificationsMuted === true;
      const muted = userMuted || groupMuted;

      // In-app badges for every other member; tray/FCM only when not muted.
      const notifRef = db.collection("users").doc(uid).collection("notifications").doc();
      const notif = {
        type: "boardUpdate",
        groupId,
        listId,
        groupName,
        updatedBy: actorUid || null,
        updatedByName: actorName || null,
        changeAction: change.action,
        newHymnCount,
        read: false,
        createdAt: new Date(),
        title,
        body,
        // Client must not post a local tray when muted (FCM is also skipped below).
        suppressPush: muted,
      };
      if (Array.isArray(change.deletedHymns) && change.deletedHymns.length > 0) {
        notif.deletedHymns = change.deletedHymns;
      }
      await notifRef.set(notif);

      if (muted) {
        continue;
      }

      const tokensSnap = await db.collection("users").doc(uid).collection("fcmTokens").get();
      const seenTokens = new Set();
      const tokens = [];
      for (const d of tokensSnap.docs) {
        const token = d.data()?.token;
        if (typeof token !== "string" || token.length === 0 || seenTokens.has(token)) {
          continue;
        }
        seenTokens.add(token);
        tokens.push({ token, ref: d.ref });
      }

      const collapseKey = `board-${listId}`;
      for (const { token, ref } of tokens) {
        sends.push(
          getMessaging()
            .send({
              token,
              notification: { title, body },
              data: {
                type: "boardUpdate",
                groupId,
                listId,
                date: listId,
                groupName,
                updatedBy: actorUid || "",
                updatedByName: actorName || "",
                changeAction: change.action,
                newHymnCount: String(newHymnCount),
                // Plugin.Firebase: skip its foreground local Notify (BoardLocalNotifier handles it).
                is_silent_in_foreground: "true",
              },
              android: {
                priority: "high",
                collapseKey,
                notification: {
                  channelId,
                  tag: collapseKey,
                  priority: "high",
                  defaultSound: true,
                },
              },
              webpush: {
                headers: { Urgency: "high" },
                notification: {
                  title,
                  body,
                  icon: "/icon-192.png",
                  badge: "/icon-192.png",
                  tag: collapseKey,
                  renotify: true,
                },
                fcmOptions: {
                  // Relative deep-link handled by firebase-messaging-sw.js click handler via data.
                  link: "/",
                },
              },
            })
            .then(() => {
              console.log(`FCM sent to ${uid}`);
            })
            .catch(async (err) => {
              const code = err?.code || err?.errorInfo?.code || "";
              console.warn(`FCM send failed for ${uid}: ${code || err?.message}`);
              if (
                code === "messaging/registration-token-not-registered" ||
                code === "messaging/invalid-registration-token" ||
                code === "messaging/mismatched-credential"
              ) {
                await ref.delete().catch(() => {});
              }
            })
        );
      }
    }

    await Promise.all(sends);
  }
);

async function resolveActorName(db, actorUid, groupId) {
  if (!actorUid) {
    return "";
  }

  try {
    const [userSnap, memberSnap] = await Promise.all([
      db.collection("users").doc(actorUid).get(),
      db.collection("groups").doc(groupId).collection("members").doc(actorUid).get(),
    ]);

    const user = userSnap.exists ? userSnap.data() || {} : {};
    const member = memberSnap.exists ? memberSnap.data() || {} : {};
    return (
      pickName(member) ||
      pickName(user) ||
      ""
    );
  } catch (err) {
    console.warn(`resolveActorName failed: ${err?.message || err}`);
    return "";
  }
}

function pickName(data) {
  if (!data || typeof data !== "object") {
    return "";
  }
  const nickname = typeof data.nickname === "string" ? data.nickname.trim() : "";
  if (nickname) return nickname;
  const displayName = typeof data.displayName === "string" ? data.displayName.trim() : "";
  if (displayName) return displayName;
  const first = typeof data.firstName === "string" ? data.firstName.trim() : "";
  const last = typeof data.lastName === "string" ? data.lastName.trim() : "";
  const full = `${first} ${last}`.trim();
  return full;
}

function toMillis(ts) {
  if (!ts) return 0;
  if (typeof ts.toMillis === "function") return ts.toMillis();
  if (typeof ts.seconds === "number") return ts.seconds * 1000;
  if (typeof ts._seconds === "number") return ts._seconds * 1000;
  const parsed = Date.parse(ts);
  return Number.isFinite(parsed) ? parsed : 0;
}

function nonSectionHymns(doc) {
  const hymns = Array.isArray(doc?.hymns) ? doc.hymns : [];
  return hymns.filter((h) => h && !h.isSection);
}

/** Prefer long month: July 26, 2026 */
function formatListLabel(name, listId) {
  const fromId = formatDateKey(listId);
  if (fromId) return fromId;
  if (typeof name === "string" && name.trim()) return name.trim();
  return listId || "Hymn list";
}

function formatDateKey(listId) {
  if (typeof listId !== "string") return "";
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(listId.trim());
  if (!match) return "";
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (!year || month < 1 || month > 12 || day < 1 || day > 31) return "";
  const months = [
    "January", "February", "March", "April", "May", "June",
    "July", "August", "September", "October", "November", "December",
  ];
  return `${months[month - 1]} ${day}, ${year}`;
}

/**
 * Decide added / updated / deleted from hymn array diff.
 * Priority when mixed: deleted > added > updated.
 */
function describeHymnChange(before, after) {
  const beforeHymns = nonSectionHymns(before);
  const afterHymns = nonSectionHymns(after);
  const beforeMap = new Map(
    beforeHymns.filter((h) => h.id).map((h) => [String(h.id), h])
  );
  const afterMap = new Map(
    afterHymns.filter((h) => h.id).map((h) => [String(h.id), h])
  );

  let added = 0;
  let updated = 0;
  const deletedHymns = [];

  for (const hymn of afterHymns) {
    const id = hymn.id ? String(hymn.id) : "";
    if (!id) {
      added += 1;
      continue;
    }
    const prev = beforeMap.get(id);
    if (!prev) {
      added += 1;
      continue;
    }
    if (toMillis(hymn.updatedAt) > toMillis(prev.updatedAt)) {
      updated += 1;
    }
  }

  for (const hymn of beforeHymns) {
    const id = hymn.id ? String(hymn.id) : "";
    if (!id || afterMap.has(id)) continue;
    deletedHymns.push({
      id,
      hymnNumber: typeof hymn.hymnNumber === "string" ? hymn.hymnNumber : String(hymn.hymnNumber || ""),
      notes: typeof hymn.notes === "string" ? hymn.notes : "",
      sortOrder: typeof hymn.sortOrder === "number" ? hymn.sortOrder : 0,
      addedByName: typeof hymn.addedByName === "string" ? hymn.addedByName : "",
    });
  }

  const deleted = deletedHymns.length;

  if (!before) {
    const count = Math.max(1, afterHymns.length);
    return {
      silent: false,
      action: "added",
      verb: "added",
      object: count === 1 ? "a hymn" : `${count} hymns`,
      count,
      deletedHymns: [],
    };
  }

  let action = "updated";
  let count = updated;
  if (deleted > 0) {
    action = "deleted";
    count = deleted;
  } else if (added > 0) {
    action = "added";
    count = added;
  } else if (updated > 0) {
    action = "updated";
    count = updated;
  } else {
    // Sort-order / section moves only — nothing to notify about.
    return {
      silent: true,
      action: "updated",
      verb: "updated",
      object: "a hymn",
      count: 0,
      deletedHymns: [],
    };
  }

  const singular = count === 1;
  return {
    silent: false,
    action,
    verb: action,
    object: singular ? "a hymn" : `${count} hymns`,
    count,
    deletedHymns,
  };
}

