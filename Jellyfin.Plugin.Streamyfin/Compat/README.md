# Compat

Everything that differs between Jellyfin 10.11 and Jellyfin 12 lives here, and
nowhere else. No file outside this folder may branch on `JF10`, `JF12`,
`NET9_0` or `NET10_0`. `CompatBoundaryTests` fails the build if one does.

The rule exists so that dropping a Jellyfin version stays a deletion rather
than an archaeology exercise. Version specific code scattered through the
domain is exactly what makes old runtimes impossible to retire.

## Dropping Jellyfin 10.11, when the time comes

1. Delete the `jf10` `PropertyGroup` from `Directory.Build.props` and make
   `jf12` the default.
2. Delete the `jf10` entry from the build and release workflow matrices.
3. Delete every `#if JF10` branch in this folder, keeping the `JF12` side.
4. Drop the 10.11 manifest and stop publishing its artifact.

Nothing else in the codebase should need to change. If it does, something
leaked out of this folder and the guard test was bypassed.
