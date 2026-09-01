# Log

What actually happened, newest first. The plan says where we are going,
[issue #114](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/114)
says what is left, and this says what was done and why, so someone arriving in
three months can catch up without reading a pull request thread.

Append an entry whenever something lands or a decision is taken. A decision that
lives only in a comment thread is a decision nobody will find.

## 2026-09-01

### P3.3, the targeting screen

The engine P1.2 to P1.4 built had seven routes, its own tables, its tests, and no
screen: creating a group meant writing HTTP by hand with a user id nothing in the
interface would show you. That is the same failure mode P3.1 closed for the 72
unreachable settings, and it is why P3.3 was taken before P3.2 and P3.4.

A new **Targeting** tab: the groups with their priority, members and override count,
an editor for one group or one user, and a delete that says what it takes with it.

**The plan called this a hand written screen and half of it is not.** Its list and its
member picker are hand written; the settings a level overrides are the generated form
from P3.1, with `required_by_default` false and the property picker left on. That one
flip is the whole difference between "what does this server default to" and "what does
this group change", and it also means the Targeting page needs none of the save-diff
logic the Application page needs: the editor only ever holds the keys the level carries,
so its value is the answer. `Pages/settings-form.js` is the part they share.

**One route was missing and nobody had noticed**: `users/{userId}/settings` had a PUT
and a DELETE and no GET, because the resolution only ever reads the *caller's* override,
never a named user's. Added as `GET v1/users/{userId}/settings`, versioned only, since
unlike its siblings it is not a path any app in the field ever called.

**`Plugin.cs` gave up a static field assigned from its constructor.** `_prefix` came from
`GetType().Namespace` at construction, so the page list only answered correctly on a
running server, and the test for it could only check a hand written copy of the list.
Taken from the type instead, `PluginPagesTests` now enumerates the plugin's own pages and
holds every `EmbeddedResourcePath` to account. 167 tests green on jf11 and jf12, Release
builds clean on both.

**Not yet seen in a browser.** The screen is JS, the beta pass is owed, and the scenario
to run is written down in [admin-ui-targeting.md](admin-ui-targeting.md) along with a
casing gap on `LanguagePreference` that the same pass should confirm or dismiss.

### P4.2, the dead tokens nobody was collecting

Expo says a token is dead in two places and the plugin read neither, so a device that
uninstalled the app kept its row forever and every notification aimed at it was accepted,
queued and thrown away.

- **At send time**, as a ticket whose `details.error` is `DeviceNotRegistered`. That
  field was typed `object` and read by nothing.
- **Later, in a receipt**, because a delivery can still fail after the ticket said ok.
  `/push/getReceipts` was never called at all, which is the line the issue names.

Both now prune. The receipts half needs to outlive the request, since Expo takes minutes
to produce one, so an accepted push is stored as a ticket and token pair in a new
`ExpoReceipts` table and a scheduled task collects them hourly: it asks about pushes
older than fifteen minutes, a thousand at a time, prunes what comes back dead, forgets
what was answered, and drops rows older than twenty-four hours because past that Expo has
no answer left to give.

**The part that had to be got right.** An error ticket carries no id and no token. The
only thing tying it to a device is its position, since Expo answers with one ticket per
recipient in the order they were sent. Acting on that means a miscount deletes someone
else's token and their notifications stop with nothing to show why, which is worse than
the bug being fixed. So the mapping is only used when the two counts agree exactly,
otherwise it logs and prunes nothing; and only `DeviceNotRegistered` prunes, never
`MessageRateExceeded` or the others, which are about the message and not the device.

That decision lives in `ExpoTickets`, deliberately apart from the helper and free of the
database, because it is the only code in the plugin that deletes something a user
registered. Thirteen tests on it alone, ten more on the store, 195 green on both targets.

**A note for P4.3.** `SendToAll` still puts every token in one `to` field while Expo caps
a message at a hundred recipients. The count guard means that cannot cause a wrong
prune — a refused request is not an answer about anybody's token — but the send itself
still fails silently past a hundred, and that is P4.3's to fix.

### P4.1, and a detour that says something about ordering

[#141](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/141): the push
notification client. `new HttpClient()` per send is gone, replaced by a named client from
`IHttpClientFactory` with a 30 second timeout instead of the default 100, the HTTP status
is checked before the body is parsed (a 429 was reading exactly like a success), and
`_userManager` is guarded before it is dereferenced. Five tests with a stubbed handler.
Deployed to the beta on Jellyfin 12 and it loads with no unresolved service, which was
the real risk of the injection and the only part unit tests could not answer.

The detour worth recording: P4 was picked over the rest of P3 **because Tailscale was
down and P4 was the only large piece provable without a browser**. That is a tooling
constraint deciding a priority, and it was the wrong reason. P3 is the admin interface,
which is what was asked for. Noted here so the next gap in connectivity does not quietly
reorder the plan again.

## 2026-08-31

### P3.1 landed, and the two fixes it uncovered

[#136](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/136) generates the
Application form from the schema, and
[#139](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/139) groups it into
the sections the app uses. The reasoning, what a real dashboard found that the unit tests
could not, and why a per setting "platforms" field was investigated and dropped, are all
in [admin-ui-generated.md](admin-ui-generated.md).

Two things the generated form surfaced by offering settings the hand written page never
had:

- [#137](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/137), the default
  audio and subtitle languages could not be saved at all. `LanguagePreference` is the one
  settings type with PascalCase members, because the app matches them against the SDK's
  `CultureDto`, and the YAML reader rejected the names its own schema described.
- [#138](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/138), each video
  player setting now says in its own description which platform it decides, rather than
  leaving an administrator to guess.

### The plugin is licensed

[#140](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/140) merged into
`main`: MPL-2.0, the same licence the app uses, so the two halves of one project do not
disagree about their terms. It also credits SignPath, which is a condition of their free
open source programme and the prerequisite for P0.10.

## 2026-08-27

### #1900 merged, and both written exceptions are gone

[streamyfin#1900](https://github.com/streamyfin/streamyfin/pull/1900) merged into
the app's `develop`, so the two exceptions P1.7 wrote down rather than fixed both
expired on the same day. `AppSettingsManifest.json` was regenerated from the app
source: `subtitlesOnMute` defaults to `true` there now, and
`subtitlesOnMuteAllowRestart` exists, which takes the manifest from 94 keys to 95.
`KnownDisagreements` and `DeclaredAheadOfTheApp` are both empty.

Nothing in `Settings.cs` or `DefaultSettings()` moved. The defaults #109 declared
were that branch's defaults all along, which is the whole reason the exceptions
were safe to write. The plugin declares 92 of the 95, and the three it does not
are the three that carry a written reason: `downloadQuality`,
`playbackSpeedPerMedia` and `playbackSpeedPerShow`.

**Checked that the comparison bites rather than passing by absence.** Emptying
`KnownDisagreements` means `subtitlesOnMute` is compared for the first time, so
its manifest default was flipped back to `false` and the test failed with
`subtitlesOnMute: app False, plugin true` before the manifest was restored. 148
tests green on jf11 and jf12.

Regenerating the manifest is the review step
[settings-parity.md](settings-parity.md) asks for on any app pull request that
touches `utils/atoms/settings.ts`. This was the first time it was owed, and the
only key that moved was the one the exception named.

## 2026-08-26

### P1.7, settings parity, and seven defaults that were lying

The plugin declared 43 of the 94 settings the app reads. More than half of what
the app offers was outside an administrator's reach: every subtitle appearance
control, the player gestures, the mpv tuning, the TV options, the choice of
video player. It now declares 92. The decision about each key, and the rules a
declaration follows, are in [settings-parity.md](settings-parity.md).

Nothing else was needed to make them work. P1.1 built `SettingsSchema` to read
`Settings.cs` by reflection and P1.3 resolves whatever that schema reports, and
neither holds a list of its own, so declaring the property was the whole change.
That is the part of P1 paying off rather than a new mechanism.

**The count was wrong twice before the manifest existed.** One grep matched two
properties that were commented out, so the plugin looked like it declared 45.
One awk missed a key whose declaration sat outside the range it scanned, so the
app looked like it had 93. Both numbers went into the dossier before
`AppSettingsManifest.json` was generated from the app source, and both were
wrong in opposite directions. The file is now the count.

**Seven shipped defaults contradicted the app, and three of those were help
text.** `hiddenLibraries` held `["Enter library id(s)"]`, `jellyseerrServerUrl`
held `"Enter jellyseerr server url"` and `marlinServerUrl` held `"Enter Marlin
server URL"`. `hasMeaningfulSettingValue` accepts any non-empty string, so
`pendingPluginDefaults` seeded each one once into every user's settings: a fresh
install handed the app a sentence where it expected a server address. The other
four turned off remembering the audio and subtitle track, rewound 15 seconds
instead of 10, and shrank subtitles to 80 per cent. All seven now match the app.

None of that was found by looking. The manifest was written, the test compared
it against `DefaultSettings()`, and it printed them.

**Two exceptions are written down rather than fixed.** `subtitlesOnMute` stays
`true`, which is the app branch of streamyfin/streamyfin#1900 rather than the
app's published `false`, because #109 was deliberately aligned with that branch.
`subtitlesOnMuteAllowRestart` is declared ahead of the same branch. Both name the
pull request that removes them, and a fourth test refuses an excuse that outlives
the setting it names.

**Three keys reach the app in a different shape than they are stored in**,
because `normalizePluginValue` reshapes them: `subtitleSize` is divided by 100,
and `maxAutoPlayEpisodeCount` and `defaultBitrate` are rebuilt from a scalar into
`{ key, value }`. The manifest records the wire form for those, since it is a
contract and not a disagreement.

**Three keys stay out.** `playbackSpeedPerMedia` and `playbackSpeedPerShow` are
not settings, they are maps the player writes by itself keyed by item and series
id. `downloadQuality` is typed `{ label, value }` in the app while the generic
fallback in `normalizePluginValue` only rebuilds `{ key, value }`, so its app
side has to move first.

**Two settings that had sat commented out since before the rewrite are back.**
`defaultAudioLanguage` and `defaultSubtitleLanguage` carried a TODO saying
Jellyfin's `CultureDto` has no parameterless constructor, so the schema generator
fails on it. The app reads exactly two of its fields, so the plugin declares its
own small type carrying those two.

**Verified on the beta**, Jellyfin 12 at `10.0.20.132`: the schema serves **92**
settings, up from the 43 measured on 2026-08-25, and `openSubtitlesApiKey`
carries `x-secret`. The resolved endpoint still answers 18 keys, which is the
right answer: a declared setting is not a pushed one, and the stored
configuration predates them all. The build it is running is backed up at
`/seedbox/jellyfin/streamyfin-backup-2026-08-26-pre-parity.dll`, outside the
plugin folder, and `autoUpdate` is still `false`.

**Noticed, then fixed in the same pull request:** `make update-manifest
DRY_RUN=1` wrote the manifest before it decided to skip anything. `DRY_RUN` was
only skipping the remote checksum verification, so running it locally left a
version entry for a release that does not exist. The write now sits behind the
same early return, everything else the dry run exercises still runs, and the
entry it would have written is printed instead.

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

### P1 is complete

**#132, P1.5.** The part the plan wrote as "one time migration of the old XML
config", assuming the earlier parts had replaced the config model. They had not.
So the question was whether there was anything to migrate at all, and the answer
turned out to be yes, but not the thing anyone had written down.

The server level now lives in the plugin's database with the other two, so all
three targeting levels are in one store and can be read inside one transaction.
The XML is read once and then left alone, byte for byte, as the way back, which is
the same rollback path the device token import took in P0.4.

**Jellyfin has been dropping settings silently.** The XML deserializer discards an
element it has no property for, before anything in the plugin sees it. An
administrator who set a key that was later removed or renamed has been running
with a value that does nothing and no way to find out. A real server's file,
checked while writing this, still carries three: `downloadMethod`,
`remuxConcurrentLimit` and `autoDownload`. The import now reads the file directly
and names them. It reports rather than guesses: a removed setting has no new home,
and inventing one would be worse than saying the value is unused. #109 renamed two
keys, so this was about to happen again.

**#133, P1.6.** The surface grew a route at a time with no version at all, so
renaming any of them would have broken every app in the field at once. Every route
now answers under `v1/` as well as at the path it has always had. The shims are
extra attributes on the same action, never a second method that delegates: two
methods drift, one gets a fix and the other does not, and the shim quietly stops
behaving like the route it stands in for.

`ApiSurfaceTests` is what makes that a mechanism rather than a promise. Removing an
entry from the list in it is now how a route stops being supported, which should
take a deliberate edit and a note about which app versions are being cut off.

**Noted and not changed:** `GET config/schema` has no authorization attribute and
answers 200 to anyone. It carries no server data, being generated from the C#
types and identical on every install, and the same content sits in
`examples/full.yml` in a public repository. Closing it breaks the admin page,
which fetches it with a bare `fetch` and hands the URL to Monaco to fetch again,
and that JavaScript needs a browser signed in to the dashboard to verify. Worth a
separate change rather than a blind one.

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

So P1.4 filters it like any other secret, and the passwordless path returns
with #2244, using the user's own token rather than an admin key. One thing not to
forget when that lands: `Jellyseerr.tsx:118` persists the key into each device's
own settings storage, so filtering it server side does not remove it from the
devices that already connected. **The Seerr key has to be rotated** or the fix is
cosmetic for existing installations.

### P0 landed on `develop`

The six open pull requests merged in the order #121 gave: #122, #123, #124, and
after those #125, #127, #126. `develop` is now eleven commits ahead of `main`
and P0 is complete apart from P0.10.

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
the `Fixes` keyword reaches the default branch, and these merged onto
`develop`. #74, #110 and #88 close when #121 lands on `main`, or by hand before
then.

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
