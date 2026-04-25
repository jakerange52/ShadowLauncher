# Third-Party Installer Binaries

Place the following files in this directory before building the Full Install:

| File | Source |
|---|---|
| `Decal_3_0_0_0.exe` | https://www.decaldev.com / community mirror |
| `ThwargFilter_Setup.exe` | https://github.com/Thwargle/ThwargLauncher / www.thwargle.com |

These files are **not committed to the repository** (see `.gitignore`) because their
redistribution rights must be verified before bundling. See `THIRD_PARTY.md` at the
repo root for attribution and license details.

The installer build will fail with a missing-file error if these are absent when
building the Full Install configuration.
