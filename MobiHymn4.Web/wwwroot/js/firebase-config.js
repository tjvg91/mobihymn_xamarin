// Copy values from Firebase Console (Web app). Do not commit production secrets if restricted.
// vapidKey: Project settings → Cloud Messaging → Web Push certificates (Key pair).
// `self`, not `window`: service-worker.js importScripts this file and workers have no window.
self.mobihymnFirebaseConfig = {
  apiKey: "AIzaSyCkfwQTuYxSwOapUMiGVXlEBEOqFXGoMg8",
  databaseURL: "https://mobihymn.firebaseio.com",
  authDomain: "mobihymn.firebaseapp.com",
  projectId: "mobihymn",
  storageBucket: "mobihymn.appspot.com",
  messagingSenderId: "525477034225",
  appId: "1:525477034225:web:f68f15908b21cb216f9150",
  measurementId: "G-1KX2E3LE1K",
  vapidKey: "BKKNgawg9SMnmvq9IuAyBt_Mu1IRI7boi1zIvs4HYFVy72a7cJxPcVePJzj-xNe248ifLZcvQsieuMSnnTWy_mg",
  // Firebase Storage object path for hymn MIDI. {n} = hymn number (e.g. midi/h124.mid).
  midiPathTemplate: "midi/h{n}.mid"
};
