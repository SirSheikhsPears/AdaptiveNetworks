# Adaptive Networks 4.0.5.0 — Cities: Skylines 1

### Project Goal

The goal of this project is to properly port **Adaptive Networks** to the current Cities: Skylines 1 game version (1.21.1, Race Day update), restoring its original functionality while preserving compatibility with existing networks and saved games.

**The objective is to preserve the original behavior, not simply make the mod compile.**

This project is forked from [TheMadisonian's Adaptive Networks](https://github.com/TheMadisonian/AdaptiveNetworks), while also building upon the original work of [Kian (kianzarrin)](https://github.com/kianzarrin/AdaptiveNetworks) and subsequent development by [T.D.W. (JadHajjar)](https://github.com/JadHajjar/AdaptiveNetworks).

### Current Progress

**Completed / Initially Tested**

- Updated the codebase for the Race Day API changes, including the new flag system.
- Basic segment and node flags are now working.
- Corrected shared ConnectGroup recognition to prevent unintended meshes from appearing on Custom and Bend nodes.
- Initial integration with TM:PE and Network Skins Continued, save/load compatibility tests passed.

**Still Requires Testing**

- Advanced flags, including Direct Connect and Bend node behavior.
- Parking flags and segment direction handling.
- Full Network Skins integration, including flag selection and copying.
- Compatibility and regression testing with other network-related mods.

### Planned Work

- **Asset Editor compatibility** — not yet implemented or verified.
- **Road Builder integration** — still to be developed and tested.
- Further testing and restoration of advanced Adaptive Networks functionality.

### Status

**Work in progress — experimental development build.**

This is an ongoing effort to restore the full functionality of Adaptive Networks for Race Day patch