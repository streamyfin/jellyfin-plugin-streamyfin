# Pull request triage

The pull requests that were open when the rewrite started, diagnosed against the
code on both sides rather than against their descriptions. Rewrite pull requests
are tracked in [issue #114](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/114)
and in [plan.md](plan.md), not here.

## Summary

| PR | Age | State | Verdict |
|---|---|---|---|
| [#109](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/109) | opened 2026-07-30 | clean | Right feature, wrong keys. Settle the names, the allow restart setting and the default before merging. |
| [#81](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/81) | opened 2025-11-18 | clean, never reviewed | Real feature, collides with P4.4. Decide, do not leave it rotting. |
| [#71](https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/71) | opened 2025-09-16 | conflicting | Close. |

## #109, lockable auto subtitles on mute

Two lockable booleans, `autoSubtitlesOnMute` and `autoSubtitlesOnMuteAllowRestart`,
declared so the plugin can push and lock the app's mute behaviour. Eleven lines,
CodeRabbit found nothing, and the reasoning in the description is right: a key the
plugin does not declare resolves `locked` to `undefined`, so the lock can never
take effect.

**The keys do not match the app.** In `streamyfin/streamyfin` the setting is
`subtitlesOnMute`, a single boolean:

| Where | What |
|---|---|
| `utils/atoms/settings.ts:378` | `subtitlesOnMute: boolean` in the settings type |
| `utils/atoms/settings.ts:552` | default `false` |
| `components/settings/SubtitleToggles.tsx:442` | the switch, iOS only and not TV |
| `providers/NativePlayerProvider.tsx:1039` | the behaviour, native player only |

Neither `autoSubtitlesOnMute` nor anything resembling an allow restart setting
exists anywhere in the app. Merged as it stands, this pull request ships two keys
nothing reads, and the lock it is meant to enable still does nothing.

Three things to settle before it merges:

1. **Rename** `autoSubtitlesOnMute` to `subtitlesOnMute`, so the key the plugin
   pushes is the key the app resolves.
2. **The second key does not exist yet.** Either drop it, or add the setting to
   the app first and land the plugin side after. Declaring it now is the same
   mistake in the other direction.
3. **The default disagrees.** The pull request defaults it to on, the app defaults
   `subtitlesOnMute` to `false`. An unlocked plugin value is applied once as a
   default, so merging as is turns the feature on for every user who has not
   already chosen. That may well be the intent, but it should be a decision.

One more thing the description assumes and the code does not do: the switch in
`SubtitleToggles.tsx` has no `disabled` binding, unlike the orientation control in
`OtherSettings.tsx` which reads `pluginSettings?.defaultVideoOrientation?.locked`.
A locked value is still enforced centrally on read and on write, so locking works,
but the switch will not grey out until that binding is added. Listed in
[app-side-work.md](app-side-work.md).

## #81, Seerr webhook notifications

A `POST /streamyfin/notification/seerr` endpoint that receives Seerr webhooks and
turns them into push notifications: `MEDIA_PENDING`, `MEDIA_AUTO_APPROVED` and
`MEDIA_FAILED` to admins, `MEDIA_APPROVED`, `MEDIA_DECLINED` and `MEDIA_AVAILABLE`
to the requesting user, issue events filtered out. 755 lines, a mapper, a payload
model, and a new `Strings.zh-CN.resx`.

It has sat for nine months with **zero comments and zero reviews**. Not one person
said yes or no. That is the part worth fixing first, whatever the outcome.

The feature is real and users want it. What has to be settled:

- **Naming.** Everything in it says Jellyseerr. The project has moved to Seerr,
  which is what issue #95 is about. New code should not arrive already carrying
  the old name.
- **It hardcodes the event routing**, which is exactly what P4.4 replaces with
  declared events. Merging it adds surface that P4 then has to migrate. Holding it
  makes a contributor wait on a part that is five steps away.
- **A new locale.** `Strings.zh-CN.resx` is the first Chinese resource in the
  plugin. The plugin has no translation platform, unlike the app which uses
  Crowdin, so accepting it means accepting that someone maintains it by hand.
- **The endpoint carries `[Authorize]`.** A webhook cannot log in as a Jellyfin
  user, so this only works if the admin sets an authorization header in the Seerr
  webhook agent. That works, but it is undocumented in the pull request and it is
  the first thing anyone configuring it will hit.
- **It logs the full payload at debug level**, which includes the requester's
  username and email. `StreamyfinController.cs` serialises the whole webhook body
  into a debug line, so turning debug logging on writes other people's contact
  details into the server log.

Recommendation: take the feature, with the renaming, and accept that P4.4 will
reshape it. Nine months of silence is worse for the project than a migration cost
we already signed up for. The payload logging is not part of that cost: log the
notification type and the subject, not the body, before this merges. That is a
one line change and it is not something to inherit deliberately. Say all of this
on the pull request rather than leaving it open for another nine months.

## #71, TV sidebar links

`settings.tvSidebarLinks`, letting an admin declare custom TV sidebar entries
pointing at libraries or collections, each able to carry curated sections.

Close it. Three independent reasons:

- **The maintainer already said so.** Comment of 2026-06-17: "I think this PR need
  to be completely re-done since tv as change a lot."
- **Nothing consumes it.** There is no `tvSidebarLinks`, no `sidebarLinks` and no
  `SidebarLink` anywhere in the app. Merged, it is config an admin can write and
  no device will read.
- **It conflicts with `main`** and has since well before that comment.

There is a fourth reason to close rather than rebase: it reuses the existing
`Section` model, which is precisely the model P5.1 replaces with a discriminated
type. Rebasing it now means rebasing it again in P5.

Close with the reasoning, point at #114 and P5, and say the idea is wanted back
once the section model is settled and the TV app side exists. The contributor put
real work in; the close should read like a postponement, because that is what it
is.
