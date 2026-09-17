# Log

What actually happened, newest first. The plan says where we are going,
[issue #114](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues/114)
says what is left, and this says what was done and why, so someone arriving in
three months can catch up without reading a pull request thread.

Append an entry whenever something lands or a decision is taken. A decision that
lives only in a comment thread is a decision nobody will find.

## 2026-09-17, later still: three things an administrator should hear about

P4.4 starts with what the plugin can say. Three events, all to administrators, each with
its own switch and its own wait between two of the same:

- **A scheduled task failed**, with the first line of what it said went wrong, cut at 120
  characters since a push is a sentence and a stack trace is not.
- **A plugin was installed, updated or uninstalled**, with its version.
- **A sign in was refused**, with the name that was tried and the address it came from.
  Five minutes per address by default: a server anyone can reach is tried by machines that
  never stop, and a notification for each of those is a reason to turn the whole thing off.

The wording is Jellyfin's own, taken from the strings its activity log uses for these
events, in the four languages the plugin carries. The culture is a parameter of each
builder rather than the server's, which is what the per device language of #34 will need.

Two things reading the server's own code changed:

**Nothing publishes a task completion.** `TaskCompletionEventArgs` is never handed to the
event manager on either line: `TaskManager` raises the plain C# event and no one forwards
it, so Jellyfin's own `TaskCompletedLogger` and the webhook plugin's task notifier never
run either, and the activity log has said nothing about a failed task for years. This
listens to `ITaskManager.TaskCompleted` instead, which is what the dashboard's own web
socket does.

**Jellyfin calls an upgrade an install.** Its `InstallationManager` publishes the updated
event only when the version being installed is the one already there, a repair, so a
plugin going from 17 to 19 arrives as an install. The version being replaced stays loaded
until the restart, beside the arriving one the install adds straight away, so a copy under
another version is what says it is an update.

### Proven

On throwaway 10.11.11 and 12.0.0, with the three switched on and a registered device.
`ActivityLogRetentionDays` set to -1 makes **Clean Activity Log** throw, which reports
`Failed`: the administrators were told, and the same failure a moment later was not, since
it is inside the wait. Bookshelf installed fresh was an install, Playback Reporting at an
older version was an install, the newest over it was an update, and removing it was an
uninstall. A sign in with the wrong password was reported, and a second from the same
address was not.

A server that was already running has the three off, since its stored configuration does
not mention them, which the throwaways showed before they were turned on. A fresh install
has them on.
## 2026-09-17, later: what a user is told about libraries they cannot open

### Home sections, #69

A section built on a library a user may not open was served to them all the same.
Jellyfin refuses the request that fills it with a 401, "is not permitted to access
Library", so the row stayed empty, but its title named the library. The server now leaves
such a section out of what it serves that user, on `config`, `config/yaml` and
`v1/config/resolved`.

The check is Jellyfin's own rather than a copy of it: `GetItemById` with a user answers
only an item that user may see, which covers the libraries they were given, their
parental rating and their tags, and it reads the same on 10.11 and 12 although the two
implement it differently. The library is read from every place a section can name one:
the items, next up and latest payloads, and a `ParentId` among the query parameters of a
custom endpoint, whether in its query map or in its own address, which the app sends as
written and Jellyfin binds whatever the case.

The administrator editing the configuration still gets every section, since the page
saves what it loads. And the filter replaces the home it hands out rather than editing
it: resolution passes the stored sections through by reference, so removing one in place
would have removed it for every later caller and then from the stored copy. A test holds
that, and so did the real servers: the administrator's read after the restricted user's
still listed all five.

Reading the app for this showed it ignores `parentId` on `latest` and `nextUp` sections,
so those two show everything the user may see whatever the administrator named. That is
the app's to fix. The plugin leaves such a section out all the same, since its title still
names the library.

### New items

A new movie or episode was announced to every registered device, so its title reached
people who could not open its library, or were not allowed its rating. It now goes to the
devices of the users who may open it, asked with `IsVisibleStandalone`, which is how
Jellyfin filtered its own new content notifications before it dropped them. A disabled
account is told nothing either, the administrator notifications included: Jellyfin
refuses every request such an account makes, and its devices were still registered here.
The season send also had its awaitable discarded, so a failure went unobserved; it goes
through the same detached path as the movie send now.

### One owner per push token

Review found a way around that filter. An Expo token belongs to an installation of the
app, and Expo keeps an iOS token through an uninstall and a reinstall while the app starts
over with a new device id. Someone who deleted the app while signed in, and whoever signed
in on it after the reinstall, then shared a token on two rows, and the first account's
notifications reached the second, the administrator ones included. Expo never reports such
a token as gone, so the row stayed.

A registration now removes every other row carrying its token, in the same transaction as
its own write, and opening the database leaves each token already stored on one row, the
newest. Two tests from #180 registered one token under several devices, which registration
no longer allows: the race is held with three dead tokens instead, and the test for several
rows of one dead token is gone with the state it described.

Review then found what that made possible. The two device routes were authorized and that
was all they checked: the account asking was never compared with the account the body
named, so anybody signed in could register a device under somebody else and be sent what
that person is sent, and could remove a device that was not theirs. With registration
removing the other rows carrying its token, the same request became a way to take a device
away from its owner. A registration is now for the account making it, or for whoever an
API key names, since a key is an administrator's; one that carries no push token is refused
rather than stored, since it could receive nothing and would take the other tokenless rows
with it; and a removal only reaches a device of the account asking.

The pruning of dead tokens had the other half of the same problem. Expo answers about the
installation a push went to, and a receipt is collected up to a day later, so a device that
registered the same token in between was removed by an answer that said nothing about it.
Each dead token now carries the moment its push was sent, and a row written after it stays.

Three more from the second review. The resolved settings route filtered an administrator's
own sections as well, since it passed the library question whatever the caller was, while
the configuration route skipped it for an elevated one: the question is now answered in one
place, and it is no question at all for an administrator or an API key. A registration
stamped itself before waiting for the write lock, so a push sent in between could take the
row that landed after it for the device it was sent to; it is stamped once the lock is
held. And a message about a season's episodes was authorized on the season alone, while it
names how many arrived and, for a single one, its number and its id: an episode carries its
own rating and its own tags, and `IsVisibleStandalone` looks at what it is given and its
parents, never at its children, so every episode in the batch is checked now.

On both throwaways, a token stored under the administrator and then the restricted user was
cut to the user's row at the next start, and a movie in the other library went to one of
two devices, the administrator's own. The same token registered by the administrator and
then by the user moved to the user, and the next movie went to one of three, a control
device of the administrator. The routes answered the same way on both lines: the restricted
user registering under the administrator was refused with a 403, a registration with no
token with a 400, one naming nobody was stored as the user's own, and the user's attempt to
remove the administrator's device left it where it was, which the administrator then removed
themselves. Two episodes of one season added together were sent as one message, to the
devices of the users who may open all three of the items it names.

### Proven

On throwaway 10.11.11 and 12.0.0: two libraries with a generated movie in each, and a user
limited to one of them. That user was served two of the five sections on all three routes,
the administrator all five, before and after. A movie added to the other library went to
three of four devices, and one added to the shared library to all four. A second pass,
after review, put the private library in a custom endpoint's own address, which that user
was not served either, and disabled the account: Jellyfin refused it with a 401, and a
movie added to the library it could open went to the other device only.

The same pass caught a race in the pruning of dead tokens: two sends told by Expo about
the same dead tokens at once, and the second one's delete failed. It is fixed in #180.

## 2026-09-17, through the catalogue and back

### An update the server makes by itself

The path a stable user will take, run end to end on two fresh throwaway servers,
Jellyfin 10.11.11 and 12.0.0, instead of dropping a DLL over another. 0.68.1.0
installed from `manifest.json`, given a configuration through its own Yaml tab and a
device through `POST /Streamyfin/device`, then `manifest-unstable.json` added and the
**Update Plugins** task started.

Each line took the build it can run. 10.11 is never offered the `12.0.0.0` entry and
installed the net9 zip; 12 lists both entries and installed the net10 one. At the
restart the old version was marked superseded and deleted, four migrations ran, the
configuration and the token were imported, and the old file and the old database were
left alone, the XML to the bit. The task also runs on its own at startup, so a server
carrying both repositories updates without anybody starting it.

### Going back, and staying there

Uninstalling from the dashboard deletes the plugin's folder at once and leaves the XML
and both databases where they are. 0.68.1.0, installed again from the catalogue, reads
its configuration back exactly as it wrote it, and its token rows are the ones it had
before the update.

Staying on it is the less obvious half. The dashboard has no switch for automatic
updates: `InstallationManager` skips a plugin whose `meta.json` says
`"autoUpdate": false`, or one that is disabled, and nothing else. With that set, both
repositories listed, the task started by hand and two restarts, 0.68.1.0 stayed. Once
0.70.0.0 is out that is the only way to keep the older version, so the README now says
it, beside the three steps back.

### What coming back up lost

Updating again after going back served the configuration as it stood before going
back. A skip time changed from 45 to 60 under 0.68.1.0 came back as 45, on both lines,
and nothing in the log said so. The import had run, its marker was set, and the file
was not read again.

This version never writes that file, so whatever differs between the file now and the
file as last read was written by something else. The copy is kept as a second row
beside the configuration, and every start compares the two. Each entry the file changed
or gained, and that differs from the value used here, is named once in a warning. Names
only: one of those entries is the Seerr key.

The first version of the repair took those changes over, and the review of it found why
it must not. The file is not a record of what an administrator chose. 0.68.1.0 knows 43
of the 92 settings and writes the file without the others, so a fresh 0.70.0.0 server
that went back and saved once would have lost 49 settings on the way up. And when the
file is missing or unreadable, Jellyfin fills it with the running version's defaults:
under 0.68.1.0 that is a rewind of 15 seconds, subtitles at 80, and a hidden library
called "Enter library id(s)", which the update would have applied to every user. Making
that safe meant a table of old defaults and a list of settings simple enough to trust.
Naming the changes costs an administrator a minute, once, and cannot apply a value
nobody chose, so that is what shipped, decided on the day.

The rest follows from the same two facts. An entry the file lost is not named, since that
is what an older version does to every setting it never had. A file holding exactly this
version's defaults is not a change either; one an older version filled with its own is
named like any other, and the warning says those may be defaults to ignore. The
comparison treats an empty list as no list at all, because that is all the file can say:
Jellyfin's XML serializer creates every list it meets, so a list left null comes back
empty. The real 12.0.0 server showed that one. A deleted file was rewritten with the
defaults, which was recognised, and the next start read the same file back with an empty
genre list in every home section the defaults never had. A server imported by a build
that kept no copy is compared from its first start on a build that keeps one.

A second review found the copy written before the warning, so a start failing in between
would have lost the change for good while its log line promised another comparison. The
copy is written last now, and a test breaks the stored configuration to prove it.

Proven on the 12.0.0 throwaway, in the case the review described: a file holding this
version's defaults, 0.68.1.0 installed through the dashboard, five settings saved there,
which rewrote the file from 351 lines to 26. Up again: four settings named, the fifth
left out because it already matched the value used here, none of the settings the rewrite
dropped mentioned, nothing applied, and the next start said nothing. The net9 build
loaded on 10.11.11 and kept its copy.

Device tokens are left as they are. One registered while the older version ran is
missing after the update, but the app posts its token at every cold start, so it comes
back by itself. What does not come back is a device that signed out while the older
version ran: its row stays until that device signs in again or loses the app.

## 2026-09-16, the last three pages, and the admin UI put through its paces

### One plugin, not two

Application and Targeting had been on the cards, rows and three state segments of
`settings-form.css` since P3.6. Notifications, Other and the Yaml editor were still
wearing the dashboard's own form controls, so two tabs of the same plugin looked like two
different plugins. They now share the vocabulary: the top row with the page's name, a
card per subject, a row per setting with its key underneath, and the save dock that says
whether there is anything to save rather than offering a button that is always ready.

Porting them turned up two defects that had nothing to do with styling.

**The start page select never reached the configuration.** The Other tab wrote the chosen
value nowhere, and a save dumps the configuration the page is holding, so Save on that tab
stored what was already there. The setting could not be changed from the page that offers
it. It is written on change now, and a save was watched storing `Targeting` and surviving
a reload.

**Every page looked its elements up in the whole document.** The dashboard keeps the views
it has already shown. Once Application had been opened, its hidden copy of a shared id
answered first, so the theme attribute landed on the wrong page and the page behind it
stayed dark on a light dashboard. The dock these pages gained would have written into
Application's for the same reason. Each page resolves inside its own view now, which is
what Application was already doing.

The theme is no longer read once and hoped for either: the dashboard swaps its stylesheet
after the view is shown and announces nothing, so `applyTheme` watches the head for the
swap, settles over the first frames when the theme was already applied, and tells the Yaml
editor when it changes, since Monaco cannot re-read a CSS variable.

### The pass

Section 2 of the release checklist, done as a script rather than by hand, on the beta in
Jellyfin 13.0.0, in both themes. Fifteen scenarios, fifteen green, each one acting in the
page and then asking the server what it stored.

A toggle through all three states and the key disappearing when it goes back to free. A
number refusing 100 against its bounds and 2.5 against its integer, the dock saying
`1 unsaved · 1 to fix`, Save going dark and `Show me` appearing, then 30 stored. Text
stored as typed. A select locked on its value. A list of two lines arriving as an array of
two. A secret revealing and hiding with nothing to save behind it. A dependency inert only
while its parent is locked off, with Free still reachable. A setting the app declares no
default for saying `App default` and refusing an empty suggestion. The search reaching
across categories and the Set filter showing only what is set. The two switches and the
banner surviving a reload. One column at 760px with nothing hanging off the side. An
ordinary account seeing a save at once and no key anywhere in what it receives. A broken
Yaml file refused with the configuration unchanged. Five tabs in a row with no ghost Monaco
editor, one dock in view and a clean console. And a group created on Targeting with an
override, found on the server afterwards.

The configuration was backed up through the plugin's own route before the pass and restored
after it, and the backup taken afterwards matches the one taken before section for section.
The throwaway accounts are deleted. The harness stays out of the repository: it carries a
server address and two passwords.

## 2026-09-15, later: the findings nobody had read, and a pass on Jellyfin 13

### Merged on a green check that says nothing

#165 to #168 went in on `CodeRabbit: pass` in `gh pr checks`. That check passes with
ten findings open; it says a review ran, not that it found nothing. The reviews on
#166 and #167 had been rate limited when the branches were pushed and arrived later,
and nobody looked again before merging.

#168 was carrying five. Four were real, and the worst of them was mine. The guard I
had added against publishing the same commit twice refused every tag that already
existed. The tag and the release are created before the manifest is pushed, so a
failure between those two steps left a release nobody could reach and a rerun that
stopped on the guard. A publication could get stuck half way with no way out.

On `main`, where these workflows live and fire from, #170 splits the two cases the guard
confused: a tag on the very commit being published means an earlier attempt got that far,
so the run carries on and replaces the assets; a tag on a different commit is a real
collision and still stops. It also bounds what the publish job will run, since that job
holds `contents: write` and the dispatch input decides which `Makefile` and which scripts
it executes: the ref has to be an ancestor of `develop`. `persist-credentials: false` on
the build checkout, which never pushes. And `housekeeping.yml` gets `actions: write`,
without which `actions/stale` cannot save its place and starts from the first item every
time, plus a concurrency group so a manual dispatch cannot overlap the schedule.

One of the five was wrong: CodeRabbit read the `Makefile` as naming both targets'
archives identically. It names them per target and has since #126, and the published
release carries both zips. Answered with that and resolved, without a commit to make
the bot happy.

The review of #170 itself then found something real in the repair. `git ls-remote`
against an exact tag ref answers with the id of the tag object, not of the commit,
so an annotated tag would have compared unequal to the commit it points at and read
as a collision with itself. Ours are lightweight and the published tag answers with
the commit, so nothing was broken, but a tag pushed by hand would have been. The probe
now takes the peeled line when it is there, checked on a scratch repository against no
tag, a lightweight tag, an annotated tag and a sibling tag sharing the prefix.

The catch-up path is not argued, it ran: the workflow was dispatched twice against a
commit that already carried its tag, and the log says `unstable-0.68.1.59 already
points at d95c6ed..., so a previous run got this far. Carrying on.` Both zips were
replaced and `manifest-unstable.json` was rewritten with two entries and no duplicate.

#171 carries the same two files back to `develop`, since #170 had to land on `main`
and the copies would otherwise drift until the integration branch silently reverted
them. Until that one merges, the branch this entry is written on still carries the old
workflow, which is why the paragraph above says where each change lives.

### The admin pages, on Jellyfin 13

All five, in a real dashboard on the beta, captured rather than asserted: Application
with its 92 settings and the three states, Targeting with a group and a locked
override, Notifications, Other with the backup section, and the Yaml editor. Dark and
light for the first four.

The Yaml editor looked dead at first and is not. `monaco-editor.bundle.js` is 11.1 MB.
The server hands it over in 7 ms from inside the container and in 47 seconds across a
VPN link, so a page that waits twelve seconds sees an empty editor and concludes it is
broken. Worth saying in the release notes for an administrator working remotely.

### The app against the plugin, end to end

The chain that had only ever been checked one half at a time. A build of the app's
`develop` on an iOS 27 simulator, signed in to the beta on Jellyfin 13.0.0, its
"refresh settings from the server" reaching the plugin controller. Then a per user
override, `PUT v1/users/{id}/settings` with `defaultVideoOrientation` set to landscape
and locked: the app moves to that value and greys the row out. Override removed, the
app goes back to its own value and the row is editable again.

An ordinary account gets no secret from any of the four read paths, `config`,
`v1/config`, `v1/config/resolved` and `config/yaml`, all answering 200 with no key or
token anywhere in the body.

The app half of #110 is settled by the same screen: it offers five orientations and
one of them is the automatic landscape, which is what the reporter said in the first
place.

One thing that is app side and worth recording because it decides how this gets tested
from now on. A local build with Xcode 27 links against the iOS 27 SDK, and iOS 27
refuses to launch an app that has not adopted the UIKit scene life cycle. Expo adopts
it in SDK 58; the app is on 57. Until it moves, a local iOS build needs a scene
delegate added by hand in the generated `ios/` folder, which is not in the repository.

## 2026-09-15, the unstable channel, and four things that were wrong

### Loaded on a real server, at last

The load test this dossier has been deferring since 2026-08-25 is done. The whole
of `develop`, built as `jf12`, on the beta running **Jellyfin 13.0.0**: no load
error, nothing of its own in the log, the four satellite assemblies loaded, the
drawer logo registered with File Transformation.

The database half is the part that was really waiting, and it reads better than a
fixture ever could. Four EF migrations applied. `ImportMarkers` holds
`legacy-device-tokens` with 34 rows imported on 2026-08-25 and `legacy-global-config`
with 1. The old `streamyfin_plugin.db` still holds exactly those 34 rows and is dated
May, so it has not been written to since. The new table holds 37 across 20 users, so
three devices have registered through EF since. That is the P0.4 contract, read off
production data.

### The channel

`develop` is the unstable channel and `main` is the stable one, and that is now in
the plumbing rather than in a convention. `prerelease.yml` publishes a build of
`develop` as a prerelease and writes it into `manifest-unstable.json`.

Numbering was the decision worth taking care over. An unstable build is numbered
**above the release it follows**, 0.68.1.1 upwards counting commits, rather than
below the one it is heading towards. Jellyfin updates a plugin by comparing versions
and nothing else, so the tempting scheme strands the tester: on 0.69.0.5 they would
never be offered the 0.69.0.0 that eventually ships. This way, removing the URL is
the whole way back.

The first build, `0.68.1.59`, is published, installed on the beta from the
catalogue, and loaded. The beta tracks the channel from here.

### One manifest, not one per line

Pull request #126 gave each Jellyfin line a manifest of its own and that was the wrong shape: it
turns a server upgrade into a configuration change, since somebody moving from 10.11
to 12 has to know the URL they pasted a year ago is now the wrong one. #165 collapses
it to one file per channel, which is what `manifest.json` always did anyway, its 62
entries carrying three different `targetAbi` values between them.

Checked on the live Jellyfin 13.0.0 rather than argued: an entry with `targetAbi`
99.0.0.0 is dropped by the server, and two entries sharing a version both survive
with the higher ABI listed first, which is the order the writer produces and the one
the install path depends on.

### Four things that were written down and were not true

Worth recording as a class, because they were all reached the same way: by checking
one source and concluding.

**No Jellyfin 13 package exists.** It does. `13.0.0-20260914101923`, published
2026-09-14 on GitHub Packages, `https://nuget.pkg.github.com/jellyfin/index.json`,
which is where the weekly builds of `master` go and where a new line appears first by
months. nuget.org and the abandoned Azure DevOps feed had been checked; that third
one had not. `nuget-watch.yml` read only nuget.org too, so the watch that exists to
catch exactly this would never have said a word. It reads both now.

**Jellyfin refuses a plugin folder with no `meta.json`.** It does not.
`PluginManager.LoadManifest` falls back to the folder name and returns the plugin as
supported. The real consequence is worse: the id becomes the MD5 of the folder name,
so the server never matches it to the catalogue entry and the plugin never receives
an update again.

**The app is missing "Landscape auto" in one of its two orientation pickers.** The
second picker, `OtherSettings.tsx`, is dead code, orphaned since #1178 and imported
by nothing. The live one has offered it since January. The issue body said so all
along: the reporter wrote that the app has the option and the plugin does not. A pull
request had already been opened on the app repository before anyone read that
sentence; it is closed.

**No scheduled run has ever happened in this repository.** `runs schedule: 0`.
`security.yml` and `housekeeping.yml` had ten runs each, all of them pushes. Both
triggers need the workflow file to be on the default branch before they exist at all,
and these lived only on `develop`. What each does from there differs and is worth
keeping straight: `schedule` runs the default branch's copy, while `workflow_dispatch`
becomes available and can then be pointed at whichever branch you choose, which is how
the first unstable build was published from `develop`. Either way the weekly CodeQL
scan, the stale sweep and the NuGet watch had been decorative since the day they were
written. #168 puts the four that need it on `main`.

### #81, picked up

The Seerr webhook, open since 2025-11-18 with no review, whose author said on
2026-09-01 that they no longer had the bandwidth. Ported rather than rebased, since
the branch predates EF Core, the settings model and the rename.

Three things changed from what it proposed: it is Seerr everywhere; the payload is
not logged, since Seerr sends the requester's email and Discord id beside their
username; and an event naming no requester goes to administrators rather than
producing the target-nobody combination that makes the endpoint send to every device
on the server.

Verified end to end on the beta: 401 without a key, 202 on an issue event, and a
`MEDIA_APPROVED` that reached one iPhone, in French, with the deliberately planted
email appearing nowhere in the log.

## 2026-09-12

### A probe that grew too big, and what cutting it back taught

P6.2 and P6.3 landed in #160 and then lost 188 lines in #161, which is worth
writing down because the second half is the lesson.

The feature is small: ask a service whether it answers, say what came back, and
let the app know. What it grew instead was a cache of lazily started rounds each
timing its own completion, and a copy of the server's address rule in the browser
so a bad address could be marked on the field. Those two blocks carried ten of
the defects found while reviewing it, including a cache that never expired on a
normal server and a client rule that disagreed with .NET in both directions at
every revision.

Both are gone. The cache is one answer kept for half a minute. The form checks
that a value starts with a scheme and leaves the rest to the server, which
already refuses an address by name. Nothing an administrator sees changed.

The rule that came out of it: reviewing the same branch over and over stops
paying once the corrections start introducing as many defects as they remove. On
three of those rounds, three to four of the findings were regressions from the
round before.

### P3.4, and the backup Jellyfin does not take

Jellyfin backs up its own XML, which holds the plugin's configuration. It does
not back up the plugin's database, which holds the targeting levels, and those
are the work: the groups, who is in them, and what each one overrides.

The Other tab hands over one file with all three and takes one back. It carries
the Seerr key, because a backup that cannot restore a working server is not one,
and the page says so above the button.

Two things only a real server showed, both found by taking a backup of the beta
and putting it straight back. The restore reads the body itself rather than
letting the framework bind it, because a setting is a required member on
`Lockable<T>` and the serializer omits a null, so model validation refused a file
this plugin had produced. And the backup is written with the plugin's own
serializer, since the framework writes an enum as its name where the reader
expects the number it stores.

The review found two more that matter. The restore was five separate writes with
no rollback, so a failure partway through left a server with neither what it had
nor what the file carried; the levels are replaced in one transaction now. And a
restored group kept a fresh id, which silently reorders groups that share a
priority, so restoring a file onto the server it came from was not a no-op.

### P3.5, and the fifteen megabytes that are not what they looked like

The question was whether to keep serving the admin pages as resources embedded in
the DLL, or to move to `jellyfin-plugin-pages` and File Transformation the way
`jellyfin-plugin-custom-tabs` does. The reason to consider moving was size: the
DLL is fifteen megabytes and the pages were assumed to be why.

They are not. Measured:

| | |
|---|---|
| The pages this plugin wrote | 168 KB |
| Monaco, vendored for the Yaml tab | 11 MB |
| Its three web workers | 3.8 MB |

So the mechanism costs 168 KB and the choice of editor costs 14.8 MB. Moving to
File Transformation would move the 168 KB and leave the rest exactly where it is,
which answers the question: **the pages stay embedded.**

What that keeps is worth saying. `IHasWebPages` is Jellyfin's own interface,
supported on both lines this plugin targets, and it needs nothing installed
beside it. File Transformation is a third party plugin an administrator would
have to install first, and reaching it means reflection across
`AssemblyLoadContext` boundaries, since every plugin loads into its own. That is
a real dependency and a real fragility to take on, and the thing it was supposed
to buy is not there.

**The size is a separate question, and it is Monaco.** A code editor with a
language server, three web workers and completion, shipped so an administrator
can edit the one part of the configuration the form does not draw: the home
sections. Once P3.2 gives those an editor of their own, the Yaml tab is a
fallback, and fifteen megabytes for a fallback is the wrong shape. Worth
revisiting then rather than now, and worth measuring against CodeMirror, which
does the same job for about 200 KB.

## 2026-09-11, later

### The repository catches up with its siblings

The app and seerr both carry a set of workflows this repository never had, and the
plugin had two: a build and a pull request title lint. Read tab by tab against
`streamyfin/streamyfin`, `streamyfin/seerr` and `intro-skipper/intro-skipper`, and what
applies here was taken:

- **`security.yml`**: CodeQL over the C# and over the workflows themselves, and a Trivy
  filesystem scan for a vulnerable dependency, a leaked secret or a misconfiguration.
  Both on what lands and weekly, never on a pull request, since uploading to code
  scanning needs a write token a fork never gets.
- **A dependency review on every pull request**, which is the half a scheduled scan
  cannot do and the only one that runs on a fork, since it needs no write.
- **`--frozen-lockfile` as the lockfile check**, now that `bun.lock` exists: it fails
  when `package.json` asks for something the lockfile does not carry.
- **A comment pointing at the build.** Two DLLs, one per Jellyfin line, are already
  uploaded on every pull request; nothing said so, so nobody used them. It now says
  where the file goes on a server and that the replaced one is the way back.
- **`housekeeping.yml`**: a label on a pull request that stopped merging, which a stack
  of pull requests onto `develop` produces every time one of them lands, and a stale
  sweep that only touches threads waiting on their author. An issue nobody has answered
  is waiting on us, and closing it for our own silence is how a tracker stops being
  read: #81 sat nine months for that reason.
- **Issue templates and a pull request template**, which this repository had none of,
  and a Renovate configuration, which it also had none of. Renovate leaves the Jellyfin
  and EF Core packages alone: those pin the oldest server each target supports and the
  runtime the host provides, which are decisions rather than versions to keep current.

What was deliberately not taken: the app's Crowdin sync, its Expo build matrix, seerr's
Helm and Cypress jobs, and intro-skipper's SPDX header pass. The duplicate issue
detector and the notification workflow are worth a second look once the release is out.

**On sharing these across the organisation**: the `.github` repository gives every
repository its community health files (issue templates, contributing, security policy,
a pull request template) automatically, and workflow templates that are offered when
someone creates a workflow. It does not run a workflow on other repositories. The rule
that does, required workflows in an organisation ruleset, is a GitHub Enterprise
feature, and this organisation is on the free plan. What works everywhere is a reusable
workflow called by a few lines in each repository, which is the shape to move these into
once they have proven themselves here.

### The release takes the number it is given

`scripts/next-version.js` reads the commits since the last tag, which answers what a
change deserves: a `feat:` is a minor bump, so this release computed 0.69.0.0. A release
is sometimes a decision instead, and no commit subject says 0.70 without also claiming a
breaking change. The Create release workflow now takes an optional version, and the
script uses it when it is given one and computes as before when it is not. A value that
is not a version stops the release rather than tagging something nobody meant.

`bun test` now looks at the whole test project rather than only its pages, since the
release scripts are JavaScript this repository owns too.

### The Targeting tab moves onto the renderer, and json-editor goes

The screen P3.3 built kept its shape and changed what draws a level's overrides. The
renderer now has two modes: the Application tab asks what this server defaults to and
lists every setting, a level asks what it changes and lists only that, as one list,
with Suggested and Locked and a drop button where the other has Free. Adding an
override is a select and a button, which is what json-editor's property picker never
managed: it rendered nothing and saved nothing, so an override could be read and
edited and never created.

Each override says what it falls through to, in words rather than a value: *everyone
gets 15*, *everyone gets Max*, *everyone gets the app's default*. For a group that is
the server; for one user it is their groups too, applied in ascending priority, which
is the order the server resolves in.

**json-editor is gone**, and with it `legacy-settings-form.js`, the four schema
reshapings that existed only for it, and the five tests that pinned those workarounds.
The served schema is the generated one again, plus `x-secret` and `x-category`, which
describe the configuration rather than a form. The assembly is 516 KB smaller. The
Application page's stylesheet moved to `Pages/settings-form.css`, which both tabs load.

### Two defects the beta pass found

**A level that set the playback quality to Max lost everything it carried.** Max is a
null; Jellyfin's JSON options omit a null when writing, so the stored document lost the
`value` key, and reading it back tripped the required `value` on `Lockable<T>`. The
tolerant read answered null and the level came back empty: the group looked saved until
the page was reopened, and its members got nothing at all. Proven on the beta, fixed by
letting an absent value mean null on the plugin's own store, which is what omitting it
meant.

**The same null was refused by the form.** That choice arrives with no `value` key, so
compared strictly against null it looked absent and Max was held invalid with *Choose a
value*. That one had reached the Application tab in #145. The fixtures had spelled the
option as `{ value: null }`, a shape the server never sends; they carry the wire shape
now, and three tests failed the moment they did.

The scenario `admin-ui-targeting.md` had been carrying since P3.3 ran and passed: a
group created from the screen with one member and a locked override reaches exactly
that member through `config/resolved`, an outsider gets their own group's value, a user
override wins over their group, and both reopen showing what was stored. The casing gap
that document flagged is not real.

## 2026-09-11

### P4.2 on a real phone, and what the pass found

#143 merged on the 10th. The receipt half of P4.2 needs a push token a real
installation registered, so the pass ran with an iPhone 15 Pro Max on the TestFlight
build, signed in to the beta as a test account with one device:

- The app registered its token. One notification targeted at that account reached the
  phone; fifteen minutes later the task collected its receipt, `ok`, and forgot the
  ticket, token kept.
- After the app was uninstalled, two more sends both came back `ok` from Expo,
  asked directly, so the token stayed. That is APNs, which on iOS takes hours or
  days to report an uninstalled app, and the plugin did what the receipts said. The
  `DeviceNotRegistered` receipt stays covered by the unit tests on `ExpoTickets`,
  and the send time prune was proven against the real Expo with a token it never
  issued: `DeviceNotRegistered` in the ticket, row removed, nothing else targeted.
- Reinstalled, the app registered under a new device id; signing out removed it
  through `DELETE device/{id}`. The old token stays until APNs speaks, which is the
  real world case, and the hourly task will log it when it does.

Two things the pass found, neither in P4.2 itself:

- **The app posts its token twice on sign in**, within a second, and the second post
  failed on the device id with a 500 while the first was still saving. #149 makes the
  registration one upsert statement, and streamyfin/streamyfin#2068 posts once.
- **Any signed in account could post a notification to every device.** The route
  carried a plain `Authorize`; the app never calls it. #148 takes an administrator,
  and an API key still passes, so integrations keep working.

### Jellyfin 12.0.0, and a report that did not reproduce

Jellyfin 12.0.0 shipped on the 8th; #147 compiles `jf12` against it and makes it the
default target, as `Directory.Build.props` promised for that day. It needs EF Core
10.0.11 and, through it, Newtonsoft.Json 13.0.4.

#146 says 0.68.1.0 on 12.0 breaks Home Screen Sections through a bundled Harmony.
The published zip carries no Harmony at all, checked byte by byte, and on the beta,
Jellyfin 12.0.0 with File Transformation 3.0.0.0 and Home Screen Sections 3.0.0.0 for
12, that plugin starts, injects its script and completes its startup task with our
net10 build present, with the published 0.68.1.0 present, and with Streamyfin absent.
Whatever fails on that server is not what the report names; asked for the plugin list
and the exact build.

### A probe on the store

`value: null` on an integer setting is accepted by `config/yaml` and stored as `0`,
silently. The form now never writes one, which is what the second review of #145
caught, and a server side validator that refuses it, along with values outside their
`[Bounds]`, is owed as its own pull request.

The beta carries the production server's plugins since the 10th, binaries only, so a
pass there sees the same neighbours a real server has.

## 2026-09-10

### P3.6, the audit before the merge

The renderer had sat in #145 for a week with its CI red and a second pass of polish
uncommitted. Read in full against the rest of the plugin before asking for the merge.
What it found is recorded in
[admin-ui-renderer.md](admin-ui-renderer.md#what-the-audit-found); the short version:

**The `[Range]` attributes broke the targeting routes.** ASP.NET validates
DataAnnotations on every body it binds, and a `[Range]` on a `Lockable<int>` is asked
about the `Lockable`, not its value. Every group or user override carrying a skip time
or the subtitle size was refused with a 400 that named the bounds as the reason, for
any value. Proven on the beta before the fix and after it. The bounds are now a
`[Bounds]` attribute of the plugin's own, and a test refuses any validation attribute
on a setting.

**Three smaller things on the page**: the search and the filter outlived the form
across a tab switch, a page drawn without the configuration could post one without
its other sections, and clearing a search came back to the first category.

**The CI was red on packaging, not on tests.** The `package.json` that gives the page
tests a runner declares `"type": "module"`, which turned `scripts/*.js` into ES modules,
and `make update-manifest` failed on `require`. A `package.json` in `scripts/` pins them
back to CommonJS.

**The second pass of polish** lands with it: each pill says how many of its settings
are set, an *All / Set / Locked* filter looks across every category, a *Keys* switch
shows the YAML keys, the banner can be dismissed for good, Home and appearance is
subdivided like the other large categories, and the page keeps the dashboard's own
materials. 45 tests on the renderer, 209 on the plugin.

**Jellyfin 12.0.0 was released on 2026-09-08** and `jf12` still compiles against
`12.0.0-rc5`. The bump to the release needs EF Core 10.0.11, which is what 12.0 pins,
and that needs Newtonsoft.Json 13.0.4, or the restore fails on NU1605 under the
warning policy. Checked on a throwaway worktree: three lines, 0 errors, tests green
on net10.0. It goes in its own pull request after this one.

## 2026-09-02

### P3.6, the form drawn by the plugin

The Application tab no longer renders through json-editor. The server describes the
form at `GET v1/settings/form`, one entry per setting with its control, bounds,
choices and dependency, and `Pages/settings-form.js` draws it. The reasoning, what
was measured on the way and what is deferred are in
[admin-ui-renderer.md](admin-ui-renderer.md); the audit that decided the shape is
[admin-ui-references.md](admin-ui-references.md).

**Why json-editor went.** Seen on the beta after P3.3: its property picker never
added a setting, so an override could be edited and never created. Its DOM could
only be styled from outside, four passes reshaped the schema for it alone, and a
`locked` box shows two states where the app has three. Free, suggested and locked
are now the three answers every row offers, and a save writes exactly the settings
that are not free.

**Two things the tests found before the beta did.** A dropdown offered `Left` where
the store writes `left`, because the first descriptor used the member name and not
the `EnumMember` value; the round trip test failed on `subtitleAlignX` and the
choices now carry the stored spelling. And the audit's premise that the accent could
follow Jellyfin's own CSS variable was wrong: the 12 web client defines four `--jf-*`
properties, none a colour, and 10.11 none. The page carries Jellyfin's greys and
accent itself and picks light or dark from the background the theme paints.

**Dependencies are declared, four of them, each read in the app.** A dependent
setting is inert only while its toggle is locked off at this level; suggested off
still lets a user turn the toggle on.

**A review of the diff caught four things the beta pass had not reached**: the language
settings written with the cultures API's spelling where the config wants camel case, a
whole number accepting `2.5`, a dependent setting that could be inert and invalid at once
with no way out, and a refused save leaving its edit in the shared config. Each has its
test now; the detail is in the renderer document.

**The repository tests JavaScript for the first time.** 41 tests on the renderer run
under a test DOM with `bun test`, in a `pages` job beside the two Jellyfin targets.
The json-editor form stays as `legacy-settings-form.js` for the Targeting tab, and
goes with the schema reshaping when that tab moves onto the renderer.

**Seen on the beta**, Jellyfin 12, through a real Chrome: the page renders in the
dashboard with all 92 settings, and a setting locked from the page, saved, reloaded,
released and saved again is stored, read back and removed as the states say.

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
