# Compat

Everything that differs between Jellyfin 10.11 and Jellyfin 12 lives here, and
nowhere else. No file outside this folder may branch on `JF11`, `JF12`,
`NET9_0` or `NET10_0`. `CompatBoundaryTests` fails the build if one does.

The rule exists so that dropping a Jellyfin version stays a deletion rather
than an archaeology exercise. Version specific code scattered through the
domain is exactly what makes old runtimes impossible to retire.

## Dropping Jellyfin 10.11, when the time comes

1. Delete the `jf11` `PropertyGroup` from `Directory.Build.props` and make
   `jf12` the default.
2. Delete the `jf11` entry from the build and release workflow matrices.
3. Delete every `#if JF11` branch in this folder, keeping the `JF12` side.
4. Drop the 10.11 manifest and stop publishing its artifact.

Nothing else in the codebase should need to change. If it does, something
leaked out of this folder and the guard test was bypassed.

## Adding a Jellyfin line, when the time comes

The reverse of the list above, and it is meant to stay this short. As of
2026-09-11 Jellyfin's `master` is versioned 13.0.0 and carries no API change
against 12.0: the ten commits between them are a version bump, translations and
issue templates. Nothing publishes a `13.*` package yet, so there is nothing to
compile against. `nuget-watch.yml` says so the day there is.

1. Add a `PropertyGroup` to `Directory.Build.props` for the new target, with its
   `TargetFramework`, `JellyfinVersion`, `JellyfinAbi`, `EfCoreVersion` and its
   `DefineConstants`. The floor is the oldest server the plugin actually uses,
   not the oldest of the line: 10.11.9 is the floor of `jf11` because
   `IUserManager.Users` became `GetUsers()` inside that patch line.
2. Add it to the matrices in `build.yml` and `release.yml`, with the SDK it
   needs.
3. Give it its own manifest file name in the `MANIFEST` line of the `Makefile`.
   The oldest line keeps `manifest.json`, so servers already pointed at that URL
   do not break, and every newer line gets a `manifest-jfNN.json` beside it.
4. Build both. Anything that fails to compile is a real difference, and it goes
   in this folder behind `#if`, not where it was found.

Two things that are worth checking before assuming a target is only a version
number, because both bit this plugin on the 10.11 to 12 move:

- **The EF Core version the host provides.** The plugin references it but the
  server loads it, so a plugin ahead of its host fails to load. Read
  `Directory.Packages.props` in the Jellyfin release rather than taking the
  newest.
- **A breaking change inside a patch line.** `IUserManager.Users` disappeared in
  10.11.9, which is why no single artifact covers 10.11.0 through 10.11.11.
