# Plan

Seven parts. Each one ships on its own and leaves the plugin working. Tracked as
checkboxes in
[issue #114](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/114),
which stays the source of truth for progress. This document holds the reasoning
that does not fit in a checkbox.

App side counterpart: [streamyfin/streamyfin](https://github.com/streamyfin/streamyfin).

## Decisions already taken

**Breaking change with a migration.** New config model, one time migration that
reads the old XML at startup. Old routes stay as compatibility shims until the
app fleet has moved. The pattern is copied from intro-skipper's
[#871](https://github.com/intro-skipper/intro-skipper/pull/871): read only import
of the old store, an `ImportHistory` marker, an atomic commit, a retry on the next
start if it fails, and the old file left untouched as the rollback path.

**Three targeting levels.** Server default, then plugin defined groups, then per
user. The server resolves the three and serves the effective set, with secrets
filtered out for non admins. Jellyfin has a `Group` entity in `JellyfinDbContext`
with permissions and preferences, but nothing uses it, no manager, no controller,
no route, so the groups are defined by the plugin rather than borrowed from a
dormant entity the server may reshape.

**Plugin and app move together.** Two repositories, one project.

**Dual Jellyfin support via an MSBuild switch**, the same approach as the
JavaScript Injector plugin: one branch, a `JellyfinTarget` property flipping
`TargetFramework` and package versions, CI building twice. The alternative,
intro-skipper's branch per Jellyfin major, means porting every fix by hand
forever. Dropping 10.11 later means deleting one `PropertyGroup`, one matrix
entry and the compat folder.

**Everything version specific lives in one folder**, behind `#if`, enforced by a
test that fails the build if a version conditional appears anywhere else. The
constraint is deletability, and a rule that is not executable decays in six
months.

**Generated admin forms.** `@json-editor/json-editor` 2.15.2 is already vendored
in `Pages/Libraries/` and imported by nothing. Simple settings render from the
JSON schema the plugin already publishes. The home editor and the group targeting
screen stay hand written. That hybrid is what KefinTweaks landed on in
`scripts/configuration.js`: descriptor driven for the repetitive fields, hand
written where the shape is genuinely irregular.

## How it lands

`develop` is the integration branch, `main` keeps serving the published plugin.
One branch per sub part named `refonte/pX-N-slug`, one pull request onto
`develop`, chained with `gh stack` so each pull request shows only its own layer.

Order: **P0**, then **P1**, then **P2** and **P3** in parallel, then **P4**,
**P5**, **P6** in whatever order suits.

Two ordering constraints inside P0 that are not obvious:

- **P0.11 before P0.2.** Do not switch CI on over a suite that fails on Windows
  and on any non English machine. Red that everybody ignores is worse than no CI.
- **P0.12 after P0.5.** The warning policy pass must not annotate nullability in
  `Storage/`, since P0.5 deletes that folder.

## P0. Foundation

Two artifacts from one source, and the storage layer moved to EF Core before
anything is built on top of it. Nothing changes for the user.

- **P0.1** `JellyfinTarget` switch (`jf11` = net9.0 + Jellyfin 10.11, `jf12` =
  net10.0 + Jellyfin 12), single compat folder, test that fails on a stray `#if`
  outside it
- **P0.11** Make the test suite pass outside an English Linux box
- **P0.2** `build.yml`: build and test both targets on every pull request and
  push, upload DLL artifacts
- **P0.3** EF Core `DbContext`, baseline migration, `IDesignTimeDbContextFactory`,
  database file under `IApplicationPaths`
- **P0.4** Move device tokens off `Storage/` onto EF, one time read only import of
  the old database, old file left untouched
- **P0.5** Drop the direct `Microsoft.Data.Sqlite` usage and the hand written SQL
- **P0.6** Reworked `release.yml`: matrix produces one zip per target, a single
  GitHub release carries both
- **P0.7** Two manifests with their own `targetAbi` and checksum, keeping the
  current URL for 10.11 so no configured server breaks
- **P0.8** Script cleanup: manifest deduplication, zip path derived from the TFM
  instead of the hardcoded `net9.0`, `Jellyfin.Controller` pinned to each line's
  floor rather than `10.11.*-*`
- **P0.9** Set `owner` in `manifest.json` to the organisation instead of a
  personal account
- **P0.10** Optional: SignPath artifact signing, free for open source
- **P0.12** Warning policy, once P0.5 has removed `Storage/`
- **P0.13** The three `CS4014` fire and forget calls in the notification handlers

Two findings from P0 that changed the shape of the rest:

The plugin **compiles against Jellyfin 12 with zero source changes**. The compat
folder starts empty. `net9.0` to `net10.0` is the only structural break, and
everything the plugin consumes, `IUserManager`, `ILibraryManager`,
`IServerConfigurationManager`, `IApplicationPaths`, `IPluginServiceRegistrator`
and the `BaseItemKind`, `ItemSortBy`, `ItemFilter`, `SubtitlePlaybackMode` enums,
exists unchanged in both.

`IUserManager.Users` became `IUserManager.GetUsers()` in **10.11.9**, a breaking
change inside a patch line, so the floor for the 10.11 artifact is 10.11.9 and
not 10.11.0. Still wider than the published manifest, which demands 10.11.11 by
accident.

Also settled here: both versions register
`AddPooledDbContextFactory<JellyfinDbContext>`, so a plugin can inject
`IDbContextFactory<JellyfinDbContext>`, but that is the server's own context and
its migrations belong to the server. The plugin owns its own SQLite database
either way. There is no cleaner story on 12 worth waiting for.

## P1. Settings model

The core. Everything after this depends on it.

- **P1.1** Typed settings schema carrying, per key: value, lock, and a secret
  marker
- **P1.2** Plugin defined groups: table, user assignment, API
- **P1.3** Resolution engine global to group to user, with precedence tests
- **P1.4** Output filtering: secrets go to admins only, everything else resolved
  for the caller
- **P1.5** One time migration of the old XML config
- **P1.6** New routes, old ones kept as shims

P1.4 is the fix for the finding at the top of
[state-of-the-plugin.md](state-of-the-plugin.md): today `GET config` hands the
whole configuration, Seerr admin key included, to every authenticated account.
It is also what closes #69, since only the server can decide which sections a
given user is allowed to know exist.

The shape of P1 was proposed by the maintainers themselves in
[#29](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/29), well
before this rewrite was scoped. See [issue-triage.md](issue-triage.md).

## P2. App on the new contract

- **P2.1** Consume the server resolved effective set instead of the raw map
- **P2.2** Keep the three existing semantics: locked, pushed once as a default,
  free
- **P2.3** Tolerate a server still on the old format during rollout
- **P2.4** Remove the hardcoded rule that forces `searchEngine` to Streamystats
  when `streamyStatsServerUrl` is set

P2.2 is the one that is easy to lose. The app has two distinct behaviours today,
not one. A `locked` setting is enforced on read, through `effectiveSettingsAtom`,
and on write, through `updateSettings`. An unlocked plugin value is applied
exactly once, through `pendingPluginDefaults` and the `PLUGIN_APPLIED_DEFAULTS`
registry, so the admin proposes a starting value and the user stays free to change
it afterwards. Collapsing those two into one flag would take away an admin's
ability to suggest without imposing. The schema in P1.1 must carry all three
states: locked, pushed once, unmanaged.

## P3. Generated admin UI

- **P3.1** Render simple settings from the JSON schema using the already vendored
  json-editor
- **P3.2** Hand written editor for home sections
- **P3.3** Hand written screen for group and user targeting
- **P3.4** JSON export and import
- **P3.5** Decide between embedded pages and `jellyfin-plugin-pages`

P3.5 is a real fork. Today the pages are HTML and JS embedded as resources in the
DLL, 16 MB of it. `jellyfin-plugin-pages` and `jellyfin-plugin-custom-tabs` are
both built on File Transformation, which lets a plugin change what jellyfin-web
serves without touching its files. The call has to go through reflection, since
every plugin lives in its own `AssemblyLoadContext`, which is a real cost to
weigh against dropping the embedded page machinery.

## P4. Push notifications

- **P4.1** Inject `IHttpClientFactory` with a named client and a timeout
- **P4.2** Read Expo receipts and prune `DeviceNotRegistered` tokens
- **P4.3** Batching, honour 429 and `Retry-After`, retry with backoff
- **P4.4** Declared events instead of the four hardcoded ones
- **P4.5** Per user notification preferences, on top of P1

P4.2 is the one with user visible consequences. Expo only reports a dead token
through `/push/getReceipts`, which the plugin never calls, so tokens accumulate
forever and sends go nowhere.

P4.4 absorbs #29, #34 and #30, and each needs an explicit decision rather than an
open ended promise. See [issue-triage.md](issue-triage.md).

## P5. Custom home

- **P5.1** Replace the four nullable siblings (`items`, `nextUp`, `latest`,
  `custom`) with a discriminated type
- **P5.2** Server side section validation with errors the UI can show
- **P5.3** Dedicated reorderable section editor with a preview
- **P5.4** Per group section targeting, once P1 is in
- **P5.5** Migrate existing configurations

P5.1 is what unblocks #78, #21 and every future section kind. Today adding a kind
means adding a fifth nullable sibling that nothing says is exclusive with the
other four. P5.3 needs the explicit `order` field from #93 to have somewhere to
write to.

## P6. Third party integrations

- **P6.1** Group integrations into typed blocks instead of flat keys
- **P6.2** Server side connection probe with a test button in the admin UI
- **P6.3** Expose health so the app knows an integration is down
- **P6.4** Replace the hidden Streamystats rule with a declared one

P6.2 belongs on the server, which can reach an internal URL a phone never will.
The app's `utils/serverUrl/probes/reachability.ts` is the pattern to follow.

P6.1 is also the moment to rename the `jellyseerr*` keys to `seerr*` with the old
names kept as aliases, which closes #95 without breaking every existing YAML.

## Progress

| Sub part | Pull request |
|---|---|
| P0.1 | [#115](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/115) |
| P0.2 | [#117](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/117) |
| P0.11 | [#116](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/116) |
