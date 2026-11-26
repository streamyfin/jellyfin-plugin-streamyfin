<!-- The text of the 0.70.1.0 release as it was published on 2026-11-26. -->

Welcome to **0.70.1.0** of the Streamyfin plugin!

A small release on top of 0.70.0.0. The dashboard learns the app's new Auto quality, with the four settings that tune it, and the refresh rate matching of Android TV. It also fixes the Home tab's two examples on Jellyfin 12 and 13, the YAML editor's message for a value of the wrong type, and two leftovers of the notifications' wait.

> [!IMPORTANT]
> The playback quality left empty now means the app's own default, which is Auto from the app 0.56. If you set or locked the quality, read [Before you update](#before-you-update) first.

## Highlights

- [Auto quality](#auto-quality)
- [Match the display's refresh rate](#match-the-displays-refresh-rate)
- [Fixes](#fixes)

### Auto quality

The coming app 0.56 adds an **Auto** quality: before a stream, it measures the connection to the server and asks for a stream that fits in it, and during playback it steps down when the stream stalls, then back up. In the **Application** tab, under **Playback controls**, the **Quality** group follows it:

- **Default playback quality** offers Auto first, which is the empty value, then **Max**, now a choice of its own.
- Four settings tune Auto, in the order the app shows them: **Share of the measured speed** (the app's own is 70%), **Measure again after** (60 minutes), **Minimum in the server's network** (140 Mb/s) and **Adapt during playback** (on).

The four ship with no default, so the app keeps its own until you set one. The apps before 0.56 do not read them.

<p align="center"><img width="800" alt="The Quality group of Playback controls: Default playback quality on Auto, Share of the measured speed and Minimum in the server's network left to the app, Measure again after suggested at 30 minutes, and Adapt during playback locked on" src="https://github.com/user-attachments/assets/df2e29cf-1563-42fb-8b02-34e149105fe0"></p>

### Match the display's refresh rate

**Match display refresh rate**, under **Playback controls** → **Video player**, switches the display to the video's frame rate on Android TV, with the native player. The app leaves it off: most screens go black for a moment when the rate changes. Read by the app 0.56 and later, with no default in the plugin either.

<p align="center"><img width="500" alt="The Video player group: the native player switches for Apple TV and Android TV, then Match display refresh rate and Confirm before stopping playback" src="https://github.com/user-attachments/assets/c12a7e55-c45a-4704-b53d-fa00ae259d1c"></p>

### Fixes

- **The Latest Movies row of the Home tab's first example, "Watching, then new", fills on Jellyfin 12 and 13.** It lists movies by the date they were added, as Jellyfin answers no movie to Latest there without a library (#307). A server that already loaded the example keeps its own row: make that row an Items row of movies sorted by date added, newest first. Loading the example again also does it, but it replaces every row of the Home tab.
- **The Home tab's second example, "Libraries first", shows new movies on Jellyfin 12 and 13.** Its Recently Added row, a Latest row of every type, had series and episodes only there. It is now two rows: Recently Added Movies, by the date they were added, and Recently Added Episodes, from Latest (#311). A server that already loaded the example keeps its own row.
- **A value of the wrong type in the YAML editor names its setting.** The refusal said which internal type could not be read, such as `System.Int32`. It now says the setting and what it takes, after the line and column: `Forward skip time (settings.forwardSkipTime.value) takes a whole number, and this is a list.` (#283)
- **Notifications: the wait between two of the same event.** Each event that waits reads the wait its own block sets on the Notifications tab, with no hidden 5 seconds left for one that has no field, and the plugin cleans up the events it remembers at most once a minute, on a clock that setting the time does not move (#293). What is sent does not change.

## Compatibility

| | Versions |
| --- | --- |
| Jellyfin | 10.11.9 and later on the 10.11 line, and 12 (tried on 12.4), as for 0.70.0.0. |
| Streamyfin app | 0.55.0 and later, as for 0.70.0.0. Auto, its four settings and the refresh rate matching need the app 0.56. |

## Before you update

- **The playback quality's empty value is the app's own default.** Earlier versions labelled it Max. The app with Auto quality reads it as Auto, which measures the connection to the server, and the apps before it as Max. Max is now a choice of its own, which every app reads. At the first start, once, the server's quality locked empty is locked at Max, and a group's or a person's left empty is set at Max, locked or not; the server's own, left unlocked, stays empty. A backup taken before this version is restored the same way.
- **In the YAML editor, an empty quality now means Auto.** Write `value: Max` for no limit: a document written for an earlier version that locks it with no value locks Auto.
- **Going back to 0.70.0.0** keeps Max as no limit: it shows it as Other and sends it on as a number no stream reaches. If you choose Max there, choose it again after updating, as 0.70.0.0 stores it empty.

## How to update

Update **Streamyfin** from **Dashboard** → **Plugins**, or let Jellyfin update it, and restart Jellyfin. The repository you already have serves both builds.

## Known issues

- In the app 0.56, a locked **Share of the measured speed**, **Measure again after** or **Minimum in the server's network** still opens its list, and a value picked there changes nothing. The lock holds: the app drops the write. streamyfin/streamyfin#2561 greys them out.
- When Jellyfin adds the same movie twice within minutes, the new item message goes out twice (#308). 0.68 did the same.
- The refused sign in notification is not sent for a name the server does not know (#315).

## Support

Something wrong? Open an [issue](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues) with your Jellyfin version, the plugin build (`-jf11` or `-jf12`), the app version, and the plugin's lines from the server log. Questions are welcome on [Discord](https://discord.streamyfin.app).

---

## What's Changed

<details>
<summary>9 pull requests</summary>

### 🚀 Features

* feat(settings): leave the empty playback quality to the app's Auto, and name Max by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/305
* feat(settings): declare Auto's four rows and the Android TV refresh rate matching by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/319

### 🐛 Bug fixes

* fix(home): list the first example's new movies by date added by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/310
* fix(home): list the second example's new movies by date added, beside its new episodes by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/313
* fix(yaml): a value of the wrong type names its setting and what it takes by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/316
* fix(notifications): an event that waits says how long, and the cleanup runs once a minute by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/314

### 📚 Documentation

* docs(rewrite): the 0.70.1 notes and log for the Home example, and the 0.70.0 text as published now by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/312

### ⚙️ CI and build

* fix(parity): read the app's develop again in the settings generator by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/309
* fix(ci): the release workflows read every page of releases and wait for each other by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/318

</details>

**Full Changelog**: https://github.com/streamyfin/jellyfin-plugin-streamyfin/compare/0.70.0.0...0.70.1.0
