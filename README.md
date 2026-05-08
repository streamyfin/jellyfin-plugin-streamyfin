<div align="center">

# Streamyfin Companion Plugin

**Centralized configuration management for the Streamyfin mobile application**

Configure and synchronize app settings, customize the user experience, and manage push notifications - all from your Jellyfin server.

[![GitHub Release](https://img.shields.io/github/v/release/streamyfin/jellyfin-plugin-streamyfin)](https://github.com/streamyfin/jellyfin-plugin-streamyfin/releases)

</div>

---

## ✨ Features

### 🔧 **Centralized Configuration Management**
Control and lock app settings for all your users from a single location:
- **Video Settings**: Skip times, default playback bitrate, orientation lock
- **Audio Settings**: Remember audio selections, default language
- **Subtitle Settings**: Playback mode, size scaling, remember selections
- **Swipe Controls**: Configure brightness, volume, and skip gestures
- **Library Management**: Hide specific libraries, customize library appearance

### 🏠 **Custom Home Screen**
Create dynamic, personalized home screens with customizable sections:
- **Continue Watching**: Resumable content at your fingertips
- **Next Up**: TV show episodes ready to watch
- **Latest Media**: Newly added content
- **Custom Sections**: Create any view using Jellyfin's API
- **Collection Integration**: Works seamlessly with the [Collection Import plugin](https://github.com/lostb1t/jellyfin-plugin-collection-import)

### 🔔 **Push Notifications** 
Receive real-time notifications on your mobile device:
- **Item Added**: New movies, episodes, and seasons
- **Session Started**: Track active user sessions (admin only)
- **Playback Started**: Monitor content playback (admin only)
- **User Locked Out**: Security alerts for account issues
- **Custom Webhooks**: Integrate with external services
- **Smart Grouping**: Episode notifications are intelligently grouped to reduce spam

[📖 Read full notification documentation](NOTIFICATIONS.md)

### 🔗 **Third-Party Integrations**
Seamless integration with popular services:
- **[Seerr](https://github.com/seerr-team/seerr)**: Automatic SSO login for request management
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
- **YAML Editor**: Full configuration via YAML
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

### Method 2: Manual Installation

1. Download the latest release from [GitHub Releases](https://github.com/streamyfin/jellyfin-plugin-streamyfin/releases)
2. Extract the `.dll` file to your Jellyfin plugins directory:
   - **Linux**: `/var/lib/jellyfin/plugins/Streamyfin/`
   - **Windows**: `%AppData%\Jellyfin\Server\plugins\Streamyfin\`
   - **Docker**: `/config/plugins/Streamyfin/`
3. **Restart Jellyfin**

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

### Seerr Integration
Enable automatic authentication for your users:
1. Set your Seerr server URL in plugin settings
2. Ensure Seerr is configured for **Jellyfin authentication**
3. Users will be automatically logged in when opening Seerr from the app

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
- **[Streamyfin App](https://github.com/fredrikburmester/streamyfin)** - The mobile application

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
