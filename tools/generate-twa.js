/**
 * Non-interactive Bubblewrap TWA project generator for MobiHymn PWA.
 * Icons are fetched from a local static server; host is the production domain.
 */
const path = require("path");
const fs = require("fs");
const { TwaManifest, TwaGenerator } = require("@bubblewrap/core");

async function main() {
  const root = path.resolve(__dirname);
  const outDir = path.join(root, "android-twa");
  const keystore = path.resolve(root, "..", "MobiHymn4.Maui", "mobihymn_keystore.p12");
  const iconUrl = "http://127.0.0.1:5173/icon-512.png";
  const host = process.env.MOBIHYMN_TWA_HOST || "mobihymn.web.app";
  // Fetch icons/manifest from local publish; TWA launch host stays production.
  const localOrigin = "http://127.0.0.1:5173";
  const webManifestUrl = `${localOrigin}/manifest.webmanifest`;

  if (!fs.existsSync(keystore)) {
    throw new Error("Keystore not found: " + keystore);
  }

  fs.rmSync(outDir, { recursive: true, force: true });
  fs.mkdirSync(outDir, { recursive: true });

  const manifest = new TwaManifest({
    packageId: "com.tjapps.mobihymn.twa",
    host,
    name: "MobiHymn",
    launcherName: "MobiHymn",
    display: "standalone",
    themeColor: "#F5D200",
    themeColorDark: "#2D2D2D",
    navigationColor: "#000000",
    navigationColorDark: "#000000",
    navigationDividerColor: "#000000",
    navigationDividerColorDark: "#000000",
    backgroundColor: "#F5D200",
    enableNotifications: true,
    startUrl: "/",
    iconUrl,
    maskableIconUrl: iconUrl,
    monochromeIconUrl: undefined,
    splashScreenFadeOutDuration: 300,
    signingKey: {
      path: keystore,
      alias: "coindo_prod",
    },
    appVersionName: "0.9.0",
    appVersionCode: 5,
    shortcuts: [],
    generatorApp: "bubblewrap-cli",
    webManifestUrl,
    fallbackType: "customtabs",
    features: {},
    alphaDependencies: { enabled: false },
    enableSiteSettingsShortcut: true,
    isChromeOSOnly: false,
    isMetaQuest: false,
    fullScopeUrl: `https://${host}/`,
    minSdkVersion: 21,
    orientation: "default",
    fingerprints: [],
    additionalTrustedOrigins: [],
    retainedArtifacts: [],
  });

  const generator = new TwaGenerator();
  await generator.createTwaProject(outDir, manifest);
  manifest.saveToFile(path.join(outDir, "twa-manifest.json"));
  console.log("TWA project generated at", outDir);
  console.log("Host:", host);
  console.log("Package:", manifest.packageId);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
