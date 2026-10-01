# Adaptive Networks: Race Day fixes for review

This branch contains a candidate extension of TheMadisonian's Emergency Fix for
Cities: Skylines 1. The goal is to restore Adaptive Networks behavior after the
Race Day changes while retaining support for existing networks and saves.
Changes were prepared with AI assistance and require independent code review
and further in-game testing.

## Current status

- The five functional AN source files are committed on `fix-raceday-flags`.
- On 1 October 2026, their committed contents were checked against the prepared
  source package: all five matched byte for byte, with no additional changed paths.
- Full AN behavior and compatibility are still under evaluation.
- **KianCommons integration is pending.** Its complete functional changes are
  included here as patches for review. The AN submodule still points to its old
  commit, so this branch is not yet a complete source integration of test2.

This is a working fork for inspection and discussion. It does not represent an
accepted upstream change or a production release.

## Review the AN changes

[Exact five-file code change](https://github.com/SirSheikhsPears/AdaptiveNetworks/commit/22c84acec04c6b1c52fb8f848432c9e44249202a)
from source base `fafd40aadd674302e2c2fceda9cddeca6a399cfc`.

| File | Change |
| --- | --- |
| [Flags.cs](AdaptiveRoads/Manager/NetInfoExtension/Flags.cs) | Require overlapping connection groups; allow both-empty sets only for `CheckOrNone`. |
| [Extensions.cs](AdaptiveRoads/Manager/NetInfoExtension/Extensions.cs) | Check vanilla segment `Flags` and `Flags2` for a specific model orientation. |
| [CheckSegmentFlagsCommons.cs](AdaptiveRoads/Patches/Segment/CheckSegmentFlagsCommons.cs) | Evaluate both orientations with vanilla and AN requirements together; swap extended node-end flags for the existing LHT reversal case. |
| [Segment.cs](AdaptiveRoads/Manager/NetInfoExtension/Segment.cs) | Swap extended tail/head node flags in the backward branch as well as ordinary end flags. |
| [NetLaneExt.cs](AdaptiveRoads/Data/NetworkExtensions/NetLaneExt.cs) | Query TM:PE parking restrictions with `LaneInfo.m_finalDirection`. |

EF already used the game's selected orientation and supported new flags in its
updated checks. The orientation change addresses a case where vanilla selects
forward, AN rejects forward, and backward would satisfy both sets of conditions.
The missing backward extended-node flag swap predates EF.

The outer vanilla check in the transpiler is retained. These changes do not
establish that every ordinary Bend `NetNode.RenderSegments` path is covered.
Segment `Flags2` and node flag handling are separate concerns.

## Review the KianCommons changes

See [dependency integration details](review/race-day/INTEGRATION.md).

- [Full functional patch from AN's pinned KianCommons commit](review/race-day/patches/02-KianCommons-from-69a5f8.patch).
- [Alternative: local corrections on top of zhuchhh's migrated commit](review/race-day/patches/03-KianCommons-local-on-b922567.patch).
- [Portable copy of the five AN changes](review/race-day/patches/01-AN-core.patch).

The two KianCommons patches are alternatives. Do not apply both.
The tested build used zhuchhh's `b922567` plus local corrections, rather than
`b922567` alone. Merely changing the submodule pointer to that commit is incomplete.
Patches in this review directory do not automatically modify the dependency.

## Evidence and limits

These are results from the previously built **AN-EF test2** candidate and the
provided sessions, not a claim that this fork revision was independently built
and run in Unity.

| Check | Observed result | Limit |
| --- | --- | --- |
| Compilation of test2 | 0 errors, 1263 warnings; Roslyn 4.8.0, .NET Framework 3.5 references, Release/TRACE | Uses the supplied game/mod assemblies and modified KianCommons. |
| Connection-group predicates | 1568 checks and six asset-derived cases passed | A limited interpreter of compiled IL, not a CLR or Unity test harness. |
| Same-save comparison in CS1 1.21.1-f9 | Both sessions loaded AN successfully; both reported 46 successful AN Harmony patch classes with matching target sets | The mod sets matched except for replacing EF with test2; loading order was not identical. |
| AN-owned logs | No warning/error/exception entries in either session | Global game logs contain pre-existing issues, including IMT `UpdateEnters` errors. |
| Visual test | Unwanted elevated embankment wall removed on the reported Custom and Bend cases of Workshop asset 2819280719 | Combined candidate tested; individual changes were not isolated. This does not prove all node configurations. |
| Current fork upload | Five expected AN files match the prepared sources exactly | KianCommons gitlink remains at its original commit. |

[Predicate report](review/race-day/validation/tag-tests.json),
[earlier patch-application checks](review/race-day/validation/patch-verification.json),
and [fork upload verification](review/race-day/validation/github-upload-verification.json).

Target integrations are IMT, Node Controller Renewal, TM:PE, Network Skins,
Hide Unconnected Tracks Renewed and Hide Crosswalks Renewed. Their presence and
loading in the compared sessions are not a blanket compatibility certification.
The global log comparison found no new normalized suspect-message type;
that is not proof that all regressions are absent.

## Review and test priorities

1. Review connection-group predicates and the forward/backward selection rule.
2. Integrate and commit the KianCommons changes, then update AN's submodule
   reference and, if necessary, `.gitmodules` to a remotely available commit.
3. Test one-sided TM:PE parking restrictions, opposite segment orientations,
   updates after toggling restrictions, and left-hand traffic.
4. Exercise ordinary Bend, Direct Connect, Custom and Junction cases, distinct
   flags at both ends, close rendering and LOD transitions.
5. Check the listed integrations on the same controlled scenarios.

## Provenance

| Item | Version |
| --- | --- |
| AN source base | TheMadisonian/AdaptiveNetworks `fafd40aadd674302e2c2fceda9cddeca6a399cfc` |
| Reviewed AN upload | `22c84acec04c6b1c52fb8f848432c9e44249202a` |
| Original KianCommons gitlink | `69a5f8abede9fcb23440b98418fccfaf006877ca` |
| Test2 KianCommons starting point | zhuchhh/KianCommons `b922567013664ac6378ad8dabc0082c50b86f666` plus local changes |
| Test2 DLL SHA-256 | `d31ab4c50939301f1cbdee027ba5b9d21b62c7940a1d6e72f45ccbe3a7571689` |

The source builds on Kian Zarrin's Adaptive Networks and KianCommons,
TheMadisonian's Emergency Fix, and zhuchhh's KianCommons migration. Existing
EF initialization safeguards are preserved and are not presented as new work.

This review material contains no game DLLs, mod binaries, CRP assets, raw player
logs or save files. It describes a test build; it is not a download of that build.
