# Log

What actually happened, newest first. The plan says where we are going,
[issue #114](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/114)
says what is left, and this says what was done and why, so someone arriving in
three months can catch up without reading a pull request thread.

Append an entry whenever something lands or a decision is taken. A decision that
lives only in a comment thread is a decision nobody will find.

## 2026-08-24

### The chantier opened

Scoped the rewrite, wrote issue #114, and settled the decisions that everything
else hangs off: breaking change with a migration, three targeting levels, plugin
and app moving together, dual Jellyfin support through an MSBuild switch, admin
forms generated from the JSON schema. Reasoning in [plan.md](plan.md).

Studied four reference projects first rather than inventing: KefinTweaks for the
hybrid form approach, intro-skipper for the EF Core model and for the migration
pattern of its pull request #871, File Transformation and the JavaScript Injector
for the page injection question and for the multi target build.

### `develop` became the integration branch

`main` keeps serving the published plugin. Every sub part gets its own branch and
its own pull request onto `develop`, chained with `gh stack`. #121 is the draft
pull request from `develop` onto `main` that shows the cumulative diff.

Consequence nobody predicted: CodeRabbit only auto reviews pull requests based on
the default branch, so the whole stack silently stopped being reviewed and the
check went green as `Review skipped`. Fixed by #120, which adds `.coderabbit.yaml`
listing `develop` and `refonte/.*`. Worth remembering as a shape of failure: a
skipped check looks exactly like a passing one.

### P0.1, P0.2 and P0.11 landed

- **#116, P0.11.** The suite only passed on an English Linux box. `DatabaseTests`
  deleted the SQLite file without draining the connection pool, which throws on
  Windows, and a localization test asserted the English string without pinning the
  culture, so it failed on any French machine. Fixed before turning CI on, not
  after, because red that everyone ignores is worse than no CI.
- **#115, P0.1.** `JellyfinTarget` switch, a single `Compat/` folder, and a test
  that fails the build if a version conditional appears anywhere else.
- **#117, P0.2.** `build.yml`, building and testing both targets on every pull
  request.

Two findings from doing it:

**The plugin compiles against Jellyfin 12 with zero source changes**, 0 errors on
both targets. `Compat/` starts empty. The only structural break between 10.11 and
12 is `net9.0` to `net10.0`.

**`IUserManager.Users` became `IUserManager.GetUsers()` in 10.11.9**, a breaking
change inside a patch line. No single artifact can cover 10.11.0 through 10.11.11,
so the floor is 10.11.9. That is still wider than the published manifest, which
demands 10.11.11 by accident rather than by choice.

### The dossier

#119 added [state-of-the-plugin.md](state-of-the-plugin.md),
[issue-triage.md](issue-triage.md) and [plan.md](plan.md). Reading the code to
write the first one turned up three things that were not in anyone's head:

- `Pages/Libraries/` is 16 MB of vendored JavaScript embedded in the DLL, and
  523 KB of it is `json-editor`, imported by nothing. That is the form generator
  P3 needs, already paid for.
- The third party assemblies in `packages/` are committed binaries, not build
  output. We compile against `Newtonsoft.Json.Schema` 3.0.16 and ship 4.0.1, and
  no source file references that namespace at all. A paid package, referenced,
  shipped, unused.
- `GET config` and `GET config/yaml` are guarded by `[Authorize]` alone, so every
  account on the server reads the whole configuration including the Seerr admin
  key. `examples/full.yml` already warns about it in capitals.

### Issue triage, and two fixes that needed none of the rewrite

Diagnosed all sixteen open issues against the code, in
[issue-triage.md](issue-triage.md). Results worth naming:

- **#74**, open eleven months with 26 comments, is a missing null.
  `EnabledLibraries` was declared non nullable with no initializer, so a default
  configuration left it null and the handler read `.Length` on it. Fixed in #122.
  The compiler had been emitting `CS8618` on that exact property the whole time,
  into a build where warnings are ignored. That is P0.12 argued in one property.
- **#110** is a value dropped when `OrientationLock` was hand copied from Expo.
  Fixed in #123, with a test pinning every member to its Expo counterpart, since
  the values are served as numbers and go straight to `lockAsync`.
- **#100 and #90 can be closed.** One was fixed and shipped in 0.67.0.0 and both
  reporters were waiting on a release, the other asks for a setting that exists.
- **#88 is not a bug.** `includeItemTypes` already accepts collections, Jellyfin
  just calls them `BoxSet` and nothing documents the legal values. That single
  issue is the argument for P3.1.

### Pull request triage

The three pull requests that were already open, diagnosed in
[pull-request-triage.md](pull-request-triage.md). #71 to close, #81 needs a
decision after nine months of nobody answering, #109 declares keys the app does
not read.

The app side work that came out of it is tracked in
[app-side-work.md](app-side-work.md).
