const { onDocumentWritten } = require("firebase-functions/v2/firestore");
const { onRequest, onCall, HttpsError } = require("firebase-functions/v2/https");
const { initializeApp, getApps } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");
const { getMessaging } = require("firebase-admin/messaging");
const { getStorage } = require("firebase-admin/storage");
const { renderOgPng } = require("./og-image");

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

const PUBLIC_ORIGIN = "https://mobihymn.web.app";
const SHARE_NUMBER_RE = /^\d{1,6}[a-zA-Z]{0,3}$/;

function hymnOgImageUrl(number) {
  return `${PUBLIC_ORIGIN}/og/${encodeURIComponent(number)}.png`;
}

function escapeHtmlAttr(value) {
  return String(value ?? "")
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function extractShareNumber(req) {
  const path = String(req.path || req.originalUrl || "").split("?")[0];
  const fromPath = path.match(/\/(?:share|hymn)\/([^/]+)\/?$/i);
  if (fromPath && fromPath[1])
    return decodeURIComponent(fromPath[1]).trim();
  const q = req.query && typeof req.query.n === "string" ? req.query.n.trim() : "";
  return q;
}

function firstLineFromHymnJson(data) {
  if (!data || typeof data !== "object") return "";
  const direct = typeof data.firstLine === "string" ? data.firstLine.trim() : "";
  if (direct) return direct;
  const lyrics = typeof data.lyrics === "string" ? data.lyrics : "";
  if (!lyrics) return "";
  const line = lyrics
    .replace(/\r\n/g, "\n")
    .split(/\n|<br\s*\/?>/i)
    .map((l) => l.replace(/<[^>]+>/g, "").trim())
    .find((l) => l.length > 0);
  return line || "";
}

async function lookupHymnFirstLine(number) {
  const target = `${HYMN_UPSTREAM}/hymn/api/hymns.dna?q=${encodeURIComponent(number)}`;
  const upstream = await fetch(target, {
    method: "GET",
    headers: { accept: "application/json" },
    redirect: "follow",
  });
  if (!upstream.ok) return "";
  const text = await upstream.text();
  if (!text || /^\s*Error:/i.test(text)) return "";
  try {
    const data = JSON.parse(text);
    if (Array.isArray(data))
      return firstLineFromHymnJson(data[0]);
    return firstLineFromHymnJson(data);
  } catch {
    return "";
  }
}

/**
 * Share landing page for crawlers (WhatsApp / iMessage / Slack, etc.).
 * Injects og:title with hymn number + first line, then sends browsers to /read/{n}.
 * Hosting rewrite: /share/** → hymnShare (keeps /read/** on the SPA, no extra latency).
 */
exports.hymnShare = onRequest(
  {
    region: "us-central1",
    cors: false,
    timeoutSeconds: 30,
    memory: "256MiB",
    invoker: "public",
  },
  async (req, res) => {
    try {
      if (req.method !== "GET" && req.method !== "HEAD") {
        res.status(405).send("Method not allowed");
        return;
      }

      const number = extractShareNumber(req);
      // Hymn ids are digits, optionally with a short letter/tune suffix (e.g. 77b).
      if (!number || !SHARE_NUMBER_RE.test(number)) {
        res.redirect(302, `${PUBLIC_ORIGIN}/read`);
        return;
      }

      let firstLine = "";
      try {
        firstLine = await lookupHymnFirstLine(number);
      } catch (e) {
        console.warn("hymnShare lookup failed", e?.message || e);
      }

      const readPath = `/read/${encodeURIComponent(number)}`;
      const shareUrl = `${PUBLIC_ORIGIN}/share/${encodeURIComponent(number)}`;
      const readUrl = `${PUBLIC_ORIGIN}${readPath}`;
      const title = firstLine
        ? `#${number} — ${firstLine}`
        : `Hymn #${number}`;
      const description = firstLine || "Read this hymn on MobiHymn";
      const escTitle = escapeHtmlAttr(title);
      const escDesc = escapeHtmlAttr(description);
      const escShare = escapeHtmlAttr(shareUrl);
      const escRead = escapeHtmlAttr(readUrl);
      const escNum = escapeHtmlAttr(number);
      const escImage = escapeHtmlAttr(hymnOgImageUrl(number));
      const jsRead = JSON.stringify(readPath);

      const html = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>${escTitle}</title>
  <meta name="description" content="${escDesc}" />
  <link rel="canonical" href="${escRead}" />
  <meta property="og:type" content="website" />
  <meta property="og:site_name" content="MobiHymn" />
  <meta property="og:locale" content="en_US" />
  <meta property="og:url" content="${escShare}" />
  <meta property="og:title" content="${escTitle}" />
  <meta property="og:description" content="${escDesc}" />
  <meta property="og:image" content="${escImage}" />
  <meta property="og:image:secure_url" content="${escImage}" />
  <meta property="og:image:type" content="image/png" />
  <meta property="og:image:width" content="1200" />
  <meta property="og:image:height" content="630" />
  <meta property="og:image:alt" content="${escTitle}" />
  <meta name="twitter:card" content="summary_large_image" />
  <meta name="twitter:title" content="${escTitle}" />
  <meta name="twitter:description" content="${escDesc}" />
  <meta name="twitter:image" content="${escImage}" />
  <meta name="twitter:image:alt" content="${escTitle}" />
  <meta http-equiv="refresh" content="0;url=${escRead}" />
  <script>location.replace(${jsRead});</script>
</head>
<body>
  <p><a href="${escRead}">Open hymn #${escNum} on MobiHymn</a></p>
</body>
</html>`;

      res.setHeader("Content-Type", "text/html; charset=utf-8");
      res.setHeader("Cache-Control", "public, max-age=3600");
      if (req.method === "HEAD") {
        res.status(200).end();
        return;
      }
      res.status(200).send(html);
    } catch (e) {
      console.error("hymnShare failed", e);
      if (!res.headersSent)
        res.status(302).setHeader("Location", `${PUBLIC_ORIGIN}/read`).end();
    }
  }
);

function extractOgNumber(req) {
  const path = String(req.path || req.originalUrl || "").split("?")[0];
  const m = path.match(/\/og\/([^/]+?)(?:\.png)?\/?$/i);
  return m && m[1] ? decodeURIComponent(m[1]).trim() : "";
}

/**
 * Per-hymn 1200x630 share thumbnail (number + first line).
 * Hosting rewrite: /og/** → hymnShareOg. Referenced by hymnShare's og:image.
 */
exports.hymnShareOg = onRequest(
  {
    region: "us-central1",
    cors: false,
    timeoutSeconds: 30,
    memory: "512MiB",
    invoker: "public",
  },
  async (req, res) => {
    try {
      if (req.method !== "GET" && req.method !== "HEAD") {
        res.status(405).send("Method not allowed");
        return;
      }

      const number = extractOgNumber(req);
      if (!number || !SHARE_NUMBER_RE.test(number)) {
        res.redirect(302, `${PUBLIC_ORIGIN}/og-image.png`);
        return;
      }

      let firstLine = "";
      try {
        firstLine = await lookupHymnFirstLine(number);
      } catch (e) {
        console.warn("hymnShareOg lookup failed", e?.message || e);
      }

      const png = await renderOgPng(number, firstLine);
      res.setHeader("Content-Type", "image/png");
      res.setHeader("Content-Length", String(png.length));
      // Missing first line is likely a transient upstream failure — don't pin it at the CDN.
      res.setHeader(
        "Cache-Control",
        firstLine
          ? "public, max-age=86400, s-maxage=604800"
          : "public, max-age=300, s-maxage=300"
      );
      if (req.method === "HEAD") {
        res.status(200).end();
        return;
      }
      res.status(200).send(png);
    } catch (e) {
      console.error("hymnShareOg failed", e);
      if (!res.headersSent)
        res.redirect(302, `${PUBLIC_ORIGIN}/og-image.png`);
    }
  }
);

/**
 * Longest-common-subsequence of two id arrays (same multiset of ids in each).
 * Ids NOT part of the LCS are the ones whose *relative* order changed — i.e.
 * they were actually dragged/reordered, as opposed to merely shifting position
 * because something else was added/removed elsewhere in the list.
 */
function computeMovedIds(oldSeq, newSeq) {
  const n = oldSeq.length;
  const m = newSeq.length;
  const dp = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
  for (let i = 1; i <= n; i++) {
    for (let j = 1; j <= m; j++) {
      dp[i][j] = oldSeq[i - 1] === newSeq[j - 1]
        ? dp[i - 1][j - 1] + 1
        : Math.max(dp[i - 1][j], dp[i][j - 1]);
    }
  }

  const inLcs = new Set();
  let i = n;
  let j = m;
  while (i > 0 && j > 0) {
    if (oldSeq[i - 1] === newSeq[j - 1]) {
      inLcs.add(oldSeq[i - 1]);
      i--;
      j--;
    } else if (dp[i - 1][j] >= dp[i][j - 1]) {
      i--;
    } else {
      j--;
    }
  }

  const moved = new Set();
  for (const id of oldSeq) {
    if (!inLcs.has(id)) moved.add(id);
  }
  return moved;
}

function hymnLabel(h) {
  if (!h) return "this entry";
  if (h.isSection) return `the "${h.sectionName || "section"}" section`;
  return `hymn #${h.hymnNumber || "?"}`;
}

function asText(value) {
  if (value == null || value === undefined) return null;
  return String(value);
}

function contentChanged(oldEntry, newEntry) {
  return (
    Boolean(oldEntry.isSection) !== Boolean(newEntry.isSection) ||
    asText(oldEntry.sectionName) !== asText(newEntry.sectionName) ||
    asText(oldEntry.hymnNumber) !== asText(newEntry.hymnNumber) ||
    asText(oldEntry.notes) !== asText(newEntry.notes)
  );
}

/**
 * Callable — the ONLY sanctioned way to write groups/{groupId}/boards/{listId}
 * (Firestore rules deny direct client create/update on that path). Enforces:
 *   - anyone in the group may add new hymns/sections;
 *   - only the group admin or the original adder may edit/remove/reorder an
 *     existing entry;
 *   - authorship (addedBy/addedByName) is stamped server-side and can never
 *     be spoofed or rewritten by a later editor, even an admin.
 * This mirrors (and backstops) the client-side checks in BoardPane.razor /
 * MobiHymn4.Maui BoardService, closing the gap where a technically-savvy
 * member could otherwise write straight to Firestore and bypass the UI.
 */
exports.boardUpdateList = onCall({ region: "us-central1", invoker: "public" }, async (request) => {
  const auth = request.auth;
  if (!auth || !auth.uid) {
    throw new HttpsError("unauthenticated", "Sign in to continue.");
  }
  if (auth.token?.email_verified !== true) {
    throw new HttpsError("failed-precondition", "Verify your email to continue.");
  }

  const data = request.data || {};
  const groupId = typeof data.groupId === "string" ? data.groupId : "";
  const listId = typeof data.listId === "string" ? data.listId : "";
  const hymns = Array.isArray(data.hymns) ? data.hymns : null;
  if (!groupId || !listId || !hymns) {
    throw new HttpsError("invalid-argument", "groupId, listId and hymns are required.");
  }

  const uid = auth.uid;
  const db = getFirestore();
  const groupRef = db.collection("groups").doc(groupId);
  const boardRef = groupRef.collection("boards").doc(listId);

  const [groupSnap, memberSnap, boardSnap] = await Promise.all([
    groupRef.get(),
    groupRef.collection("members").doc(uid).get(),
    boardRef.get(),
  ]);

  if (!groupSnap.exists) {
    throw new HttpsError("not-found", "Group not found.");
  }
  if (!memberSnap.exists) {
    throw new HttpsError("permission-denied", "You are not a member of this group.");
  }

  const group = groupSnap.data() || {};
  const member = memberSnap.data() || {};
  const isAdmin = member.isAdmin === true || group.createdBy === uid;

  const before = boardSnap.exists ? boardSnap.data() || {} : null;
  const oldHymns = Array.isArray(before?.hymns) ? before.hymns : [];
  const oldById = new Map(oldHymns.filter((h) => h && h.id).map((h) => [String(h.id), h]));

  const oldSeq = oldHymns.filter((h) => h && h.id).map((h) => String(h.id));
  const newSeqAll = hymns.filter((h) => h && h.id).map((h) => String(h.id));
  const newIdSet = new Set(newSeqAll);
  const commonOldSeq = oldSeq.filter((id) => newIdSet.has(id));
  const commonNewSeq = newSeqAll.filter((id) => oldById.has(id));
  const movedIds = isAdmin
    ? new Set()
    : computeMovedIds(commonOldSeq, commonNewSeq);

  let callerName = null;
  const finalHymns = [];
  for (const raw of hymns) {
    if (!raw || typeof raw !== "object" || !raw.id) continue;
    const id = String(raw.id);
    const old = oldById.get(id);
    const entry = {
      id,
      isSection: raw.isSection === true,
      sectionName: raw.sectionName == null || raw.sectionName === "" ? null : String(raw.sectionName),
      hymnNumber: raw.hymnNumber == null || raw.hymnNumber === "" ? null : String(raw.hymnNumber),
      sortOrder: typeof raw.sortOrder === "number" ? raw.sortOrder : 0,
      notes: raw.notes == null || raw.notes === "" ? null : String(raw.notes),
      updatedAt: raw.updatedAt ?? null,
    };

    if (!old) {
      // New entry — any group member may add one, but authorship is stamped
      // from the verified caller so it can never be spoofed.
      if (callerName === null) {
        const userSnap = await db.collection("users").doc(uid).get();
        callerName = pickName(userSnap.exists ? userSnap.data() : null) || pickName(member) || "";
      }
      entry.addedBy = uid;
      entry.addedByName = callerName;
      // Always stamp server time so onBoardWrite can detect adds/edits reliably
      // (client clocks / missing updatedAt previously caused silent notifications).
      entry.updatedAt = Date.now();
    } else {
      const needsOwnership = contentChanged(old, entry) || movedIds.has(id);
      if (needsOwnership && !isAdmin && old.addedBy !== uid) {
        throw new HttpsError(
          "permission-denied",
          `Only the group admin or ${old.addedByName || "the person who added it"} can change ${hymnLabel(old)}.`
        );
      }
      // Authorship can never be rewritten by whoever is editing, not even an admin.
      entry.addedBy = old.addedBy ?? null;
      entry.addedByName = old.addedByName ?? null;
      if (contentChanged(old, entry)) {
        entry.updatedAt = Date.now();
      } else {
        // Preserve prior updatedAt (number or Timestamp) when only sortOrder moved.
        entry.updatedAt = old.updatedAt ?? entry.updatedAt ?? null;
      }
    }

    finalHymns.push(entry);
  }

  for (const [id, old] of oldById) {
    if (newIdSet.has(id)) continue;
    if (!isAdmin && old.addedBy !== uid) {
      throw new HttpsError(
        "permission-denied",
        `Only the group admin or ${old.addedByName || "the person who added it"} can remove ${hymnLabel(old)}.`
      );
    }
  }

  await boardRef.set({
    name: typeof data.name === "string" ? data.name : "",
    createdAt: typeof data.createdAt === "number" ? data.createdAt : Date.now(),
    createdBy: typeof data.createdBy === "string" && data.createdBy ? data.createdBy : uid,
    updatedBy: uid,
    updatedAt: Date.now(),
    hymns: finalHymns,
    hymnCount: finalHymns.filter((h) => !h.isSection).length,
    entryCount: finalHymns.length,
  });

  return { ok: true };
});

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
    const before = event.data?.before?.data() || null;
    const actorUid = resolveActorUid(before, after);
    const actorName = await resolveActorName(db, actorUid, groupId);

    const membersSnap = await db.collection("groups").doc(groupId).collection("members").get();
    if (membersSnap.empty) {
      return;
    }

    const listLabel = formatListLabel(after.name, listId);
    const change = describeHymnChange(before, after);
    console.log(`[BoardNotify] ${groupId}/${listId} action=${change.action} count=${change.count} silent=${change.silent} actor=${actorUid || "(unknown)"}`);
    if (change.silent) {
      return; // reorder / section-move only — no content change
    }
    // Fail closed: without a known actor we used to notify everyone, including
    // the person who just edited. Prefer silence over self-spam.
    if (!actorUid) {
      console.warn(`[BoardNotify] skip all: missing actorUid for ${groupId}/${listId}`);
      return;
    }
    const who = actorName || "Someone";
    const title = `${who} ${change.verb} ${change.object}`;
    const body = `${groupName} · ${listLabel}`;
    const newHymnCount = Math.max(1, change.count);
    const unmutedRoles = ["projector", "accompaniment"];
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
      const memberRoles = Array.isArray(member.roles) ? member.roles : [];
      // Prefer group member roles, fall back to profile roles (leadership = unmuted by default).
      const effectiveRoles = memberRoles.length > 0 ? memberRoles : roles;
      const preferenceSet = user.notificationsPreferenceSet === true
        || user.notificationsPreferenceSet === "true";
      // Explicit Account mute always wins. Otherwise use role default (leadership unmuted).
      const userMuted = user.notificationsMuted === true
        || user.notificationsMuted === "true"
        || (!preferenceSet && !effectiveRoles.some((r) => unmutedRoles.includes(r)));
      const groupMuted = member.notificationsMuted === true
        || member.notificationsMuted === "true";
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
        console.log(`[BoardNotify] skip ${uid}: muted (user=${userMuted} group=${groupMuted})`);
        continue;
      }

      const tokensSnap = await db.collection("users").doc(uid).collection("fcmTokens").get();
      const seenTokens = new Set();
      const tokens = [];
      for (const d of tokensSnap.docs) {
        const row = d.data() || {};
        const token = row.token;
        if (typeof token !== "string" || token.length === 0 || seenTokens.has(token)) {
          continue;
        }
        seenTokens.add(token);
        const platform = typeof row.platform === "string" ? row.platform : "Web";
        tokens.push({ token, ref: d.ref, platform });
      }
      console.log(`[BoardNotify] ${uid}: ${tokens.length} token(s) to send (${tokens.map((t) => t.platform).join(",")})`);

      const collapseKey = `board-${listId}`;
      const boardPath = `/read?groupId=${encodeURIComponent(groupId)}&listId=${encodeURIComponent(listId)}`;
      for (const { token, ref, platform } of tokens) {
        const isWeb = !platform || /^web$/i.test(platform);
        // Web tokens: also send a visible webpush.notification so Chrome paints the
        // tray even when the push subscription was recently recreated. Keep title/body
        // in `data` for our SW click handler. Do NOT set fcmOptions.link (Firebase's
        // default click handler would hijack taps).
        const message = {
          token,
          data: {
            type: "boardUpdate",
            title,
            body,
            groupId,
            listId,
            date: listId,
            groupName,
            updatedBy: actorUid || "",
            updatedByName: actorName || "",
            changeAction: change.action,
            newHymnCount: String(newHymnCount),
            boardPath,
            is_silent_in_foreground: "true",
          },
        };
        if (!isWeb) {
          message.android = {
            priority: "high",
            collapseKey,
            notification: {
              title,
              body,
              channelId,
              tag: collapseKey,
              priority: "high",
              defaultSound: true,
            },
          };
          message.apns = {
            headers: { "apns-priority": "10" },
            payload: {
              aps: {
                alert: { title, body },
                sound: "default",
              },
            },
          };
        } else {
          // Visible web tray again (data-only alone needs a healthy SW + token;
          // after cleared browsing data that often fails silently). No fcmOptions.link
          // — that made Firebase's SW click handler hijack taps. Our SW registers
          // notificationclick BEFORE firebase.messaging() and stops propagation.
          message.webpush = {
            headers: { Urgency: "high", TTL: "86400" },
            notification: {
              title,
              body,
              icon: "/icon-192.png",
              badge: "/icon-192.png",
              tag: collapseKey,
              renotify: true,
            },
          };
        }

        sends.push(
          getMessaging()
            .send(message)
            .then((id) => {
              console.log(`FCM sent to ${uid} (${platform}) id=${id}`);
            })
            .catch(async (err) => {
              const code = err?.code || err?.errorInfo?.code || "";
              console.warn(`FCM send failed for ${uid} (${platform}): ${code || err?.message}`);
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

function asUid(value) {
  if (typeof value === "string") return value.trim();
  if (value && typeof value === "object" && typeof value.id === "string")
    return value.id.trim();
  return "";
}

/** Who made this board write — never the original list creator unless they actually wrote it. */
function resolveActorUid(before, after) {
  const updatedBy = asUid(after && after.updatedBy);
  if (updatedBy) return updatedBy;

  const hymns = Array.isArray(after && after.hymns) ? after.hymns : [];
  const oldIds = new Set(
    (Array.isArray(before && before.hymns) ? before.hymns : [])
      .filter((h) => h && h.id)
      .map((h) => String(h.id))
  );
  const addedBys = [];
  for (const h of hymns) {
    if (!h || !h.id || oldIds.has(String(h.id))) continue;
    const by = asUid(h.addedBy);
    if (by) addedBys.push(by);
  }
  if (addedBys.length > 0 && addedBys.every((u) => u === addedBys[0]))
    return addedBys[0];

  // Brand-new list only — using createdBy on later edits would notify the adder.
  if (!before) return asUid(after && after.createdBy);
  return "";
}

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

  const addedHymns = [];
  const updatedHymns = [];
  const deletedHymns = [];

  for (const hymn of afterHymns) {
    const id = hymn.id ? String(hymn.id) : "";
    if (!id) {
      addedHymns.push(hymn);
      continue;
    }
    const prev = beforeMap.get(id);
    if (!prev) {
      addedHymns.push(hymn);
      continue;
    }
    // Prefer content diff over updatedAt — client clocks / null stamps used to
    // make real edits look like silent renumbers and skip FCM entirely.
    if (
      contentChanged(prev, hymn) ||
      toMillis(hymn.updatedAt) > toMillis(prev.updatedAt)
    ) {
      updatedHymns.push(hymn);
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

  const added = addedHymns.length;
  const updated = updatedHymns.length;
  const deleted = deletedHymns.length;

  if (!before) {
    const count = Math.max(1, afterHymns.length);
    return {
      silent: false,
      action: "added",
      verb: "added",
      object: formatHymnObject(afterHymns),
      count,
      deletedHymns: [],
    };
  }

  let action = "updated";
  let count = updated;
  let hymnsForLabel = updatedHymns;
  if (deleted > 0) {
    action = "deleted";
    count = deleted;
    hymnsForLabel = deletedHymns;
  } else if (added > 0) {
    action = "added";
    count = added;
    hymnsForLabel = addedHymns;
  } else if (updated > 0) {
    action = "updated";
    count = updated;
    hymnsForLabel = updatedHymns;
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

  return {
    silent: false,
    action,
    verb: action,
    object: formatHymnObject(hymnsForLabel),
    count,
    deletedHymns,
  };
}

/** e.g. "hymn #12" or "hymns #12, #34, #56" (cap listed numbers). */
function formatHymnObject(hymns) {
  const nums = [];
  for (const h of hymns || []) {
    const n = h && (h.hymnNumber != null && String(h.hymnNumber).trim() !== "")
      ? String(h.hymnNumber).trim()
      : "";
    if (n) nums.push(n);
  }
  if (nums.length === 0) {
    const count = (hymns && hymns.length) || 0;
    return count <= 1 ? "a hymn" : `${count} hymns`;
  }
  if (nums.length === 1) return `hymn #${nums[0]}`;
  const shown = nums.slice(0, 3);
  const extra = nums.length - shown.length;
  const list = shown.map((n) => `#${n}`).join(", ");
  return extra > 0 ? `hymns ${list} +${extra}` : `hymns ${list}`;
}

const { getAuth } = require("firebase-admin/auth");

/** Comma-separated override via env DASHBOARD_ADMIN_EMAILS. */
const DEFAULT_DASHBOARD_ADMINS = ["tim.gandionco@gmail.com"];

function dashboardAdminEmails() {
  const fromEnv = String(process.env.DASHBOARD_ADMIN_EMAILS || "")
    .split(",")
    .map((s) => s.trim().toLowerCase())
    .filter(Boolean);
  return fromEnv.length > 0 ? fromEnv : DEFAULT_DASHBOARD_ADMINS;
}

function assertDashboardAdmin(auth) {
  if (!auth || !auth.uid) {
    throw new HttpsError("unauthenticated", "Sign in to continue.");
  }
  if (auth.token?.admin === true) {
    return;
  }
  const email = typeof auth.token?.email === "string"
    ? auth.token.email.trim().toLowerCase()
    : "";
  if (!email || !dashboardAdminEmails().includes(email)) {
    throw new HttpsError("permission-denied", "You don’t have access to the dashboard.");
  }
}

function normalizeDeviceId(raw) {
  const id = typeof raw === "string" ? raw.trim() : "";
  if (!id || id.length < 8 || id.length > 128 || !/^[A-Za-z0-9_-]+$/.test(id)) {
    return null;
  }
  return id;
}

function normalizePlatform(raw) {
  const p = typeof raw === "string" ? raw.trim().toLowerCase() : "";
  if (["pwa", "twa", "android", "ios", "web"].includes(p)) {
    return p;
  }
  return "web";
}

/**
 * Upsert a device/install record. Callable without auth (guests / pre-sign-in installs).
 * When signed in, links the device to the account.
 */
exports.registerDevice = onCall({ region: "us-central1", invoker: "public" }, async (request) => {
  const data = request.data || {};
  const deviceId = normalizeDeviceId(data.deviceId);
  if (!deviceId) {
    throw new HttpsError("invalid-argument", "A valid deviceId is required.");
  }

  const platform = normalizePlatform(data.platform);
  // Trust explicit installed flag; never demote a previously installed device.
  const uid = request.auth?.uid || null;
  const now = new Date();
  const db = getFirestore();
  const ref = db.collection("devices").doc(deviceId);
  const snap = await ref.get();
  const prev = snap.exists ? snap.data() || {} : {};
  const installed = data.installed === true || prev.installed === true;

  const next = {
    deviceId,
    platform,
    installed,
    uid: uid || prev.uid || null,
    hasAccount: !!(uid || prev.uid),
    userAgent: typeof data.userAgent === "string"
      ? data.userAgent.slice(0, 400)
      : (prev.userAgent || null),
    lastSeenAt: now,
    firstSeenAt: prev.firstSeenAt || now,
  };

  await ref.set(next, { merge: true });
  return {
    ok: true,
    deviceId,
    installed: next.installed,
    hasAccount: next.hasAccount,
  };
});

/**
 * Admin-only census for /dashboard.
 * - accounts: Firebase Auth users
 * - withProfile: Firestore users/{uid} docs
 * - installs: devices with installed=true
 * - withoutAccounts: installed devices not linked to a uid
 */
exports.adminGetDashboardStats = onCall({ region: "us-central1", invoker: "public" }, async (request) => {
  assertDashboardAdmin(request.auth);

  const db = getFirestore();
  const authApi = getAuth();

  let accountTotal = 0;
  let accountVerified = 0;
  let pageToken;
  do {
    const page = await authApi.listUsers(1000, pageToken);
    for (const user of page.users) {
      accountTotal += 1;
      if (user.emailVerified) accountVerified += 1;
    }
    pageToken = page.pageToken;
  } while (pageToken);

  const [profileCountSnap, installCountSnap, guestInstallSnap, deviceTotalSnap] = await Promise.all([
    db.collection("users").count().get(),
    db.collection("devices").where("installed", "==", true).count().get(),
    db.collection("devices").where("installed", "==", true).where("hasAccount", "==", false).count().get(),
    db.collection("devices").count().get(),
  ]);

  const platforms = { pwa: 0, twa: 0, android: 0, ios: 0, web: 0, other: 0 };
  const platformSnap = await db.collection("devices").where("installed", "==", true).select("platform").get();
  platformSnap.forEach((doc) => {
    const p = normalizePlatform(doc.get("platform"));
    if (Object.prototype.hasOwnProperty.call(platforms, p)) {
      platforms[p] += 1;
    } else {
      platforms.other += 1;
    }
  });

  return {
    generatedAt: new Date().toISOString(),
    accounts: {
      total: accountTotal,
      verified: accountVerified,
      withProfile: profileCountSnap.data().count || 0,
    },
    installs: {
      total: installCountSnap.data().count || 0,
      byPlatform: platforms,
    },
    withoutAccounts: guestInstallSnap.data().count || 0,
    devicesTracked: deviceTotalSnap.data().count || 0,
  };
});

