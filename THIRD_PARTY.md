# Third-Party Acknowledgements

ShadowLauncher optionally integrates the following third-party software. We are grateful to their authors for making
these tools available to the Asheron's Call emulation community.

---

## Decal

- **Author:** Decal Development Team / AC community
- **Website:** https://www.decaldev.com
- **License:** Historically distributed freely within the Asheron's Call
  emulation community. No formal open-source license is publicly documented.
  Redistribution should be verified with the current maintainers before
  bundling in a public installer.
- **Purpose:** Decal is the plugin framework required to run ShadowFilter
  (and other AC client plugins). ShadowFilter.dll is always installed under the
  ShadowLauncher folder; setup registers it with Decal so heartbeat
  monitoring, character tracking, and login commands work.

---

## ShadowFilter

ShadowFilter is a **first-party Decal plugin** built from source in this repository.
It is installed to `ShadowLauncher\ShadowFilter\` with every setup. The installer
registers it as a Decal network filter; if Decal was installed later, ShadowLauncher
retries on first run or you can add that folder in Decal Agent manually. It communicates with ShadowLauncher via files under
`%LocalAppData%\ShadowLauncher\`.

---

## Notes on Redistribution

ShadowLauncher does **not** distribute the Asheron's Call game client. Users
must obtain the client independently. The hard-link DAT management in
ShadowLauncher operates entirely within the user's own data directories and
does not modify or redistribute any game files.

---

*Last updated: 2026*
