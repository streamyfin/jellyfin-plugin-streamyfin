# The targeting screen

This is P3.3. Groups and per user overrides get the screen they never had. P3.2 (home
sections), P3.4 (JSON import and export) and P3.5 (embedded pages versus
`jellyfin-plugin-pages`) are out of scope and named at the end.

## The drift this closes

P1.2, P1.3 and P1.4 built the whole targeting engine: groups with a priority,
memberships, per user overrides, resolution from server default to group to user, and
credentials filtered out of what a non admin receives. Seven routes, a database table
each, and tests. **And no screen at all.**

So the feature exists and nobody can reach it. An administrator who wants one group to
get a different default bitrate has to write the HTTP requests by hand, with a Jellyfin
user id they have no interface to look up. That is the same shape as the finding P3.1
closed, where 72 of 92 declared settings had no control: the work is done, the door is
missing.

| | |
|---|---|
| Targeting routes the plugin serves | 7, plus the one added here |
| Ways an administrator could use them before | hand written HTTP |
| Ways now | a tab |

## The route that was missing

`users/{userId}/settings` had a `PUT` and a `DELETE` and no `GET`. Nothing had needed to
read one back, because nothing read one at all: the resolution reads the *caller's*
override, never a named user's. A screen that edits an existing override needs to open
it first, so this part adds:

```
GET v1/users/{userId}/settings
```

It is the only targeting route with **no unversioned shim**, and deliberately so. The
shims in P1.6 exist for paths that were already being called by apps in the field. This
path never was, so it starts life versioned and stays that way.

It reads through `SettingsResolutionService.ReadLevel`, the same tolerant path the
resolution uses, so an override whose stored JSON cannot be parsed still answers rather
than throwing. A level an administrator cannot see is a level they cannot repair.

## What the screen does

A new tab, **Targeting**, beside Application, Notifications, Other and Yaml. Chosen over
a section inside the Application page: that page already carries 92 settings, and "what
this server defaults to" and "who gets what" are two questions, not one.

- **The groups**, listed in the order that decides who wins, each row saying its
  priority, how many members it has and how many settings it overrides.
- **Editing a group**: name, priority, members ticked from the server's actual user list
  rather than typed as ids, and the settings it overrides.
- **One user**: pick a user, edit the settings aimed at them, or clear them.
- **Create and delete**, with a confirmation on the delete, since removing a group
  removes everyone's membership of it too.

## The decision: the same form, one option flipped

The plan called P3.3 a "hand written screen". That was written before P3.1 existed. Now
that the generated form is built, tested and on `develop`, writing a second settings
editor by hand would be duplication, so the group's overrides are rendered by the same
json-editor mechanism the Application tab uses.

The two pages differ in exactly one thing, and it is the thing that matters:

| | Application | Targeting |
|---|---|---|
| The question | what does this server default to | what does this level change |
| `required_by_default` | `true`, so every declared setting renders | `false`, so only the keys the level carries do |
| `disable_properties` | `true` | `false`, so a setting can be added or dropped |
| What a save writes | the keys already present, plus the ones actually edited | whatever the editor holds |

That last row is why the Targeting page needs none of `settingsToPersist`. On the
Application page an editor fills in a default for every key the config was missing, so a
save has to diff against the editor's own first value or it writes all 92. Here the
editor only ever holds the keys the level carries, so its value *is* the answer. The
contract "a group only carries the settings it means to change" is enforced by the
editor's shape rather than by a comparison.

Dropping an override is json-editor's own property picker, which is why this page keeps
the object controls its stylesheet otherwise hides. It is also the only affordance for
*removing* a setting from a level, which a form that renders everything cannot offer at
all.

## What moved to be shared

`Pages/settings-form.js`, a new module, holds what both pages do with the schema: the
blank/null round trip for the "Max" quality option, the category and group sectioning
read from `x-category` and `x-group`, the per section sub-schema, the editor options
they agree on, and the render loop. `Pages/Application/index.js` keeps only what is its
own: the save diff, and its two editor options.

It is a third shared module rather than more of `shared.js`, which is the pages' runtime
state — schema, config, tabs, save. This one is the form.

## How it is validated

**xUnit, for what C# can hold to account.** A page is registered by an
`EmbeddedResourcePath` string, so a wrong one is not a build error: the dashboard serves
an empty tab and nothing says why. `PluginPagesTests` now enumerates the plugin's *own*
page list and asserts every resource path it claims is actually embedded, so a page added
without its file fails, and so does a file renamed without its page. A second test names
the five tabs, because enumerating the list cannot prove a page was never dropped from
it, and a tab that stops being registered is exactly as invisible as one pointing at a
missing resource.

This needed one change in `Plugin.cs`: `_prefix` was a static field assigned in the
constructor, so the page list only answered correctly on a running server. It is now
taken from the type.

**A pass on the beta**, LXC 132, real Jellyfin 12. Everything above is C#; the screen is
JS and only a browser connected to a dashboard exercises it. The scenario to run, which
is the one that proves the engine and the screen agree:

1. Create a group, put one user in it, override one setting and lock it.
2. `GET v1/config/resolved` as that user carries the group's value.
3. The same call as a user outside the group does not.
4. Set an override on that same user and it wins over their group.
5. Reopen both and the form shows what was stored.

**Not yet run.** Tailscale was up long enough to validate P4.1 and dropped again.

## A casing gap to confirm on that pass

Group and user settings travel as JSON through MVC, which keeps the CLR property names.
Every settings type is already camel case in C#, so the wire matches the schema — except
`LanguagePreference`, whose two members are `ThreeLetterISOLanguageName` and
`DisplayName` in PascalCase, because the app matches them against the SDK's `CultureDto`.
The schema camel cases them, as #137 established it must.

If that reasoning holds, a group overriding a default audio or subtitle language would
save and then not show the value back, silently, in 2 settings of 92. It is derived from
how Jellyfin configures its JSON options rather than observed, so it is written here as
a thing to check on the beta pass, not as a finding. If it is real it wants its own
change and its own test, the way #137 did for the YAML reader.

## Out of scope

- **P3.2** home section editor. It would be built on the four nullable siblings
  `items`, `nextUp`, `latest` and `custom` that P5.1 replaces with a discriminated type,
  so building it now means building it twice.
- **P3.4** JSON import and export.
- **P3.5** the embedded pages versus `jellyfin-plugin-pages` decision. This slice stays
  inside the embedded pages.
- **Group section targeting** (P5.4), which needs the section model P5.1 has not
  reshaped yet.

## Delivery

Branch `refonte/p3-3-targeting`, one pull request onto `develop`, squash, body in the
`Part of #114. Covers P3.3.` form the sisters use. The tracking pull request is #121.
