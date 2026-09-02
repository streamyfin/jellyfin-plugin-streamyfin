# Issue triage

Every issue open on the plugin repository, diagnosed against the code rather
than against its title, and mapped onto the plan in [plan.md](plan.md).

Issue #114 is the tracking issue for the rewrite itself and is not triaged here.

## Summary

| Issue | Title | Verdict | Lands in |
|---|---|---|---|
| [#100](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/100) | Notification endpoint broken since 10.11.9 | Fixed and released | Close |
| [#90](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/90) | Hide Watchlist not settable from the plugin | Setting exists now | Close after confirming |
| [#74](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/74) | NullReferenceException in ItemAddedHandler | Live, one missing guard | Fix now, P0 |
| [#110](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/110) | Missing "Landscape Auto" orientation | Live, enum copied incompletely | Fix now, P0 |
| [#95](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/95) | `seerrServerUrl` does not work | Accurate, keys still say jellyseerr | P6.1 |
| [#82](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/82) | Auto link Seerr accounts to Jellyfin users | Design already settled upstream | P6 |
| [#108](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/108) | Seerr integration missing on tvOS | App side | streamyfin repo, P6.3 helps |
| [#69](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/69) | Hidden libraries leak section headers | Live, needs server side resolution | P1.4 with P5.4 |
| [#29](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/29) | Native Jellyfin notification events | Design validates P1 | P4.4 and P4.5 |
| [#34](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/34) | Customizable notification messages | Contested, scope it down | P4.4 |
| [#30](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/30) | Images in notifications | Blocked by Expo, Android only | P4.4 |
| [#93](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/93) | Explicit ordering for home sections | Straightforward | P5.3 |
| [#78](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/78) | "My Media" home section | Missing section kind | P5.1 |
| [#21](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/21) | "Recommended" / "For you" section | Jellyfin already has the endpoint | P5.1 |
| [#88](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/88) | Collections in `includeItemTypes` | Not a bug, a discoverability failure | P3.1 |
| [#17](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/17) | Support for `CultureDto` | Needs the typed schema first | P1.1 then P3.1 |

## Close now

### #100, notification endpoint returning 500 since 10.11.9

Fixed by [#101](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/101),
merged 2026-06-16 and shipped in 0.67.0.0, so it is present in 0.68.1.0. Both
reporters were waiting on a release rather than on a fix. Close with the version
that carries it.

### #90, hide watchlist not settable from the plugin

`hideWatchlistsTab` exists today at `Configuration/Settings/Settings.cs:331` and
is documented at `examples/full.yml:163`. The issue predates it. Ask the reporter
to confirm on a current version, then close.

## Fix now, they do not need the rewrite

### #74, NullReferenceException in ItemAddedHandler

Open for eleven months with 26 comments, and it is four lines of code.
`Configuration/Notifications/Notifications.cs:26` declares
`public string[] EnabledLibraries { get; set; }` with no initializer. Leave the
key out of the YAML and it is null. `ItemAddedService.cs:51` reads
`enabledLibraries.Length` behind a `virtualFolder != null` guard, which is why it
only fires for some people and only on some items.

The thread converged on a workaround, tick your libraries, and the last comments
read as resolved. It is not: the default configuration still throws. A null
coalescing guard closes it, and the nullability annotation pass in P0.12 is what
stops the next one.

This is also the clearest argument for that pass. The compiler has been emitting
`CS8618` on that exact property the whole time, into a build where
`TreatWarningsAsErrors` is false.

### #110, missing "Landscape Auto" orientation

`OrientationLock` in `Configuration/Settings/Enums.cs` is a hand copied subset of
Expo's `ScreenOrientation.OrientationLock`, and the copy dropped values. It has
`Default = 0`, `PortraitUp = 3`, `LandscapeLeft = 6` and `LandscapeRight = 7`.
Expo's `Landscape = 5`, the one the app exposes as Landscape Auto, was never
copied. Numeric values are preserved, so adding the missing member is safe.

The enum is copied at all because of the 10.10 to 10.11 move noted in
`Enums.cs:77`. Worth checking the other copied enums for the same kind of gap
while we are in there.

**Read again on 2026-09-02.** The member is there: `Landscape = 5` has been in the
enum since #8, so the gap was the hand written page's dropdown, not the enum. The
generated form offers every member, and P3.6 labels this one "Landscape" where the
app says "Landscape Auto"; a `Display` name on the member closes that, and a beta
check that the option appears and round trips closes the issue. A second user added
on 2026-09-01 that they also want the video to open expanded rather than cropped, by
default. That is a setting the app does not have; it starts on the app side, and the
plugin declares it once the app reads it, the way P1.7 works. Kept open for later,
and answered on the issue when the label lands.

## Blocked on the settings model

### #69, hidden libraries still expose their section headers

A "Recently Added in X" section built on a library `ParentId` renders its header
for users who cannot see that library, revealing the name of a restricted
library. The reporter filed it as a privacy concern and they are right.

It cannot be fixed on the app side without leaking the same information a
different way, because today the app receives the raw global config and decides
locally. The server has to resolve the section list for the calling user and
serve only what that user may see. That is exactly P1.4, with P5.4 for the
targeting. Until then the only honest mitigation is documentation.

### #29, native Jellyfin notification events

Two years old and still the best statement of what the notification subsystem
should be: configurable events, per event enable, target users, forward to
admins. The discussion in the thread went further than the title. lostb1t
proposed, and herrrta agreed, a general per user override shape:

```yaml
settings:
  notification_new_movie:
    lock: true
    value: true
    overrides:
      - users: [john, simon]
        lock: false
        value: true
```

That is the three level model of P1, proposed by the maintainers before this
rewrite was scoped. Worth quoting in the P1 spec, since it means the design is
not being imposed on the project from outside.

Lands in P4.4 for the declared events and P4.5 for the per user preferences, both
on top of P1.

### #17, support for CultureDto

Language properties need a real type in the schema instead of free text. Waiting
on P1.1, then it is a form control in P3.1. Small once the schema is typed,
awkward before.

## Notifications

### #34, customizable notification messages

The requester wants the Handlebars templating the Jellyfin webhook plugin offers.
herrrta pushed back with a real constraint: the webhook plugin waits for metadata
to be loaded before firing, while this plugin consolidates events, for instance a
whole season at once, and therefore has much less metadata to interpolate.

Do not promise templating. What P4.4 can honestly deliver is per event, per
locale message overrides on the fields that are actually populated. Say so on the
issue rather than leaving it open as if full templating were coming.

### #30, images in notifications

Expo's push service does not carry images on iOS. The linked Expo discussion has
a working Android path. Ship it Android only under P4.4, with the limitation
written into the setting description, or say no. Leaving it open with no decision
is the worst of the three.

## Home sections

All three are the same structural gap: the section model has four hardcoded
shapes (`items`, `nextUp`, `latest`, `custom`) and no way to add a fifth without
another nullable sibling. P5.1 replaces that with a discriminated type, and then
each of these becomes one new kind.

- **#78, My Media.** A section listing the user's library folders, like the
  default Jellyfin home. New kind.
- **#21, Recommended.** lostb1t already pointed at the Jellyfin endpoint that
  serves this. New kind, thin wrapper.
- **#93, section ordering.** Sections currently render in YAML order. An explicit
  `order` field is trivial in the model. The real reason it is worth doing is
  P5.3: a reorderable editor needs a persisted order to write to, so this is a
  prerequisite rather than a nicety.

## Not bugs

### #88, Collections in `includeItemTypes`

`includeItemTypes` is `BaseItemKind[]` (`Settings.cs:95`), so it already accepts
collections. Jellyfin calls them `BoxSet`, not `Collections`, and nothing in
`examples/full.yml` says which values are legal. The reporter tried the obvious
word, got no error and no results, and filed a feature request for something that
already works.

That is the whole case for P3.1 in one issue. A generated form renders a
`BaseItemKind` field as a list of the values that exist, and the question cannot
be asked again. Until then, one line in `examples/full.yml` pointing at the enum
is worth writing.

## Third party integrations

### #95, `seerrServerUrl` does not work but `jellyseerrServerUrl` does

Accurate. The plugin schema still says `jellyseerrServerUrl` and
`jellyseerrApiKey` (`Settings.cs:297` and `301`) while the app and its
documentation have moved to Seerr. Renaming is a breaking change for every
existing YAML, so it needs the alias mechanism rather than a find and replace.
P6.1 groups integrations into typed blocks, which is the moment to rename with an
alias kept for the old key.

### #82, automatically link Seerr accounts to Jellyfin users

The interesting part is in the comments, not the body. The requester asked for
the Jellyfin Enhanced approach, an admin API key that the plugin uses on behalf
of everyone. herrrta rejected that and changed Seerr instead so a client can
authenticate with a user's own Jellyfin access token
([seerr#2244](https://github.com/seerr-team/seerr/pull/2244)).

That decision is already made and it is the right one, since it is the same
admin key that #1 of [state-of-the-plugin.md](state-of-the-plugin.md) says every
authenticated user can currently read. P6 should implement the token path and
plan for the API key setting to become optional, then go away.

### #108, Seerr integration missing on tvOS

App side, belongs on the streamyfin repository. Worth keeping in view here
because P6.3, exposing integration health, is what would have turned "is this
something I'm doing wrong" into an answer on screen.
