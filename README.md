<div align="center">

<img src="https://raw.githubusercontent.com/streamyfin/.github/refs/heads/main/streamyfin-github-banner.png" alt="Streamyfin" width="100%">

# Streamyfin Companion Plugin

**Centralized configuration management for the Streamyfin mobile application**

Configure and synchronize app settings, customize the user experience, and manage push notifications - all from your Jellyfin server.

[![GitHub Release](https://img.shields.io/github/v/release/streamyfin/jellyfin-plugin-streamyfin)](https://github.com/streamyfin/jellyfin-plugin-streamyfin/releases)

</div>

---

## ✨ Features

### 🔧 **Centralized Configuration Management**
Control and lock app settings for all your users from a single location:
- **Video Settings**: Skip times, default playback bitrate, orientation lock, segment skip (intro/credits)
- **Audio Settings**: Remember audio selections, default language
- **Subtitle Settings**: Playback mode, size scaling, remember selections
- **Swipe Controls**: Configure brightness, volume, and skip gestures
- **Library Management**: Hide specific libraries, customize library appearance
- **App-side lock sync**: Locked settings sync directly with the Streamyfin app UI

### 🏠 **Custom Home Screen**
Create dynamic, personalized home screens with customizable sections:
- **Continue Watching**: Resumable content at your fingertips
- **Next Up**: TV show episodes ready to watch
- **Latest Media**: Newly added content
- **Custom Sections**: Create any view using Jellyfin's API, including custom endpoints for sections
- **Collection Integration**: Works seamlessly with the [Collection Import plugin](https://github.com/lostb1t/jellyfin-plugin-collection-import)

### 🔔 **Push Notifications**
Receive real-time notifications on your mobile device:
- **Item Added**: New movies, episodes, and seasons (filterable by library)
- **Session Started**: Track active user sessions (admin only)
- **Playback Started**: Monitor content playback (admin only)
- **User Locked Out**: Security alerts for account issues
- **Custom Webhooks**: Integrate with external services
- **Smart Grouping**: Episode notifications are intelligently grouped to reduce spam

[📖 Read full notification documentation](NOTIFICATIONS.md)

### 🔗 **Third-Party Integrations**
Seamless integration with popular services:
- **[Seerr](https://github.com/seerr-team/seerr)** (formerly Jellyseerr): Automatic SSO login for request management
- **[Marlin](https://github.com/fredrikburmester/marlin-search)**: Enhanced search capabilities
- **[Streamystats](https://github.com/fredrikburmester/streamystats)**: Personalized recommendations and promoted watchlists

### 🎨 **Customizable Library Options**
Tailor the library experience:
- Display types: List or row views
- Card styles: Compact or detailed
- Image styles: Poster or cover art
- Toggle titles and statistics visibility

### 🔒 **User Control & Security**
- Lock settings to prevent user modifications
- Set server-wide defaults
- Hide libraries from specific users
- Control menu link visibility

### ⚙️ **Advanced Configuration**
- **YAML Editor**: Full configuration via YAML, with dynamic autocomplete for parentId/id values
- **Form-Based UI**: User-friendly interface for common settings
- **Default Presets**: Sensible defaults out of the box

---

## 📦 Installation

### Method 1: Via Jellyfin Dashboard (Recommended)

1. Open **Jellyfin Dashboard** → **Plugins** → **Catalog**
2. Click the **⚙️ Settings icon** (next to "Catalog" title)
3. Click **➕ Add** to add a new repository
4. Enter the repository URL:
   ```
   https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/main/manifest.json
   ```
5. Go back to **Catalog** and search for **"Streamyfin"**
6. Click **Install**
7. **Restart Jellyfin** to complete installation

One URL for every supported server. The plugin is built twice, once for Jellyfin
10.11 and once for Jellyfin 12, and both builds are listed in that file; your server
picks the one it can run and never sees the other. Upgrading your server from 10.11
to 12 needs nothing changed here, and the next update is simply the right build.

### Method 2: Manual Installation

Use this only when the catalogue is not an option, and give it the `meta.json` in
step 4 even though the plugin loads without one. Jellyfin does not refuse a folder
that has none: it reads the name and version out of the folder name instead. What it
cannot invent is the plugin's identity, so it uses the MD5 of the folder name as the
id, the server never matches that to the catalogue entry, and **the plugin never
receives an update again**. The file is what keeps it updatable.

1. Download the `.zip` for your Jellyfin version from
   [GitHub Releases](https://github.com/streamyfin/jellyfin-plugin-streamyfin/releases):
   `-jf11` for 10.11, `-jf12` for 12.
2. Create a folder named `Streamyfin_<version>` in your plugins directory:
   - **Linux**: `/var/lib/jellyfin/plugins/Streamyfin_0.70.0.0/`
   - **Windows**: `%AppData%\Jellyfin\Server\plugins\Streamyfin_0.70.0.0\`
   - **Docker**: `/config/plugins/Streamyfin_0.70.0.0/`
3. Extract **everything** from the zip into it, not only
   `Jellyfin.Plugin.Streamyfin.dll`. The other assemblies beside it are required,
   and without them Jellyfin logs "Failed to load assembly" and disables the
   plugin.
4. Add a `meta.json` in the same folder, keeping the `guid` exactly as it is since
   that is the whole point, and using the `targetAbi` of the line you downloaded
   (`10.11.9.0` for `-jf11`, `12.0.0.0` for `-jf12`):

   ```json
   {
     "guid": "1e9e5d38-6e67-4615-8719-e98a5c34f004",
     "name": "Streamyfin",
     "version": "0.70.0.0",
     "targetAbi": "12.0.0.0",
     "status": "Active",
     "autoUpdate": false,
     "assemblies": []
   }
   ```

   That is the `-jf12` file. For `-jf11`, the same line reads
   `"targetAbi": "10.11.9.0"`. Declaring an ABI higher than your server has it
   refuse the plugin, which is what copying this one unchanged onto a 10.11 server
   would do.

5. **Restart Jellyfin**

> **Jellyfin 10.11.9 or later, or Jellyfin 12.** 10.11.9 is the floor for the
> 10.11 line rather than 10.11.0, because `IUserManager.Users` became
> `GetUsers()` inside that patch line. An older server refuses the plugin rather
> than loading it, and keeps running.

### Unstable builds

Work in progress, published so it can be tried on a real server before it is
released. These are builds of the `develop` branch: they are not a release, they
have not been through a release's checks, and one of them can break something the
last release did correctly.

Add this URL the same way as above, beside the one you already have:

```
https://raw.githubusercontent.com/streamyfin/jellyfin-plugin-streamyfin/main/manifest-unstable.json
```

An unstable build is numbered above the release it follows: after 0.68.1.0 they are
0.68.1.1, 0.68.1.2 and so on, counting commits. The next release, 0.70.0.0, is above
all of them, so **removing the unstable repository puts you back on the stable path
by itself**: the next release is offered as an ordinary update. Nothing has to be
uninstalled.

Keep both repositories and you are on the unstable channel, since its builds are the
newer number until the next release goes out. That is the point of it, and it is the
reason to remove the URL once you are done testing.

> Running Jellyfin 13 unstable? The 12 build is what installs there, and the same
> `manifest.json` above serves it: `targetAbi` is the oldest server a build accepts
> rather than the only one. A build of its own will come when Jellyfin 13 needs one.

### Going back to 0.68.1.0

0.70.0.0 moves the configuration out of Jellyfin's plugin XML and the device
registrations out of the old database, and never writes to either file again. The
older version finds both as it left them:

1. Remove the unstable repository, if you added it.
2. **Dashboard** → **Plugins** → **Streamyfin** → **Uninstall**, then restart Jellyfin.
3. **Catalog** → **Streamyfin** → install **0.68.1.0**, then restart again.

Once a newer release is in the repository, the **Update Plugins** task installs it
again at the next start, and the dashboard has no switch to stop that. To stay on
0.68.1.0, set `"autoUpdate": false` in `plugins/Streamyfin_0.68.1.0/meta.json`
before restarting.

What the newer version stored stays in `data/streamyfin.db` and is there again when
you update: groups, per user settings, and the configuration as you left it.
Settings you change while on 0.68.1.0 are not carried back. The first start after the
update names each of them in the server log, so you can set them again in the
dashboard. A device that signs in while 0.68.1.0 is running registers with 0.68.1.0
only, and registers again the next time the app starts after the update.

---

## 🚀 Quick Start

1. After installation, navigate to **Dashboard** → **Plugins** → **Streamyfin**
2. Configure your desired settings using either:
   - **Application Tab**: Form-based settings for video, audio, subtitles, etc.
   - **YAML Editor Tab**: Advanced configuration
   - **Notifications Tab**: Configure push notification settings
3. Lock any settings you want to enforce across all users
4. Save your configuration

---

## 📚 Configuration Examples

### Example: Custom Home Screen
```yaml
home:
  sections:
    - title: "Continue Watching"
      orientation: vertical
      items:
        filters: [IsResumable]
        includeItemTypes: [Episode, Movie]
        limit: 25
    - title: "Trending Movies"
      orientation: horizontal
      items:
        sortBy: [DateCreated]
        sortOrder: [Descending]
        includeItemTypes: [Movie]
        limit: 20
```

### Example: Lock Video Settings
```yaml
forwardSkipTime:
  value: 30
  locked: true
rewindSkipTime:
  value: 15
  locked: true
```

📖 **[View more examples](examples/)**

---

## 🤝 Integration Guides

### Seerr Integration (formerly Jellyseerr)
Enable automatic authentication for your users:
1. Set your Seerr server URL in plugin settings
2. Ensure Seerr is configured for **Jellyfin authentication**
3. Users will be automatically logged in when opening Seerr from the app

Optionally set the **Seerr API Key** (Seerr Settings > General) to let Streamyfin sign users in without a password. This also covers Quick Connect and OIDC logins, which have no password.

**Warning:** every authenticated Jellyfin user can read this key, and it grants full admin access to the Seerr API. Only set it on servers where you trust all users. Requires a Seerr version with the `/user/jellyfin/{id}` route and a Streamyfin version with API-key sign-in.

### Streamystats Integration
Get personalized recommendations:
1. Set your Streamystats server URL
2. Enable movie and/or series recommendations
3. Optionally enable promoted watchlists

### Marlin Search Integration
Enhanced search capabilities:
1. Set Marlin as your default search engine
2. Configure your Marlin server URL
3. Users will use Marlin for all app searches

---

## 🛠️ Development

### Configuration Options
The plugin exposes comprehensive configuration options including:
- Media playback controls
- Subtitle and audio preferences
- UI customization
- Third-party service integration
- Push notification settings

### YAML Configuration
All settings can be managed via YAML for infrastructure-as-code workflows.

**[Browse YAML examples →](examples/)**

---

## 📖 Documentation

- **[Notification Setup Guide](NOTIFICATIONS.md)** - Complete notification configuration
- **[YAML Examples](examples/)** - Sample configurations
- **[Streamyfin App](https://github.com/streamyfin/streamyfin)** - The mobile application

---

## 🐛 Issues & Support

Found a bug or have a feature request?
- **[Open an issue](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues)**
- **[View existing issues](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues)**

---

<div align="center">

**Made with ❤️ for the Jellyfin community**

[Report Bug](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues) · [Request Feature](https://github.com/streamyfin/jellyfin-plugin-streamyfin/issues)

</div>
