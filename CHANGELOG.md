# Changelog

## 4.0.5.9 — 2026-10-26 (experimental Debug)

- [`15a82ab`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/15a82ab5848e8b4e6b14fda1baabdff8449be13b) — [install ZIP](https://github.com/SirSheikhsPears/AdaptiveNetworks/blob/test-builds/ANR_A-4.0.5.9-INSTALL.zip) / [`.sha256`](https://github.com/SirSheikhsPears/AdaptiveNetworks/blob/test-builds/ANR_A-4.0.5.9-INSTALL.sha256): 12 files in a versioned folder; Debug, `clean` source: ANR [`3fc78f1`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/3fc78f1c9f59f053b74941cf3fae15d5ef9fdae6), KianCommons [`bc9071b`](https://github.com/SirSheikhsPears/KianCommons/commit/bc9071bcb5c097fb7c3ddad653da70dd882820e2).
- [`ce9e66c`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/ce9e66c6f5ecdfe3c3443950a2269ea2bee6749c) — `master` merged into `test-builds`; previous test ZIP preserved.
- Source preparation: [`3fc78f1`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/3fc78f1c9f59f053b74941cf3fae15d5ef9fdae6), [`15a6a31`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/15a6a317b5d738cdeacdf185b75e4635ac9a7048), [`a563736`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/a563736b4b163de73245930734c2ecc5e8f429e9), [`07d9864`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/07d98640166d0913316dd1202696d5c49eada925), [`7d8c0b4`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/7d8c0b47640b1ee06c4898530a268415d129862f).

## 4.0.5.0-TEST — 2026-10-08 (experimental)

- [`32a539f`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/32a539f49ad5cc72843b22243317bcb00e241824) — historical [test ZIP](https://github.com/SirSheikhsPears/AdaptiveNetworks/blob/test-builds/AdaptiveNetworks-4.0.5.0-TEST.zip); unchanged. Initial testing requested for Direct Connect/Bend, TM:PE and Network Skins Continued.

## Race Day port — source changes since TheMadisonian

### 1. DynamicFlags and connection groups

`DynamicFlags<NetInfo>` (`DynamicFlagsUtil`, `DynamicFlags2`, `NetUtil`); `TagBase.Check()` / `CheckOrNone()` intersection checks.

Commits: [`29c13e9`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/29c13e95393bbea507eceda69d2b7644a3f80e22), [`54f8cbb`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/54f8cbbbf367dca58b924ae4fc0616d86fce61d1), [`6d9fb5e`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/6d9fb5e90c3adef686dce107da22213759aae21b); KianCommons [`bc9071b`](https://github.com/SirSheikhsPears/KianCommons/commit/bc9071bcb5c097fb7c3ddad653da70dd882820e2).

### 2. Flags2 and orientation

`Extensions.CheckFlags()` / `CheckSegmentFlagsCommons.CheckFlags()` — bidirectional `Flags2`; `Segment.CheckFlags()` — legacy extended node-end ordering retained.

Commits: [`7562b26`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/7562b26ca895f71e688a3a3fe6e44d3f2f57770c), [`87ed6be`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/87ed6bec55e1d9d449fbe58067fb895adb6c0cc5), [`445da9e`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/445da9e2d03e60ad35633964214262e68c9d8220).

### 3. TM:PE parking

`NetLaneExt.UpdateLane()` — `LaneInfo.m_finalDirection`, original `ParkingMan` guard.

Commits: [`1c7b4e1`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/1c7b4e10aba980f8640b8297e25de88eda59e52f), [`5578d72`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/5578d72e95815ec991559fc0b450cc21c719a478) (comment-only), [`ca42089`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/ca420897385e85acba56597177ebb79df7a246fc).

### 4. Harmony corner patches

`CalculateCorner_SharpPatch.TargetMethod()` / `CalculateCorner_ShiftPatch.TargetMethod()` — Race Day `NetSegment.CalculateCorner()` overloads.

Commits: [`3ecba6a`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/3ecba6a547911049fafae791a14f7505f5af4ed7), [`60eebfe`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/60eebfe3642deff8c4bf222e382d39dbfe3109b6).

### 5. Track object colors

`LaneTransition.GenerateRenderData()`, `NetLaneExt.GenerateRenderData()`, `TrackRenderData.PopulateGroupData()` — `NetAI.GetObjectColorIndex()`.

Commit: [`74b8297`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/74b829704aa37bd1aa65cd69c8fc302c2bec9240).

### 6. Fence rendering

`RenderInstance.Prefix()` — `CheckSegmentFlagsCommons.s_currentSegmentID` for `RefreshRoadFence` paths.

Commit: [`18a50d2`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/18a50d2e58802dfaadaab8d01ee8c7ed31304d12).

### 7. Network Skins identity

`ANImplementation.ID` restored to `"Adaptive Roads"`.

Commit: [`84f1fca`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/84f1fcaf9fd3cf22591ee0080b6617f25f4fa4bd).

### 8. Unified UI

`ARTool.Awake()` / `VBSTool.Awake()` — `UUIHelpers.GetFullPath()`; preview image restored.

Commit: [`0952e85`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/0952e856d40150d5e5eb6c4b447f2018b315e4e8).

### 9. Repository and release preparation

Commits: [`0313cbb`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/0313cbb1afdbba172a3caeffc389c0a94125bf54), [`8fce2b5`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/8fce2b5801e095a32f6e490dfe683e1d0ddda34b), [`d24e728`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/d24e7281faaee70631d12e23d70155e9be508db3), [`d37a28f`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/d37a28fba8a5856d165f6987b25f4829d851d394), [`05bd2f0`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/05bd2f0c161ce6ad4c51fd42ee1fb8e6804e9764), [`7d8c0b4`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/7d8c0b47640b1ee06c4898530a268415d129862f); `test-builds`: [`32a539f`](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/32a539f49ad5cc72843b22243317bcb00e241824).
