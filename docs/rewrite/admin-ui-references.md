# What the good plugins do, and what we take

An audit of Jellyfin Enhanced's configuration page, read tab by tab on the beta
(LXC 132, Jellyfin 12.0.0) rather than from its source, because the point was to
judge the ergonomics and not the markup. It is the plugin whose admin surface is
most often called the best in the ecosystem, and this records why, so the answer
outlives the browsing session.

Its page is **698 KB of hand written HTML with a 78 KB stylesheet of its own**. That
is the scale of investment behind "it looks nice", and it is worth knowing before
deciding how much of it to reach for.

## The one thing that makes it look native

Every accented colour resolves from Jellyfin's own theme variable:

```css
--je-accent-border: color-mix(in srgb, var(--primary-accent-color, #00a4dc) 40%, transparent);
```

So the page follows whatever accent the server administrator picked, instead of
imposing one. Around that sit four token scales, redefined per theme and nothing
else: surfaces, borders at four strengths, text at four strengths, and a card
gradient with a "lift" variant for hover. That is the whole system. It is small,
and it is why the page reads as part of Jellyfin rather than as a guest.

## Tab by tab, and what each one is for

| Tab | The idea worth stealing |
|---|---|
| **Overview** | A read-only snapshot: what is connected, what is enabled, what is misconfigured. Clicking any card jumps to the tab that owns it. The page opens on a diagnosis, not on a form. |
| **Display** | Three column card grid, one card per sub-group. A dependent setting stays visible, greyed, with the reason in its place: *"Enable Show watch progress to configure"*. |
| **Playback** | A setting that needs another plugin is greyed with an inline note, *"Install Intro Skipper plugin to enable"*, rather than hidden or silently broken. |
| **Pages** | Each card opens with a short "how this works" panel before any control, so a feature explains itself where it is configured. |
| **Seerr** | A master toggle at the top of the card, accented, gating everything under it. A `Test` button beside the credential. A field hint in the label, *(One per line)*. A note saying a value is shared with another tab. |
| **\*arr** | **Repeatable instances.** One card per service, each instance a collapsible row with its own toggle and URL, plus *Add instance*, *Validate mappings*, *Import from Seerr*. |
| **Elsewhere** | Cross-references between tabs, a *How to?* link to the docs beside the field that needs it, and an unmet requirement stated as a plain hint. |
| **Extras** | A `▸ View screenshot` disclosure per setting, showing what the toggle actually changes. A separate amber caveat block, distinct from the description, for a limitation. |
| **Keyboard** | **The override list.** A picker plus an *Add override* button, and a *Current overrides* list underneath. Exactly the shape a level-scoped override needs. |
| **Admin** | Full width prose cards, a *New in v12.5.0.0* badge on the header, and a privacy explanation stating precisely what is collected before the opt-in is offered. |
| **Docs** | The documentation site embedded in the page, with *Open in new tab*. Help never costs a context switch. |

Constant across every tab: a **global search** over all settings, a **Descriptions
on/off** toggle that strips the help text once you know the page, **sub-group
chips** acting as in-page anchors, and a **floating save dock** carrying an unsaved
indicator.

## What we take, and why it fits our problem

**The card grid.** Ours is 92 settings in 8 categories with sub-groups. A single
column reads as a wall and wastes the width; a grid of sub-group cards is exactly
the shape our `x-category` and `x-group` metadata already describes.

**The descriptions toggle.** 80 of our 92 settings carry a description. They are
the reason the form is readable the first time and the reason it is slow to scan
the tenth. One toggle serves both.

**The global search.** With 92 settings across 8 tabs, hunting is the default
failure. Search is the answer, and our form descriptor already carries every title,
key and description to search over.

**The override list, for targeting.** P3.3's blocker was that json-editor's property
picker never actually added a setting. The Keyboard tab's shape replaces it
outright: pick a setting, add it, see the current overrides listed. No modal.

**The dependency treatment.** We have real cases: the two native player toggles
only matter on a TV, `subtitlesOnMuteAllowRestart` only matters when
`subtitlesOnMute` is on. Greying with the reason in place beats hiding, which is
what the app does to its own users today.

**The inline test.** P6.2 already plans a server side connection probe for the
integrations. `Test` beside the Seerr URL is where it belongs.

## What we do not take

**The Overview tab**, for now. It is the best idea on their page, and it needs
health data we do not collect yet. It belongs after P6.3 exposes integration
health, not before.

**The embedded documentation.** We have no documentation site to embed. The
Docusaurus repo exists and sleeps.

**The screenshot disclosures.** They cost one screenshot per setting, maintained by
hand, across 92 settings. The value is real and the upkeep is not ours to take on
yet.

**Their scale of investment.** 698 KB of hand written page is what buys their
polish. Ours is generated from `SettingsForm.Describe()`, so the equivalent effort
goes into the renderer once rather than into every setting. That is a different
trade and, for 92 settings that change with the app, the better one.

## The difference that stays ours

Their settings are on or off. Ours have three states, because the app has three
behaviours: left alone, pushed once as a starting value the user can still change,
or pinned. And ours resolve across levels, so a setting can be answered by the
server, by a group, or by one user. Nothing on their page has to say either of
those things, so nothing on their page shows us how. That part is ours to design,
and it is where the boldness belongs.
