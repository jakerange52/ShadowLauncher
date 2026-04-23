# Third-Party Acknowledgements

ShadowLauncher optionally integrates the following third-party software in its
**Full Install** configuration. We are grateful to their authors for making
these tools available to the Asheron's Call emulation community.

---

## ThwargFilter / ThwargLauncher

- **Author:** Thwargle (www.thwargle.com)
- **Repository:** https://github.com/Thwargle/ThwargLauncher
- **License:** No explicit license file is present in the repository at time of
  writing. Redistribution is intended to be used with permission — we are in
  contact with the author. If you are distributing ShadowLauncher, verify
  current license status before bundling.
- **Purpose:** ThwargFilter is a Decal plugin that writes per-session heartbeat
  files consumed by ShadowLauncher for in-game status monitoring, character
  tracking, and login command execution. ShadowLauncher degrades gracefully
  (process-uptime-based monitoring only) when ThwargFilter is not installed.
- **Notes:** ThwargLauncher is the spiritual predecessor to ShadowLauncher.
  ShadowLauncher was built in the spirit of Thwargle's own stated goal of
  a cleaner, DAT-management-integrated launcher experience. We aim to honour
  and extend that work.

---

## Decal

- **Author:** Decal Development Team / AC community
- **Website:** https://www.decaldev.com
- **License:** Historically distributed freely within the Asheron's Call
  emulation community. No formal open-source license is publicly documented.
  Redistribution should be verified with the current maintainers before
  bundling in a public installer.
- **Purpose:** Decal is the plugin framework required to run ThwargFilter
  (and other AC client plugins). The Full Install option runs the Decal
  installer silently so users do not need to set it up manually.

---

## Notes on Redistribution

ShadowLauncher does **not** distribute the Asheron's Call game client. Users
must obtain the client independently. The symlink-based DAT management in
ShadowLauncher operates entirely within the user's own data directories and
does not modify or redistribute any game files.

---

*Last updated: 2026*
