# State of the plugin

Read from the code at `da60702`, the last commit on `main` before the rewrite
branches. Every claim points at a file so it can be checked instead of believed.

## Shape

| | |
|---|---|
| C# | 3862 lines across 35 files |
| Settings schema | `Configuration/Settings/Settings.cs`, 409 lines |
| Storage | `Storage/Database.cs` 282 lines and `Storage/Extensions.cs` 387 lines, hand written `Microsoft.Data.Sqlite` |
| Admin pages | 4 pages, 717 lines of hand written JS including `Pages/shared.js` at 230 |
| Vendored JS | 16 MB under `Pages/Libraries/`, all embedded in the DLL |
| Tests | 4 files |
| Target | `net9.0`, `Jellyfin.Controller 10.11.*-*` |
| CI | `lint_pr.yml` and `release.yml`, nothing that builds or tests |
| Published zip | 3.1 MB for 0.68.1.0 |

## The public contract

`StreamyfinController` is routed at `streamyfin` and carries no class level
`[Authorize]`. Endpoint by endpoint:

| Route | Guard |
|---|---|
| `POST config/yaml` | `RequiresElevation` |
| `GET config` | `[Authorize]` |
| `GET config/schema` | none, anonymous |
| `GET config/yaml` | `[Authorize]` |
| `GET config/default` | `[Authorize]` |
| `POST device`, `DELETE device/{id}`, `POST notification` | `[Authorize]` |

The app consumes `GET /streamyfin/config` and caches the result in MMKV under
`STREAMYFIN_PLUGIN_SETTINGS`.

## What is wrong

### 1. Any authenticated user can read the whole configuration

`GET config` (`Api/StreamyfinController.cs:107`) and `GET config/yaml`
(`Api/StreamyfinController.cs:124`) are guarded by `[Authorize]` alone, so every
account on the server receives the entire config, secrets included. This is not
a suspicion, `examples/full.yml:131` already warns in capitals that the Seerr
admin API key is readable by anyone with an account. Only the write path
requires elevation. A settings model that cannot filter its own output is the
root cause, not the endpoint.

### 2. One global blob, no targeting

There is a single `Config` (`Configuration/Config.cs`) for the whole server.
Nothing expresses "this value for these users". Issue #29 already argued for per
user overrides two years ago, and #90 and #69 are the same gap seen from two
other angles.

### 3. Storage is hand written SQLite

`Storage/` is 669 lines of `Microsoft.Data.Sqlite` with SQL as strings, its own
schema handling and its own `Dispose` pattern. intro-skipper is the cautionary
tale here: it moved to EF Core late and still carries several hundred lines of
`EnsureLegacySchemaCompatibility()` doing raw `ALTER TABLE` and manual
`__EFMigrationsHistory` inserts. Starting on EF Core means never writing that.

### 4. The admin UI is a YAML text editor

Adding a setting means editing `Settings.cs`, then `examples/full.yml`, and an
admin only discovers the setting by reading the example. `examples/full.yml` is
304 lines. The editor is Monaco, an 11 MB bundle embedded in the DLL, wrapped in
228 lines of `Pages/YamlEditor/index.js`.

`Pages/Libraries/json-editor.min.js` is `@json-editor/json-editor` at 523 KB,
embedded in the DLL and imported by nothing. Grep the whole tree and it never
appears outside its own file. Today that is dead weight. For P3 it is the answer
already paid for: a form generator driven by the JSON schema the plugin already
publishes at `GET config/schema`.

### 5. Push notifications never learn that a token is dead

`PushNotifications/NotificationHelper.cs:132` does `using HttpClient client =
new()` on every send. Jellyfin injects `IHttpClientFactory`, so this is socket
exhaustion and frozen DNS for nothing.

Worse on the substance: Expo answers with tickets, `ExpoNotificationResponse`
deserialises them, and `/push/getReceipts` is never called. That endpoint is the
only place Expo reports `DeviceNotRegistered`. No dead token is ever pruned, the
table only grows, and sends go nowhere. There is no batching, no handling of 429
or `Retry-After`, and no retry.

Three sends are fire and forget, flagged as `CS4014` and ignored:
`ItemAddedService.cs:76`, `PlaybackStartEvent.cs:73`, `SessionStartEvent.cs:51`.
The handler returns before the send completes and any failure lands in an
unobserved task.

### 6. Home sections have four mutually exclusive fields and nothing says so

`Section` (`Configuration/Settings/Settings.cs:40`) carries `items`, `nextUp`,
`latest` and `custom`, all nullable siblings. Exactly one should be set. Neither
the type nor any validation enforces it. That is what makes the YAML awkward to
write and a generated form impossible: a generator cannot guess it should show
only one of the four. A discriminated type fixes the schema, the form and the
server side validation in one move.

### 7. Nothing builds the plugin on a pull request

`.github/workflows/` holds `lint_pr.yml`, which checks PR titles, and
`release.yml`. Code that does not compile can be merged. The four test files are
only ever run by hand.

### 8. The shipped dependencies are not the ones we compile against

Third party assemblies are not produced by the build. They are committed as
binaries in `packages/` and zipped by the `Makefile`. The only two commits that
ever touched that folder are both named `wip`.

| Assembly | Committed | Referenced in csproj |
|---|---|---|
| `Newtonsoft.Json.Schema.dll` | 4.0.1 | 3.0.16 |
| `NJsonSchema.dll` | 11.0.2 | 11.0.2 |
| `NJsonSchema.Annotations.dll` | 11.0.2 | 11.0.2 |
| `Namotion.Reflection.dll` | 3.1.1 | 3.1.1 |
| `YamlDotNet.dll` | 16.0.0 | 16.0.0 |
| `Newtonsoft.Json` | not shipped | 13.0.3 |

Two problems. We compile against `Newtonsoft.Json.Schema` 3.0.16 and ship 4.0.1,
a major version apart. And `Newtonsoft.Json` is referenced but not shipped, so we
silently rely on Jellyfin carrying it.

There is a third, smaller one: no source file references the
`Newtonsoft.Json.Schema` namespace at all. The package is a paid product, it is
referenced, it is shipped, and it is unused.

The zip path in the `Makefile` is hardcoded on `net9.0`, twice. It breaks on the
first multi target release. `targetAbi` is hardcoded to `10.11.11` in
`scripts/validate-and-update-manifest.js:9`, and that script does
`versions.unshift(newVersion)` with no lookup, so republishing a version creates
a duplicate entry.

### 9. The project opted into every analyser rule, then ignored all of them

The csproj sets `AnalysisMode=AllEnabledByDefault` with
`TreatWarningsAsErrors=false`. Result: 88 warnings on the 10.11 target, 107 on 12
where .NET 10 adds rules. Roughly 30 are noise on serialization DTOs (`CA1002`,
`CA1819`, `CA2227`), and `PluginConfiguration.cs` already carries a file level
`#pragma warning disable CA2227`, which says someone hit it and worked around it
locally. Roughly 30 more are nullability debt: `<Nullable>` is enabled and the
code was never annotated.

That debt is not cosmetic. `Notifications.cs:26` declares
`public string[] EnabledLibraries { get; set; }` with no initializer. Omit the
key from the YAML and it is null. `ItemAddedService.cs:51` then reads
`enabledLibraries.Length`. That is issue #74, open for eleven months, 26
comments, and it is one ignored `CS8618` away from never having existed.

### 10. The test suite only passes on an English Linux box

`DatabaseTests` deletes the SQLite file without closing the connection, which
throws `IOException` on Windows. `LocalizationTests.TestStringFormatLocalization`
asserts the English string without pinning the culture, so it fails on any French
machine because `Strings.fr.resx` wins. Three failures on this workstation, none
of them caused by our changes. Turning CI on before fixing this would only teach
everyone to ignore red.

## What the in flight pull requests already change

| PR | Sub part | Effect |
|---|---|---|
| [#116](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/116) | P0.11 | Suite passes off an English Linux box, 13/13 |
| [#115](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/115) | P0.1 | `JellyfinTarget` switch, `Compat/` folder and the test that keeps the boundary |
| [#117](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/117) | P0.2 | `build.yml` building and testing `jf11` and `jf12` on every pull request |

The most useful result so far: the plugin compiles against Jellyfin 12 with zero
source changes, 0 errors on both targets. `Compat/` starts empty and stays empty
until something actually diverges, and #117 is what tells us the day a 12 release
candidate breaks that.

The one real surprise from building: `IUserManager.Users` became
`IUserManager.GetUsers()` in **10.11.9**, a breaking change inside a patch line.
Pinning `Jellyfin.Controller` at `10.11.0` does not compile. The floor for the
10.11 artifact is 10.11.9, which is still wider than what the published manifest
demands today, since `targetAbi` sits at 10.11.11 by accident rather than by
choice.
