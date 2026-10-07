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

The **Application** tab draws the 86 settings the app reads, grouped the way the app groups them. Each one is **Free**, **Suggested** or **Locked**: left to the user, pushed once as their starting point, or pinned so they cannot change it. Only what you set travels; everything else stays the app's own default. A legend at the top of each tab says what every state and every box means, **Reset** beside a value puts the app's own default back, and libraries are picked by name rather than typed as ids. The plugin also has its own entry in the dashboard's menu now, drawn like the menu's own icons where the File Transformation plugin is installed.

<p align="center"><img width="800" alt="The Application tab: the legend above settings grouped into cards, one of them locked with Reset beside it" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/application.png"></p>

### Groups, and one user

The **Targeting** tab aims the same settings, and the notifications, at a group of users or at one account. One user beats their groups, a group beats everyone, and between two groups the higher priority wins. An administrator who belongs to a group sees what is aimed at them in the app, not the server-wide values.

<p align="center"><img width="800" alt="The Targeting tab with two groups and one user" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/targeting.png"></p>

### The home screen as a list

The **Home** tab edits the rows of the app's home screen as a list: what fills each row, in which order, and the shape of its cards. Sections are dragged into order, a home screen can start from one of three examples, and a preview beside the list draws the rows the way the app does. Emptying the list gives everyone the app's own home screen back. Two rows are new and built by the plugin: **My media**, the libraries somebody can open, paged properly where Jellyfin's own endpoint is not, and **For you**, recommendations from what somebody watched, for the servers that do not run Streamystats.

<p align="center"><img width="800" alt="The Home tab: the rows in order beside a preview of how the app draws them" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/home.png"></p>

### Notifications that know who they are for

Three admin alerts join the existing events: a **scheduled task that failed**, with the reason it gave, a **plugin installed, updated or removed**, and a **refused sign in**, with the name tried and where it came from. Every event can be turned off, aimed at a group or at one user, and written differently in any language, and each waits between two of the same so a server that keeps failing does not keep notifying. A new item is announced only to the people who can open the library it went into.

<p align="center"><img width="800" alt="The Notifications tab with one card per event" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/notifications.png"></p>

### 29 languages

The notifications are translated on [Crowdin](https://translate.streamyfin.app) into 29 languages, and the admin alerts use the words of Jellyfin's own activity log in each of them. A device that tells the server its language gets its notifications in it; the others get the server's language, as before. The app side of that, and the posters a notification about a new item can carry on Android, come with the next version of the app.

### The whole configuration as YAML

The **Yaml Editor** holds the whole configuration as one file, checked against the plugin's schema as you type, with the library and collection ids suggested where a section needs one, and examples to load.

<p align="center"><img width="800" alt="The Yaml Editor with the configuration" src="https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/develop/assets/screenshots/yaml.png"></p>

### Backup and restore

The **Other** tab downloads one file with the configuration, the groups and what is aimed at one user, settings and notifications alike, and each person's own notification choices, which Jellyfin's own backup does not carry, and restores it after checking it. The file contains the Seerr API key, so keep it the way you keep that key.

### Jellyfin 12

The plugin is built for Jellyfin 10.11 and for Jellyfin 12 from one tree, and one repository URL serves both: your server installs the build it can run. It was loaded on Jellyfin 12.0 and on 12.2. The build for 12 is also the one Jellyfin 13 unstable installs: it compiles against 13's prerelease and runs on it.

## Before you update

- **The configuration moves.** It leaves `Jellyfin.Plugin.Streamyfin.xml` for `data/streamyfin.db`, and the device registrations leave the old `streamyfin_plugin.db`. Neither old file is written to again, so 0.68.1.0 finds both as it left them if you go back.
- **Jellyfin 10.11.9 or later** on the 10.11 line.
- **Seerr.** The three Seerr settings are read under their new names (`seerrServerUrl`, `seerrApiKey`, `autoLoginSeerr`) and still sent under the old ones for the apps in the field. Nothing to change.
- **If you had set the Seerr API key, rotate it.** Earlier versions sent it to every signed-in device; 0.70 sends credentials to administrators only, but the devices that connected before kept their copy.
- **`POST /streamyfin/notification` needs an administrator** now.
- **The three new admin alerts are off on a server that was already running**, since its stored configuration does not mention them. A fresh install has them on.
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

