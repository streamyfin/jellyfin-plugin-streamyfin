<!-- The text of the 0.70.0.0 release as it was published on 2026-11-24, screenshots of the released build on Jellyfin 12.4 included. -->

Welcome to **0.70.0.0** of the Streamyfin plugin!

This is the rewrite. The plugin grows from a YAML box with a handful of options into a dashboard that sets every setting of the app, for everyone, for a group or for one user, and it learns to send the notifications people actually want, in their own language.

If you run the unstable build, 0.68.1.59, 0.70.0.0 is offered to you as an ordinary update. Remove the unstable repository to stay on the stable path: it goes on publishing what comes next.

> [!IMPORTANT]
> It runs on Jellyfin 10.11.9 and later, and on Jellyfin 12 (tried on 12.0, 12.2 and 12.4). Read [Before you update](#before-you-update) first. 0.70 moves the configuration out of Jellyfin's plugin XML, and [the way back](https://github.com/streamyfin/jellyfin-plugin-streamyfin#going-back-to-068100) is documented.

## Highlights

- [Every setting of the app, from the dashboard](#every-setting-of-the-app-from-the-dashboard)
- [Groups, and one user](#groups-and-one-user)
- [The home screen as a list](#the-home-screen-as-a-list)
- [Notifications that know who they are for](#notifications-that-know-who-they-are-for)
- [29 languages](#29-languages)
- [The whole configuration as YAML](#the-whole-configuration-as-yaml)
- [Backup and restore](#backup-and-restore)
- [Jellyfin 12](#jellyfin-12)

### Every setting of the app, from the dashboard

The **Application** tab draws the 91 settings the app reads, grouped the way the app groups them, and three for the app's coming next episodes downloads, which say that no released app reads them yet. Each one is **Free**, **Suggested** or **Locked**: left to the user, pushed once as their starting point, or pinned so they cannot change it. Only what you set travels; everything else stays the app's own default. A legend at the top of each tab says what every state and every box means, **Reset** beside a value puts the app's own default back, and libraries are picked by name rather than typed as ids. The plugin also has its own entry in the dashboard's menu now, drawn like the menu's own icons where the File Transformation plugin is installed.

<p align="center"><img width="800" alt="The Application tab on Playback controls: the legend, then Forward skip time suggested at 30 and Rewind skip time locked at 10" src="https://github.com/user-attachments/assets/5090ee4f-2aae-46fc-873f-b802dd21959b"></p>

### Groups, and one user

The **Targeting** tab aims the same settings, and the notifications, at a group of users or at one account. One user beats their groups, a group beats everyone, and between two groups the higher priority wins. The notifications aimed this way reach administrators as they reach anyone. The settings do not reach an administrator's own app yet. The app reads `/Streamyfin/config`, which gives an administrator the server-wide configuration untouched, as the dashboard needs it to edit; an administrator's app gets what is aimed at them once it reads the settings resolved for each account (streamyfin/streamyfin#2004).

<p align="center"><img width="800" alt="The Targeting tab with the Kids group open, its priority and its member" src="https://github.com/user-attachments/assets/e749aa8a-6dbd-443d-9ba7-32a1e5b4fc1d"></p>

<p align="center"><img width="800" alt="The overrides of the Kids group: Skip intro and Subtitle playback mode, both locked" src="https://github.com/user-attachments/assets/234c69e0-6ee1-40e8-b529-03aae84bafb4"></p>

### The home screen as a list

The **Home** tab edits the rows of the app's home screen as a list: what fills each row, in which order, and the shape of its cards. Sections are dragged into order, a home screen can start from one of three examples, and a preview beside the list draws the rows the way the app does. Emptying the list gives everyone the app's own home screen back. Two rows are new and built by the plugin: **My media**, the libraries somebody can open, paged properly where Jellyfin's own endpoint is not, and **For you**, recommendations from what somebody watched, for the servers that do not run Streamystats.

<p align="center"><img width="800" alt="The Home tab: the rows in order beside a preview of how the app draws them" src="https://github.com/user-attachments/assets/a13484b9-a64e-445f-ab19-7c77482ebf86"></p>

### Notifications that know who they are for

Three admin alerts join the existing events: a **scheduled task that failed**, with the reason it gave, a **plugin installed, updated or removed**, and a **refused sign in**, with the name tried and where it came from. Every event can be turned off, aimed at a group or at one user, and written differently in any language. Five events wait between two of the same, so a server that keeps failing does not keep notifying: a session started, a playback started, a failed task and a changed plugin wait 5 seconds unless you set another wait, and a refused sign in waits 5 minutes. The wait is set for the whole server on the Notifications tab, which offers it only on those five and shows each one's own when it is empty. A new item is announced only to the people who can open the library it went into, and a title Jellyfin has not named yet is announced without the ids Radarr or Sonarr write in its folder name, with its year once: "Nosferatu (1922)" rather than "Nosferatu  [tmdbid-653] (1922)".

Each person also chooses what reaches them: which events, which libraries new items come from, new episodes of the shows they favorited or started, a show they turn off, and a pause from an hour to a week, or until they lift it. The plugin keeps these choices, so they follow the account to every device; the app's screen for them comes with its next release (streamyfin/streamyfin#2216). From the Seerr page of a title that is not on the server yet, a person can also ask to be told when it arrives (streamyfin/streamyfin#2244, in the same app release). They then get one message, that one, and not the new item message too, which everyone else still gets: while somebody waits for a title, a new movie's message waits until Jellyfin knows the movie's TMDB or TVDB id, since the id is what tells who waited for it. It goes as soon as the id comes, ten minutes at most, a wait the Notifications tab sets, and a copy of the title in another library leaves them out too, within the hour.

<p align="center"><img width="800" alt="The Notifications tab: Item added on, for the Movies and Shows libraries" src="https://github.com/user-attachments/assets/9e41b319-872c-4ab8-b774-b8c44eed9445"></p>

### 29 languages

The notifications are translated on [Crowdin](https://translate.streamyfin.app) into 29 languages, and the admin alerts use the words of Jellyfin's own activity log in each of them. A device that tells the server its language gets its notifications in it; the others get the server's language, as before. The app side of that, and the posters a notification about a new item can carry on Android, are still to come in the app (streamyfin/streamyfin#2076), as is asking, from the Seerr page of a title that is not on the server yet, to be told when it arrives, whoever adds it (streamyfin/streamyfin#2244).

### The whole configuration as YAML

The **Yaml Editor** holds the whole configuration as one file, checked against the plugin's schema as you type, with the library and collection ids suggested where a section needs one, and examples to load.

<p align="center"><img width="800" alt="The Yaml Editor holding the home screen's sections" src="https://github.com/user-attachments/assets/a08b353b-cd36-4aaa-a715-015034e24066"></p>

### Backup and restore

The **Other** tab downloads one file with the configuration, the groups and what is aimed at one user, settings and notifications alike, each person's own notification choices and the titles they wait for, which Jellyfin's own backup does not carry, and restores it after checking it. The file contains the Seerr API key, so keep it the way you keep that key.

<p align="center"><img width="600" alt="The Backup card of the Other tab: download a backup, or restore from a file" src="https://github.com/user-attachments/assets/56a983af-9190-4219-97ce-b1f08c82820e"></p>

### Jellyfin 12

The plugin is built for Jellyfin 10.11 and for Jellyfin 12 from one tree, and one repository URL serves both: your server installs the build it can run. It was loaded on Jellyfin 12.0, 12.2 and 12.4. The build for 12 is also the one Jellyfin 13 unstable installs: it compiles against 13's prerelease and runs on it.

<p align="center"><img width="800" alt="Jellyfin 12.4's plugin page with Streamyfin 0.70.0.0 active, installed from the catalogue" src="https://github.com/user-attachments/assets/f5e54a5d-26b7-4012-a581-28cd44948ccf"></p>

The app for Jellyfin 12 is 0.56 (streamyfin/streamyfin#2192), with the sign in, Top Shelf and hidden library fixes for 12. Jellyfin 12 removed the `/emby` and `/mediabrowser` route aliases, and 0.56 moves a server saved under one of them to its root address; it also keeps the views someone hid in the app hidden across the new ids Jellyfin 12 gives some of them. Both need 0.56 to have seen the server before the move: an account saved on a device but not signed in only moves while the old address still answers, and a hidden view is only followed if the app saw its old id. So put 0.56 on the devices, and open it, before moving a server to 12, and until 0.56 is out, wait before moving a server the apps reach under `/emby` or `/mediabrowser`.

## Compatibility

| | Versions |
| --- | --- |
| Jellyfin | 10.11.9 and later on the 10.11 line, and 12 (tried on 12.0, 12.2 and 12.4). Jellyfin 13 unstable installs the build for 12. |
| Streamyfin app | 0.55.0 and later. Use 0.55.2, and 0.56 on Jellyfin 12, as [Jellyfin 12](#jellyfin-12) says. |
| What 0.55.2 changes | Each account signed in on a device asks for its settings with its own session (streamyfin/streamyfin#2197), which the groups and the per-user settings rely on. With 0.55.0 or 0.55.1, the settings of a first sign in arrive at the next refresh, and after an account switch on a TV, the new account keeps the previous one's settings until then, and acts in Seerr as the previous account (streamyfin/streamyfin#2233). |
| Apps before 0.55.0 | Not supported. They no longer get Seerr, and a setting the app renamed in 0.55, such as the hero carousel, does nothing. |

## Before you update

- **The configuration moves.** It leaves `Jellyfin.Plugin.Streamyfin.xml` for `data/streamyfin.db`, and the device registrations leave the old `streamyfin_plugin.db`. Neither old file is written to again, so 0.68.1.0 finds both as it left them if you go back.
- **The versions** of Jellyfin and of the app are in [Compatibility](#compatibility).
- **Seerr reaches the app as one block.** The three Seerr settings are read under their new names (`seerrServerUrl`, `seerrApiKey`, `autoLoginSeerr`) as well as the old ones, and the app receives them as one block, which it reads since 0.55.0. An older app no longer gets Seerr from the plugin: update the apps first.
- **The example text earlier versions stored is taken out.** Up to 0.68.1.0, a server that never set them stored `Enter jellyseerr server url` as the Seerr address, `Enter marlin server url` (`Enter Marlin server URL` from 0.67) as Marlin's and `Enter library id(s)` as the one hidden library, and 0.70 served them as values. It takes them out at its start and names them once in the log; a lock on the hidden libraries stays, on an empty list. A Yaml document that still holds one is refused by the setting's name.
- **If you had set the Seerr API key, rotate it.** Earlier versions sent it to every signed-in device; 0.70 sends credentials to administrators only, but the devices that connected before kept their copy.
- **`POST /streamyfin/notification` needs an administrator** now, and answers 502 when Expo took none of the notifications.
- **The plugin's routes are no longer in Jellyfin's API documentation.** Swagger and `/api-docs/openapi.json` leave them out: with them in, one type name the plugin shares with Jellyfin or with another plugin made the whole document answer 500. The routes answer as before.
- **The three new admin alerts are off on a server that was already running**, since its stored configuration does not mention them. A fresh install has them on.
- **Moving a server to Jellyfin 12.** The server keeps the build for 10.11 until the next release, since Jellyfin only offers a higher version as an update, and that build runs on 12 (#247). Jellyfin 12 gives the Live TV view, and each account's Playlists view, a new id, once; Collections keeps its own. Where Hidden libraries holds one of the old ids, for the server, a group or one account, the view shows again in the app, and a locked value keeps the app from hiding it itself. Those views are not in the list of libraries the tabs offer, which shows an old id as Other: write the new id, which `/UserViews` gives for each account, in the Yaml Editor after the upgrade (#255).
- **Notifications go out in batches of 100 devices.** No test server could try a send to more than 100 real devices.

## How to update

Update **Streamyfin** from **Dashboard** → **Plugins**, or let Jellyfin update it, and restart Jellyfin. The repository you already have serves both builds: a server on Jellyfin 10.11 installs the build for 10.11, and a server on 12 the build for 12.

## Known issues

- The first example of the Home tab, "Watching, then new", has a Latest Movies row that stays empty on Jellyfin 12 and 13, since Jellyfin answers no movie for it without a library (#307).
- When Jellyfin adds the same movie twice within minutes, which a library walk can do right after a download, the new item message goes out twice (#308). 0.68 did the same.

## Support

Something wrong? Open an [issue](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues) with your Jellyfin version, the plugin build (`-jf11` or `-jf12`), the app version, and the plugin's lines from the server log. Questions are welcome on [Discord](https://discord.streamyfin.app).

---

## What's Changed

### 🚀 Features

* feat(settings): add lockable auto subtitles on mute settings by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/109
* feat(settings): describe the settings and mark which ones hold a credential by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/128
* feat(settings): group and per user targeting, and the engine that resolves them by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/129
* feat(dashboard): give the plugin an entry in the left menu, with its logo by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/130
* feat(settings): serve each caller only what they are allowed to see by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/131
* feat(settings): move the server level into the plugin's own database by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/132
* feat(api): put every route under a version prefix, keep the old paths by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/133
* feat(settings): let an admin control every setting the app reads by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/134
* feat(settings): generate the admin settings form from the schema by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/136
* feat(settings): group the admin form into the sections the app uses by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/139
* feat(targeting): give groups and per user overrides a screen by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/142
* feat(notifications): collect Expo receipts and forget the devices that are gone by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/143
* feat(settings): draw the admin form ourselves, three states per setting by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/145
* feat(targeting): give a level the screen, and drop json-editor by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/150
* feat(settings): refuse a value the bounds do not allow by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/151
* feat(home): say what kind a section is, and allow exactly one query by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/157
* feat(integrations): let the server say whether an integration answers by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/160
* feat(config): take a backup, and put one back by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/162
* feat(seerr): take a Seerr webhook and know who it is for by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/166
* feat(release): publish develop where a server can install it by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/165
* feat(admin): put the last three pages on the same design as the first two by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/173
* feat(home): let a section say where it sits by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/175
* feat(notifications): declare the events instead of drawing them twice by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/176
* feat(home): edit the home sections as a list instead of as YAML by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/177
* feat(notifications): tell the administrators when a task fails, a plugin changes or a sign in is refused by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/184
* feat(notifications): write a notification in the language of the device it goes to by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/186
* feat(notifications): carry the poster of what was added, from the address the device reaches the server at by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/187
* feat(home): a "for you" row built from what each user has watched by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/189
* feat(notifications): send each event to who the levels say, with a screen to say it by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/191
* feat(notifications): let an administrator write any sentence differently by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/192
* feat(home): a "my media" row, which Jellyfin's own endpoint cannot page by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/194
* feat(settings): state the search engine rule instead of leaving it to the app by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/197
* feat(settings): serve Seerr as a block as well as three flat keys by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/198
* feat(admin): draw the drawer mark in the row's colour, like the icons beside it by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/219
* feat(admin): edit the app language, the hero filters and the library options as the app does, reset to its default, and check the app's lists every week by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/216
* feat(home): explain the Home tab, start from an example, drag sections and preview them as the app draws them by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/217
* feat(notifications): let each person choose the notifications they get by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/226
* feat(settings)!: serve Seerr to the app as its block only by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/227
* feat(notifications): tell people when a title they wait for arrives by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/233
* feat(settings): declare the four settings of the app 0.56 by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/264
* feat(settings): declare the next episodes download settings by @eliasthecactus in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/259
* feat(settings): follow the app 0.56 on stopping playback and on casting by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/303

### 🐛 Bug fixes

* fix(tests): make the suite pass outside an English Linux box by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/116
* fix(notifications): stop the item added handler throwing on a default config by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/122
* fix(settings): add the missing landscape orientation option by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/123
* fix(settings): let an admin save a default audio or subtitle language by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/137
* fix(notifications): send through a configured client, and stop reading a refusal as a delivery by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/141
* fix(api): take an administrator to post a notification by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/148
* fix(devices): register a device in one statement, so two at once cannot race by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/149
* fix: close the two code scanning alerts on the integration branch by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/156
* fix(notifications): send in the batches Expo takes, and wait out a rate limit by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/158
* fix(settings): answer to the current spelling of the Seerr keys by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/159
* fix(settings): call the orientation what the app calls it by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/167
* fix(yaml): load the editor's workers from the plugin, not from wherever it thinks it is by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/178
* fix(settings): name what a downgrade changed in the old file by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/179
* fix(notifications): let two sends remove the same dead tokens by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/180
* fix(privacy): tell a user only about the libraries they can open by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/181
* fix(notifications): send nothing when a message names what cannot be checked by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/185
* fix(admin): believe the answer to a question, rather than the dialog's return value by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/190
* fix(notifications): read Expo's answer when it comes back compressed by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/196
* fix(notifications): keep what Seerr sends on one line in the log by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/203
* fix(settings): send the subtitle mode under the name the app compares by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/209
* fix(notifications): look up the episode line under its real key by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/211
* fix(admin): proofread the dashboard's texts, title Next Up as Jellyfin does, and stop the Yaml editor flagging valid keys by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/214
* fix(admin): match the app's settings: drop five that did nothing, labels, order and bounds as its pickers, and the switches and legend on every tab by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/215
* fix(api): leave the pause and the muted shows out of a person's update by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/229
* fix(backup): refuse a restore that repeats a group or a user by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/230
* fix(backup): keep what each group and user says about notifications by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/231
* fix(build): test the release in Release, as it ships by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/240
* fix(config): serialize YAML one call at a time by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/236
* fix(notifications): send the sentences an administrator rewrote by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/239
* fix(api): refuse a group name in use and an oversized push token by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/243
* fix(api): leave the plugin out of Jellyfin's API document by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/256
* fix(settings): store Seerr's block as the keys alone by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/261
* fix(release): build under the version the release is cut as by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/235
* fix(release): let a rerun finish a release its run started by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/237
* fix(api): refuse a Yaml that sets nothing and an empty notification by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/257
* fix(pages): keep the configuration when the Yaml tab's save is refused by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/260
* fix(compat): For You and My Media on Jellyfin 12 with the 10.11 build by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/241
* fix(pages): draw the drawer logo on Jellyfin 10.11 as well by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/268
* fix(tests): stop one test's cleanup from closing another test's database by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/269
* fix(notifications): keep the choices an update does not speak for by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/274
* fix(api): log what an administrator sends on one line by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/276
* fix(targeting): keep group names on one line, and a deleted group deleted by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/280
* fix(config): read what earlier versions and the Yaml editor wrote as they meant it by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/281
* fix(notifications): await the send to Expo, and tell no disabled account by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/285
* fix(release): publish the unstable build's own draft on a rerun, and ship no library nothing calls by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/286
* fix(pages): undo only a refused save's edit, and keep what the server stored by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/282
* fix(db): let the build for 10.11 start on Jellyfin 12 again by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/288
* fix(pages): a bigger cross to close the legend, and more room around the logo by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/290
* fix(notifications): offer a wait only where an event reads it, and say how long by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/292
* fix(notifications): one message, not two, when an awaited title arrives by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/294
* fix(notifications): leave a folder's ids out of a new title's message by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/298

### 📚 Documentation

* docs(examples): document the segment skip settings by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/113
* docs(rewrite): add the rewrite dossier by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/119
* docs(rewrite): track the open pull requests, the app side work and a log by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/124
* docs(settings): say which platform each video player setting decides by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/138
* docs(rewrite): fix two things the review of #142 found after it merged by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/144
* docs(rewrite): redact the names in the targeting captures by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/152
* docs(rewrite): log the probe, its simplification, and the backup by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/163
* docs(rewrite): log the channel, the load test, and four wrong claims by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/169
* docs(rewrite): log the repair, its proof, and the Jellyfin 13 pass by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/172
* docs(rewrite): log the ported pages and the pass over the admin UI by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/174
* docs(examples): a row of the user's libraries, and a full example that loads by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/182
* docs(compat): record the Jellyfin 13 check, and how to compile against a prerelease by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/218
* docs: rewrite the README for 0.70, with new screenshots, and draft the release notes by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/220
* docs(release): say what reaches an administrator's app, and what each app gets by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/267
* docs(release): say what each person chooses, and which build a server moved to 12 keeps by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/278
* docs(release): say the notification route answers 502 when Expo took nothing by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/287

### 🌐 Translations

* feat(i18n): translate the notifications on Crowdin by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/212
* ci(i18n): start the weekly Crowdin sync from main by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/210
* chore(i18n): take Crowdin's first sync, and close the locked out region under its name by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/213
* chore(i18n): new translations from Crowdin by @github-actions[bot] in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/234
* chore(i18n): new translations from Crowdin by @github-actions[bot] in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/251
* chore(i18n): new translations from Crowdin by @github-actions[bot] in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/302

### 🧪 Tests

* test(settings): retire a not-declared excuse the day its setting is declared by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/135
* test(home): prove a home screen can be aimed at one group, which P5.4 asked for by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/195
* test(settings): read the Seerr settings under every name the app reads them by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/208
* test: await what the tests wait on, so develop builds without a warning by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/223

### ⚙️ CI and build

* ci(coderabbit): review pull requests targeting develop by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/120
* build: select the Jellyfin target with an MSBuild switch by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/115
* ci: build and test both Jellyfin targets on every pull request by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/117
* build: turn the analyser warnings into a policy and enforce it by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/127
* ci(release): build, package and publish both Jellyfin targets by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/126
* build(jf12): compile against Jellyfin 12.0.0, the release by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/147
* build(release): let the release take the number it is given by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/153
* ci: give the repository the checks its siblings already have by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/154
* ci: watch NuGet for a Jellyfin line the build does not know about by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/155
* ci: put the workflows that only fire from the default branch on it by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/168
* ci(prerelease): let a half finished publication be finished, and narrow what it runs by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/170
* ci(prerelease): carry the workflow hardening back to develop by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/171
* ci(build): say where each build is on the pull request, and upload what a release ships by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/183
* fix(ci): keep the Jellyfin watch's issue title short enough to search for by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/188
* ci(watch): carry the shorter issue title back to main by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/199
* fix(ci): keep one NuGet watch issue up to date, in tables by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/204
* fix(ci): run the NuGet watch with the one issue and the tables from develop by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/205
* ci(parity): start the weekly app parity check from main by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/221
* ci(build): let the build be started by hand by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/270
* ci(lint): pin the title check's actions by commit on main by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/275

### 🧹 Maintenance

* refactor(db): move persistence to EF Core and import the old database by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/125
* chore: license the plugin under MPL-2.0 by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/140
* refactor(integrations): cut the two blocks that carried the defects by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/161
* perf(admin): keep the page descriptions between tabs and ask for them together by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/222
* chore(rewrite): integration branch for the plugin rewrite by @Gauvino in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/121

## New Contributors
* @github-actions[bot] made their first contribution in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/234
* @eliasthecactus made their first contribution in https://github.com/streamyfin/jellyfin-plugin-streamyfin/pull/259

**Full Changelog**: https://github.com/streamyfin/jellyfin-plugin-streamyfin/compare/0.68.1.0...0.70.0.0
