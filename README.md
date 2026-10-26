# Adaptive Networks Remain

**Cities: Skylines 1 — Race Day (1.21.1)**

**Adaptive Networks Remain (ANR)** continues Adaptive Networks development, aiming to restore pre-Race Day functionality while preserving existing networks and saved games.

Based on [TheMadisonian's Adaptive Networks](https://github.com/TheMadisonian/AdaptiveNetworks), building on the work of [Kian](https://github.com/kianzarrin/AdaptiveNetworks), [T.D.W.](https://github.com/JadHajjar/AdaptiveNetworks), and [Chamëleon TBN](https://steamcommunity.com/sharedfiles/filedetails/?id=2669938594).

## Current status: (Experimental)

### Implemented / initially tested

- Initial Race Day API migration, including `DynamicFlags<NetInfo>` and `Flags2` handling.
- Basic segment/node flags — initially tested.
- `TagBase.Check()` / `CheckOrNone()` — fixed custom ConnectGroup matching *(ie. Porthal North Bridge* Bend/Custom *nodes).*
- TM:PE and Network Skins Continued — initial integration and save/load tests.

### In testing/development

- Advanced flags, Direct Connect, Bend and segment orientation.
- Harmony patches and network rendering.
- TM:PE parking flags.
- Network Skins Continued integration.
- Asset Editor compatibility.

### Planned

- Road Builder integration.
- Further compatibility and regression testing.

## Builds

### GitHub branches

- **[`master`](https://github.com/SirSheikhsPears/AdaptiveNetworks/tree/master)** — **ANR Alpha** source code intended for the Steam Workshop **ANR Alpha** release.
- **[`test-builds`](https://github.com/SirSheikhsPears/AdaptiveNetworks/tree/test-builds)** — experimental **ANR Alpha** builds for GitHub testers, with corresponding source history.

*After the first ANR Stable Workshop release, `master` will track Stable sources, a separate `Alpha` source branch will be introduced, and `test-builds` will remain the Alpha test-build distribution branch.*

### Available builds

- **4.0.5.9 (Debug)** — [experimental install ZIP](https://github.com/SirSheikhsPears/AdaptiveNetworks/blob/test-builds/ANR_A-4.0.5.9-INSTALL.zip) ([SHA-256](https://github.com/SirSheikhsPears/AdaptiveNetworks/blob/test-builds/ANR_A-4.0.5.9-INSTALL.sha256)), published October 26, 2026. Contains a versioned mod folder for `Addons\Mods`.
- **4.0.5.0-TEST** — [experimental test ZIP](https://github.com/SirSheikhsPears/AdaptiveNetworks/blob/test-builds/AdaptiveNetworks-4.0.5.0-TEST.zip), published October 8, 2026.
- **ANR Alpha** — not published.
- **ANR Stable** — will be available after ANR Alpha passed all compatibility tests with current Race Day **(1.21.1)** patch.

## Compatibility

- **TM:PE:** required by the current implementation.
- **Network Skins Continued:** integration available (full regression testing ongoing)
- Do not enable competing Adaptive Networks variants simultaneously.

## Changes

See [CHANGELOG.md](CHANGELOG.md) for source changes and release history.

**Full Race Day compatibility verification is still in progress.**
