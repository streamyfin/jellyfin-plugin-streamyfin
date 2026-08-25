# App side work

The rewrite spans two repositories. This is the running list of what
[streamyfin/streamyfin](https://github.com/streamyfin/streamyfin) has to do, kept
here rather than there so the whole chantier can be read from one place.

Every entry says where it came from and where it is going, so nothing depends on
remembering a conversation. When an item ships, move it to Done with its pull
request rather than deleting it.

## Open

| Item | Source | Lands with |
|---|---|---|
| Consume the server resolved effective set | plan P2.1 | after P1.6 |
| Preserve locked, pushed once and free | plan P2.2 | with P2.1 |
| Tolerate a server still on the old format | plan P2.3 | with P2.1 |
| Remove the hidden Streamystats rule | plan P2.4 | any time |
| Offer Landscape Auto in the settings screen | issue #110 | any time, small |
| Grey out the mute subtitle switch when locked | PR #109 | any time, small |
| Decide on an allow restart setting | PR #109 | before that plugin key |
| Seerr integration on tvOS | issue #108 | with P6 |
| Seerr authentication by user token | issue #82, seerr#2244 | with P6 |

## Details

### Offer Landscape Auto in the settings screen

Issue [#110](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/110)
asked for it on the plugin side, which is done. The app applies whatever number
the plugin pushes, so an admin can set it, but a user cannot pick it for
themselves.

`components/settings/OtherSettings.tsx` offers four of Expo's ten values:

```ts
const orientations = [
  ScreenOrientation.OrientationLock.DEFAULT,
  ScreenOrientation.OrientationLock.PORTRAIT_UP,
  ScreenOrientation.OrientationLock.LANDSCAPE_LEFT,
  ScreenOrientation.OrientationLock.LANDSCAPE_RIGHT,
];
```

Add `ScreenOrientation.OrientationLock.LANDSCAPE` and its entry in the local
`orientationTranslations` map. The translation key
`home.settings.other.orientations.LANDSCAPE` already exists and is already mapped
in `ScreenOrientationEnum` in `utils/atoms/settings.ts`, so nothing else is
needed.

### Grey out the mute subtitle switch when locked

`components/settings/SubtitleToggles.tsx:442` renders the `subtitlesOnMute` switch
with no `disabled` binding. A locked value is still enforced, on read through
`effectiveSettingsAtom` and on write through `updateSettings`, so the setting
cannot be changed. The switch just gives no sign of it, and the user is left
wondering why it snaps back.

`components/settings/OtherSettings.tsx` shows the pattern to copy: it reads
`pluginSettings?.defaultVideoOrientation?.locked` and disables accordingly.

The same audit is worth running across every settings screen. This one was found
by accident while triaging a plugin pull request, which suggests there are others.

### Decide on an allow restart setting

Plugin pull request [#109](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/109)
declares `autoSubtitlesOnMuteAllowRestart`, described as allowing subtitle formats
that need the server to re-process the stream. Nothing in the app implements or
reads it.

Either the app grows the setting and the plugin declares it afterwards, or the
plugin drops the key. Declaring a lockable key for behaviour that does not exist
is how the plugin ends up with settings nobody can explain.

Note also that the app's `subtitlesOnMute` is iOS only and not TV
(`SubtitleToggles.tsx:439`) and lives in the native player
(`providers/NativePlayerProvider.tsx:1039`). A plugin setting that silently does
nothing on Android or on TV needs to say so in its description.

### Consume the server resolved effective set

Plan P2.1. Today the app calls `GET /Streamyfin/config`
(`augmentations/api.ts:55`), caches the raw map in MMKV under
`STREAMYFIN_PLUGIN_SETTINGS`, and resolves locally. After P1 the server sends the
set already resolved for the caller, with secrets removed.

This is also what fixes issue
[#69](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/69): a
section built on a library the user cannot see must not reach the device at all,
and only the server can decide that.

### Preserve locked, pushed once and free

Plan P2.2, and the easiest thing to lose in the migration. The app has two
distinct behaviours today, not one:

- `locked` is enforced on read, through `effectiveSettingsAtom`, and on write,
  through `updateSettings`.
- An unlocked plugin value is applied **exactly once**, through
  `pendingPluginDefaults` and the `PLUGIN_APPLIED_DEFAULTS` registry. The admin
  proposes a starting value, the user stays free to change it afterwards.

Collapsing those into a single flag takes away an admin's ability to suggest
without imposing. The schema in P1.1 has to carry all three states: locked, pushed
once, unmanaged.

### Tolerate a server still on the old format

Plan P2.3. Phones update on their own schedule and servers update on theirs, so
for a while a new app will meet an old server and the reverse. Neither should
degrade into no settings at all.

### Remove the hidden Streamystats rule

Plan P2.4. Setting `streamyStatsServerUrl` currently forces `searchEngine` to
Streamystats, a rule written into the settings loading path rather than declared
anywhere. It surprises admins who set a URL and find their search engine changed.
Replace it with a rule the plugin states, per P6.4.

### Seerr on tvOS, and Seerr authentication by user token

Issue [#108](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/108)
reports the Seerr integration working on iOS and not on tvOS. Filed on the plugin
repository, but it is app work.

Issue [#82](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/82)
matters more than its title suggests. The request was for the plugin to hold a
Seerr admin API key and act on everyone's behalf. herrrta rejected that and
changed Seerr instead, so a client can authenticate with the user's own Jellyfin
access token ([seerr#2244](https://github.com/seerr-team/seerr/pull/2244)).

That is the same admin key that every authenticated user can currently read from
`GET /streamyfin/config`, see finding 1 in
[state-of-the-plugin.md](state-of-the-plugin.md). Authenticating by user token
does not close that on its own. The endpoint still hands the whole configuration
to any account on the server, so the key stays readable until the server filters
what it serves, which is P1.4. Treat P1.4 as the prerequisite for this item
rather than a follow up: with it, the key setting can become optional and then
disappear.

## Done

Nothing yet.
