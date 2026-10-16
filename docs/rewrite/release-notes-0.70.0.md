<!-- The text for the 0.70.0 preview's release, to paste as it is: the images are absolute, from develop. -->

Welcome to the **0.70.0 preview** of the Streamyfin plugin!

This is the rewrite. The plugin grows from a YAML box with a handful of options into a dashboard that sets every setting of the app, for everyone, for a group or for one user, and it learns to send the notifications people actually want, in their own language. It is published on the unstable channel so it can be tried on real servers before 0.70.0.0 ships.

It carries an unstable number, above the 0.68.1.0 it follows and below the 0.70.0.0 it leads to. When 0.70.0.0 comes out, it is offered as an ordinary update, and removing the unstable repository puts you back on the stable path by itself.

> [!IMPORTANT]
> A preview: it runs on Jellyfin 10.11.9 and later, and on Jellyfin 12 (tried on 12.0 and 12.2). Read [Before you update](#before-you-update) first. 0.70 moves the configuration out of Jellyfin's plugin XML, and [the way back](https://github.com/streamyfin/jellyfin-plugin-streamyfin#going-back-to-068100) is documented.

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

The **Application** tab draws the 90 settings the app reads, grouped the way the app groups them, and three for the app's coming next episodes downloads, which say that no released app reads them yet. Each one is **Free**, **Suggested** or **Locked**: left to the user, pushed once as their starting point, or pinned so they cannot change it. Only what you set travels; everything else stays the app's own default. A legend at the top of each tab says what every state and every box means, **Reset** beside a value puts the app's own default back, and libraries are picked by name rather than typed as ids. The plugin also has its own entry in the dashboard's menu now, drawn like the menu's own icons where the File Transformation plugin is installed.

<p align="center"><img width="800" alt="The Application tab: the legend above settings grouped into cards, one of them locked with Reset beside it" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/application.png"></p>

### Groups, and one user

The **Targeting** tab aims the same settings, and the notifications, at a group of users or at one account. One user beats their groups, a group beats everyone, and between two groups the higher priority wins. The notifications aimed this way reach administrators as they reach anyone. The settings do not reach an administrator's own app yet. The app reads `/Streamyfin/config`, which gives an administrator the server-wide configuration untouched, as the dashboard needs it to edit; an administrator's app gets what is aimed at them once it reads the settings resolved for each account (streamyfin/streamyfin#2004).

<p align="center"><img width="800" alt="The Targeting tab with two groups and one user" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/targeting.png"></p>

### The home screen as a list

The **Home** tab edits the rows of the app's home screen as a list: what fills each row, in which order, and the shape of its cards. Sections are dragged into order, a home screen can start from one of three examples, and a preview beside the list draws the rows the way the app does. Emptying the list gives everyone the app's own home screen back. Two rows are new and built by the plugin: **My media**, the libraries somebody can open, paged properly where Jellyfin's own endpoint is not, and **For you**, recommendations from what somebody watched, for the servers that do not run Streamystats.

<p align="center"><img width="800" alt="The Home tab: the rows in order beside a preview of how the app draws them" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/home.png"></p>

### Notifications that know who they are for

Three admin alerts join the existing events: a **scheduled task that failed**, with the reason it gave, a **plugin installed, updated or removed**, and a **refused sign in**, with the name tried and where it came from. Every event can be turned off, aimed at a group or at one user, and written differently in any language, and each waits between two of the same so a server that keeps failing does not keep notifying. A new item is announced only to the people who can open the library it went into.

Each person also chooses what reaches them: which events, which libraries new items come from, new episodes of the shows they favorited or started, a show they turn off, and a pause from an hour to a week, or until they lift it. The plugin keeps these choices, so they follow the account to every device; the app's screen for them is still to come (streamyfin/streamyfin#2216).

<p align="center"><img width="800" alt="The Notifications tab with one card per event" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/notifications.png"></p>

### 29 languages

The notifications are translated on [Crowdin](https://translate.streamyfin.app) into 29 languages, and the admin alerts use the words of Jellyfin's own activity log in each of them. A device that tells the server its language gets its notifications in it; the others get the server's language, as before. The app side of that, and the posters a notification about a new item can carry on Android, are still to come in the app (streamyfin/streamyfin#2076), as is asking, from the Seerr page of a title that is not on the server yet, to be told when it arrives, whoever adds it (streamyfin/streamyfin#2244).

### The whole configuration as YAML

The **Yaml Editor** holds the whole configuration as one file, checked against the plugin's schema as you type, with the library and collection ids suggested where a section needs one, and examples to load.

<p align="center"><img width="800" alt="The Yaml Editor with the configuration" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/yaml.png"></p>

### Backup and restore

The **Other** tab downloads one file with the configuration, the groups and what is aimed at one user, settings and notifications alike, each person's own notification choices and the titles they wait for, which Jellyfin's own backup does not carry, and restores it after checking it. The file contains the Seerr API key, so keep it the way you keep that key.

### Jellyfin 12

The plugin is built for Jellyfin 10.11 and for Jellyfin 12 from one tree, and one repository URL serves both: your server installs the build it can run. It was loaded on Jellyfin 12.0 and on 12.2. The build for 12 is also the one Jellyfin 13 unstable installs: it compiles against 13's prerelease and runs on it.

The app for Jellyfin 12 is 0.56 (streamyfin/streamyfin#2192), with the sign in, Top Shelf and hidden library fixes for 12. Jellyfin 12 removed the `/emby` and `/mediabrowser` route aliases, and 0.56 moves a server saved under one of them to its root address; it also keeps the views someone hid in the app hidden across the new ids Jellyfin 12 gives some of them. Both need 0.56 to have seen the server before the move: an account saved on a device but not signed in only moves while the old address still answers, and a hidden view is only followed if the app saw its old id. So put 0.56 on the devices, and open it, before moving a server to 12, and until 0.56 is out, wait before moving a server the apps reach under `/emby` or `/mediabrowser`.

## Compatibility

| | Versions |
| --- | --- |
| Jellyfin | 10.11.9 and later on the 10.11 line, and 12 (tried on 12.0 and 12.2). Jellyfin 13 unstable installs the build for 12. |
| Streamyfin app | 0.55.0 and later. 0.55.2 is the one to use once it is out, and 0.56 on Jellyfin 12, as [Jellyfin 12](#jellyfin-12) says. |
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

## How to try it

Add the unstable repository in **Dashboard** → **Plugins** → **Catalog** → ⚙️, beside the one you have:

```
https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/main/manifest-unstable.json
```

Then install or update **Streamyfin** from the catalogue and restart Jellyfin.

## Support

Something wrong? Open an [issue](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues) with your Jellyfin version, the plugin build (`-jf11` or `-jf12`), the app version, and the plugin's lines from the server log. Questions are welcome on [Discord](https://discord.streamyfin.app).

---

