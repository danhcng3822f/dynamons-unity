# Dynamons 2 — Unity 2D native

Open this Unity project folder with Unity Hub using Unity 6 LTS (`6000.0.62f1` baseline). Open [`Assets/Scenes/Main.unity`](Assets/Scenes/Main.unity) and press Play. No browser or external game server is required.

## Linux x64 build (GitHub Actions)

In the private Unity repository, configure the license credentials as [GitHub Actions secrets following GameCI's activation guide](https://game.ci/docs/github/activation/), then run **Build Dynamons 2 (Linux x64)** from the Actions tab. Download the `Dynamons2-linux-x64` artifact, extract the `.tar.gz`, and run `./Dynamons2.x86_64` from the extracted directory. The workflow does not publish a release.

## Checks

- `node unity/tools/verify-import.mjs` from the workspace root checks the gameplay and asset snapshot. To refresh: `node unity/tools/import-web.mjs`.
- In Unity, run **Dynamons → Verify Rules**. This exercises the C# rules and a temporary save, never your real save.
- Play Mode: choose a starter, read the intro, drag the map, walk to a node, fight, try a cooldown, use an item and switch, then check the result and reopen the scene to verify the save. Check Team/Storage, Dynastones, Inventory, coin Shop, unlocked Arena, all Training tiers, and Starter Event victory/reward. Check both a narrow and a wide Game view; touch and mouse should hit the same controls. If input does not respond, check that **Active Input Handling** includes the built-in Input Manager. Toggle sound/music and try all three battle speeds.
- In a disposable save, obstruct the `save.json.tmp` path and confirm a coin-shop purchase shows **Retry Save**. Remove the obstruction and confirm retry saves the original purchase without spending twice. Do not run this check on a personal save.

Save data is in Unity's `Application.persistentDataPath`, not in the browser. Recovery uses the previous valid snapshot, so a damaged primary file can roll back the latest play. Like the Web version, closing during combat abandons the current battle but does not refund tickets or consumables already saved. The port does not import browser progress or award the Web 100-ticket test gift.

Presentation closely mirrors the Web version (`320x568` portrait UI artwork, all 7 bitmap fonts with kerning, GPU vector FX, 34 FX spritesheets, drag-and-drop Team sorting, Shop burst reveal, and Result showcase). No auto-gifted bonus starters are granted; players choose LV 2 starters and catch Dynamons normally. Unity 6 LTS C# compilation, `Dynamons -> Verify Rules`, mouse/touch drag-and-drop, VectorCanvas visuals, and audio playback still await Play Mode verification in the Unity Editor.

See [port progress](PORT_PROGRESS.md) for the exact verification status.
