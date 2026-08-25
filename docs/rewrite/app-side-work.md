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
| Audit every settings screen for a missing `disabled` binding | PR #109 | any time, small |
| Seerr integration on tvOS | issue #108 | with P6 |
| Seerr authentication by user token | issue #82, seerr#2244 | **now urgent**, P1.4 landed |
| Rotate the Seerr admin key | P1.4 | once P1.4 ships |

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

### Audit every settings screen for a missing `disabled` binding

The mute subtitle switch had none, so a locked value was enforced centrally on
read and on write while the control gave no sign of it: the user toggled it and
watched it snap back with no explanation. Fixed on `feat/subtitles-on-mute`, along
with the allow restart switch and both TV equivalents.

It was found by accident while triaging a plugin pull request, which is the part
worth acting on. `components/settings/OtherSettings.tsx` shows the pattern, reading
`pluginSettings?.defaultVideoOrientation?.locked`. Every lockable setting needs the
same, and nothing checks that today.

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

### Rotate the Seerr admin key

P1.4 stopped serving `jellyseerrApiKey` to anyone who is not an administrator.
That does not take it back from the devices that already have it:
`components/settings/Jellyseerr.tsx:118` persists it into each device's own
settings storage on a successful connection.

So the key on those devices keeps working against Seerr until it is rotated, and
until then the part is cosmetic for existing installations. Rotate it in Seerr,
then set the new one in the plugin.

While it is being rotated, non administrators lose the passwordless sign-in and
fall back to the password login at `Jellyseerr.tsx:91-113`. That path already
exists and is the default when no key is present, so nothing needs building for
the fallback itself. It comes back properly with seerr#2244, using the user's own
Jellyfin token rather than an admin key, and that pull request is still open.

## Done

### Subtitles when muted, and the allow restart switch

`feat/subtitles-on-mute` adds `subtitlesOnMuteAllowRestart`, wires the `disabled`
bindings on both switches on phone and on TV, and sets the defaults to `true` and
`false`. The plugin side landed in #109 declaring the same two keys with the same
defaults. **Not pushed yet**, so the app's published `develop` still has neither
the second key nor the bindings.
