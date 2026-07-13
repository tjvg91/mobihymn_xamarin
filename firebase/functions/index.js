const { onDocumentWritten } = require("firebase-functions/v2/firestore");
const { initializeApp } = require("firebase-admin/app");
const { getFirestore } = require("firebase-admin/firestore");
const { getMessaging } = require("firebase-admin/messaging");

initializeApp();

/**
 * Phase 4 — notify group members when a hymn list (board) changes.
 * Skips the actor and anyone with notificationsMuted == true.
 * Projector/accompaniment default unmuted on the client; others default muted.
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

    const membersSnap = await db.collection("groups").doc(groupId).collection("members").get();
    if (membersSnap.empty) {
      return;
    }

    const title = "Hymn list updated";
    const body = `${groupName} · ${after.name || listId}`;

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
      const muted = user.notificationsPreferenceSet
        ? user.notificationsMuted === true
        : !(roles.includes("projector") || roles.includes("accompaniment"));
      if (muted) {
        continue;
      }

      const notifRef = db.collection("users").doc(uid).collection("notifications").doc();
      await notifRef.set({
        type: "boardUpdate",
        groupId,
        listId,
        groupName,
        read: false,
        createdAt: new Date(),
      });

      const tokensSnap = await db.collection("users").doc(uid).collection("fcmTokens").get();
      const tokens = tokensSnap.docs
        .map((d) => d.data()?.token)
        .filter((t) => typeof t === "string" && t.length > 0);

      for (const token of tokens) {
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
              },
              android: {
                priority: "high",
                notification: {
                  channelId: "com.tjapps.mobihymn.board",
                },
              },
            })
            .catch(async (err) => {
              console.warn(`FCM send failed for ${uid}:`, err?.code || err?.message);
              if (
                err?.code === "messaging/registration-token-not-registered" ||
                err?.code === "messaging/invalid-registration-token"
              ) {
                const stale = tokensSnap.docs.find((d) => d.data()?.token === token);
                if (stale) {
                  await stale.ref.delete().catch(() => {});
                }
              }
            })
        );
      }
    }

    await Promise.all(sends);
  }
);
