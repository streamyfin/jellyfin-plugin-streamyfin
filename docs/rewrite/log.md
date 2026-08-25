# Log

What actually happened, newest first. The plan says where we are going,
[issue #114](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/114)
says what is left, and this says what was done and why, so someone arriving in
three months can catch up without reading a pull request thread.

Append an entry whenever something lands or a decision is taken. A decision that
lives only in a comment thread is a decision nobody will find.

## 2026-08-25

### P0 landed on `develop`

The six open pull requests merged in the order #121 gave: #122, #123, #124, #125,
#127, #126. `develop` is now eleven commits ahead of `main` and P0 is complete
apart from P0.10.

**The squash bit back, exactly where it was expected to.** The repository allows
squash merging only, so merging #125 put its work on `develop` as a new commit
with a different hash from the one #127 was carrying. #127 went from clean to
conflicting the moment its base moved to `develop`. Both conflicts were additions
git could not place rather than disagreements about content, the `SQLitePCLRaw`
pin in the csproj and a `using` in `StreamyfinController.cs`, and keeping the
branch side resolved them. Worth stating plainly for the next stacked pair: a
squash merge breaks the parent link, so the child always has to be brought back
onto the branch by hand.

**What was verified before merging**, on both targets, from a clean tree:
`--configuration Release` builds with 0 errors and 0 warnings on the plugin, the
7 remaining warnings are the `xUnit1031` calls in the test project that P0.12
deliberately left out of the policy, 37 tests pass on `jf11` and on `jf12`, and
the packaging chain runs, `make zip` plus `make update-manifest DRY_RUN=1` for
both manifests.

**Two pull requests merged without a CodeRabbit review.** The free tier gives two
reviews an hour and the queue was saturated all day, so #126 and #127 carry a
`Validate PR title` and a build, and nothing else. Their diffs were read by hand
instead. #121 will get the cumulative review when it comes out of draft, which is
the right place for it anyway, but the gap is worth knowing rather than assuming
every merged part was bot reviewed.

**The issues those fixes close are still open.** GitHub only closes an issue when
the `Fixes` keyword reaches the default branch, and these merged onto `develop`.
#74, #110 and #88 close when #121 lands on `main`, or by hand before then.

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

Pull request #119 added [state-of-the-plugin.md](state-of-the-plugin.md),
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

### P0 finished, in four larger pull requests

CodeRabbit gives two included reviews an hour, and one pull request per sub part
was saturating it permanently, so the rest of P0 landed in four pieces instead of
ten.

**#125, P0.3 to P0.5.** `Storage/` deleted, EF Core in its place. The three could
not land apart: the hand written store cannot go until its data has moved, and
its data cannot move until there is somewhere to move it to. Device tokens are
imported once from `streamyfin_plugin.db`, read only, in one transaction with a
marker row, and the old file is never written to or deleted, so a downgrade still
finds it. CodeRabbit caught a real regression on the first pass: the replacement
of a device token used two `SaveChanges` calls, which is two transactions, and a
failure between them left the device with no token. Fixed by updating in place.

**#126, P0.6 to P0.9.** The release chain assumed a single artifact. The zip path
was hardcoded on `net9.0` twice, `targetAbi` was hardcoded to 10.11.11, and
nothing removed an existing entry before adding one. Now: one tag, one release,
both zips, and a manifest per Jellyfin line with the `targetAbi` its own build
was compiled against. The `Makefile` gave up tagging and pushing to `main`, which
never belonged to it.

The part that matters more than the fixes: **packaging now runs on every pull
request**, in dry run. The release chain being exercised only by a release is how
a zip path pinned to `net9.0` survived unnoticed in the first place.

**#127, P0.12 and P0.13.** The warning policy. Five rules turned off in
`.editorconfig` with the reason written next to each, everything else fixed, and
`TreatWarningsAsErrors` turned on. Both targets now build at 0 warnings and 0
errors. Turning it on immediately promoted a NuGet advisory on a transitive
`SQLitePCLRaw` to an error, which is the policy working on its first day.

P0.13 was the three `CS4014`. Jellyfin's event handlers are synchronous, so a
notification send cannot be awaited from one, and the exception landed in a task
nobody observed. A failing send looked exactly like a working one. They now go
through `SendDetached`, which logs the failure.

**P0.10, SignPath artifact signing, is not done.** It was optional, it needs an
account and an application to the free open source programme, and it is the
maintainer's call rather than a code change.

### Not verified yet

The plugin has not been loaded on a real server since the EF Core change. The
evidence says `Microsoft.EntityFrameworkCore.Sqlite` is present on both Jellyfin
lines, since intro-skipper ships nothing but its own dll against the same
dependency, but that deserves a load test before a release goes out. The beta
server is stopped and the workstation is off the network, so it is deferred
rather than skipped. See `project_plugin_test_servers` for the access details.

### Pull request triage

The three pull requests that were already open, diagnosed in
[pull-request-triage.md](pull-request-triage.md). #71 to close, #81 needs a
decision after nine months of nobody answering, #109 declares keys the app does
not read.

The app side work that came out of it is tracked in
[app-side-work.md](app-side-work.md).
