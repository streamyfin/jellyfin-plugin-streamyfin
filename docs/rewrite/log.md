# Log

What actually happened, newest first. The plan says where we are going,
[issue #114](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/114)
says what is left, and this says what was done and why, so someone arriving in
three months can catch up without reading a pull request thread.

Append an entry whenever something lands or a decision is taken. A decision that
lives only in a comment thread is a decision nobody will find.

## 2026-08-25

### The load test that P0.3 was waiting on

Done, on both lines, and it passes. The plugin was loaded on a throwaway
`jellyfin/jellyfin:10.11.11` and on `jellyfin/jellyfin:12.0-rc5`, each with a
handwritten `streamyfin_plugin.db` carrying three device tokens in
`applicationPaths.DataPath`, which is where the old store wrote it.

Both servers log `Loaded plugin: Streamyfin 0.68.1.0`, then
`Imported 3 device token(s) from /config/data/streamyfin_plugin.db`. The new
database comes out with `DeviceTokens` at three rows, one `ImportMarkers` row
recording the count, and `__EFMigrationsHistory` holding `InitialCreate` stamped
`9.0.11` on 10.11 and `10.0.10` on 12. The old file's md5 is identical before and
after, and a second start does not import again. `GET /streamyfin/config` answers
401 rather than 404 on both, so the controller is routed, and the embedded admin
page serves 200. No error in either log.

So the question the pull request left open is answered:
**`Microsoft.EntityFrameworkCore.Sqlite` is provided by the server on both
lines**, and the plugin does not need to ship it. The versions line up rather
than merely coexist: every 10.11 patch from the declared floor 10.11.9 through
10.11.11 pins EF Core 9.0.11, which is exactly what `jf11` compiles against, and
12 pins 10.0.11 against the plugin's 10.0.10, so the server is the newer of the
two and satisfies the reference.

Worth writing down for whoever repeats this: the official `jellyfin/jellyfin`
image sets `JELLYFIN_DATA_DIR=/config`, so plugins live in `/config/plugins/` and
the plugin's own database in `/config/data/`. The linuxserver image puts plugins
under `/config/data/plugins/`. Getting that wrong looks exactly like a plugin
that fails to load, with nothing in the log to say why.

### P1.2 to P1.4, and the finding at the top of the dossier is closed

Three parts on `develop`. A hundred tests, still 0 warnings and 0 errors on both
targets.

**#129, P1.2 and P1.3.** Three targeting levels, each a `Settings` with only the
keys it means to speak about filled in, which works because every property on it
is nullable. Groups, memberships and per user overrides in three tables, the
overrides stored as JSON rather than as forty one columns so adding a setting is
not a migration.

The rule is that **the most specific level wins, including the lock**. That was
worth getting wrong once: the obvious reading is that the most restrictive lock
should win, and issue #29, which `plan.md` quotes as evidence the design was not
imposed from outside, has an override setting `lock: false` to hand a setting back
to named users. A resolver that could only tighten would make the design it
implements impossible.

Two things checked rather than assumed, and both mean no `Compat` entry:
`TaskTriggerInfo` is identical on 10.11 and on 12, and the review found that
`SerializationHelper` had a `SerializeToJson` with no matching reader.
`Deserialize` goes through YamlDotNet, which reads most JSON but not the three
settings this plugin deliberately writes as numbers.

**#130, the drawer.** The plugin was reachable from the plugin list and a direct
URL and nowhere else. Three fields on `PluginPageInfo` it never set fix that. The
icon can only be a Material ligature, since the web client renders it through
MUI's icon **font** component, so the real logo needs File Transformation, which
is now wired as a soft dependency. Worth writing down: **that plugin has no
Jellyfin 12 release**. Its `v12` branch rewrites it around an `IStartupFilter`
middleware and bumps to 3.0.0, but the published manifest serves ABI 10 only, so
the logo cannot appear on a 12 server today. The code is correct and dormant.

**#131, P1.4.** `GET config` was guarded by `Authorize` alone, so every account on
the server received the entire configuration. `examples/full.yml` has warned in
capitals for months that the Seerr admin key is readable by anyone with an
account. It is not a warning any more.

An administrator still gets it untouched, and gets the **raw** set rather than
their own resolved view, because the admin pages save what they load: a resolved
set would write their own group's overrides into the global configuration on the
next save. Everyone else gets their settings resolved, with credentials removed
**last**, so no level can hand a key back out.

The notification block goes with the key. It is not per user, so it cannot be
resolved for a caller, and the app never reads it:
`refreshStreamyfinPluginSettings` takes `data.settings` and nothing else. Serving
a user the list of accounts that receive notifications is the same kind of leak,
just quieter.

**The cost, taken deliberately.** A non administrator no longer receives
`jellyseerrApiKey`, so the passwordless Seerr sign-in falls back to the password
login the app already has. ⚠️ **The Seerr key has to be rotated.**
`Jellyseerr.tsx:118` persists it into each device's own storage, so filtering it
server side does not remove it from the devices that already connected. Without a
rotation this part is cosmetic for existing installations.

### P1.1, and #109 finally answered

Two more on `develop`. Fifty one tests now, still 0 warnings and 0 errors on both
targets.

**#128, P1.1.** `SettingsSchema` reads `Settings.cs` once and hands back a
descriptor per key: the type unwrapped from `Lockable`, whether it locks, whether
it holds a credential, and the label the property already carries. P1.3 resolving
a value across three levels, P1.4 deciding what leaves the config endpoint, and
P3.1 rendering a form all need to walk the settings, and without this each of them
keeps its own property list. The first one to drift is the one that leaks a key
nobody remembered to add.

Secrecy is an attribute on the property rather than a third field on `Lockable`,
because it belongs to the key and not to the value an admin writes. So an admin
cannot mark the Seerr key public by editing their YAML, and the file format every
installation already writes is unchanged. The generated schema carries it as
`x-secret` **on the property**, not on the shared `LockableOfString` definition
that three plain URLs also point at. There is a test for that, because it is the
mistake the design exists to avoid.

**#109.** Open since 30 July, and the triage was right about all three of its
problems. The keys were renamed to the ones the app actually resolves, the app
grew the second key and the `disabled` bindings on `feat/subtitles-on-mute`, and
the defaults now agree at `true` and `false`. Three tests pin the names, the
defaults, and the fact that neither ships locked, in the same spirit as the
orientation tests pinning the Expo contract. Rebased from `main` onto `develop`
and merged with no conflicts.

### Seerr authentication, decided

Not to be proxied through the plugin. Recorded here because it is the kind of
decision that otherwise lives in one comment thread.

The mechanism today is worse than the finding suggests.
`hooks/useJellyseerr.ts:263` does not sign a user in: `loginWithApiKey` calls
`GET /user/jellyfin/{id}` to *resolve* the Seerr account, and every call after
that goes out with the admin key. The app's own comment says it, at line 159:
"API-key calls act as the key's owner, so requests must carry the Seerr id of the
signed-in user to be attributed to them." Attribution is a parameter the client
chooses. So any user who reads the key can request as anyone, and approve. The key
is also held on every device and sent over the network on every session.

A server side proxy cannot be small, which is what settles it. Seerr has no
endpoint that mints a scoped session for another user, which is exactly what
[seerr#2244](https://github.com/seerr-team/seerr/pull/2244) adds and that pull
request is still open. So the plugin could not hand out a per user token; it would
have to relay every Seerr call, injecting the key and the `actAsUserId` itself.
That makes the plugin a Seerr client, and it becomes dead code the day #2244
lands.

Jellyfin 12 does not force the issue either. What is `[Obsolete]` on `master` is
`AuthenticateUser`, the one taking a user id and a password, plus three `*Legacy`
update endpoints. `POST /Users/AuthenticateByName` and `GET /Users/Me` are
untouched, and those are the two Seerr needs, for its current login and for
validating a token under #2244.

And the break is smaller than the triage assumed.
`components/settings/Jellyseerr.tsx:91-113` falls back to the classic password
login when no key is present. Filtering the key for non administrators costs the
passwordless convenience, not the integration.

So P1.4 filters it like any other secret, and the passwordless path returns with
#2244, using the user's own token rather than an admin key. One thing not to
forget when that lands: `Jellyseerr.tsx:118` persists the key into each device's
own settings storage, so filtering it server side does not remove it from the
devices that already connected. **The Seerr key has to be rotated** or the fix is
cosmetic for existing installations.

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
