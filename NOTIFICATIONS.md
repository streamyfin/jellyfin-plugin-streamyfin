# Streamyfin Client Notifications

Our plugin can consume any event and forward them to your Streamyfin users

There are currently a few Jellyfin events directly supported by our plugin

Events:
- Item Added (Everyone)
- Session Started (Admin only)
- User Locked Out (Admin + user who was locked out)
- Playback Started (Admin only)

These can be enabled or disabled inside the plugin settings page


## Custom Webhook Notifications
If you want to start using the notification endpoint directly with other services, see our examples below

Custom webhook examples:
- [Jellyfin](#Jellyfin)
- [Seerr](#Seerr)

---

# Endpoint (Authorization Required)

`http(s)://server.instance/Streamyfin/notification`

This endpoint requires two headers:

key: `Content-Type`<br>
value: `application/json`

key: `Authorization`<br>
value: `MediaBrowser Token="{apiKey}"`

**You can generate a Jellyfin API key by going to**  
`Dashboard -> Advanced (bottom left) -> API Keys -> Click (+) to generate a key` 

## Template
```json
[
  {
    "title": "string",    // Notification title (required)
    "subtitle": "string", // Notification subtitle (Visible only to iOS users)
    "body": "string",     // Notification body (required)
    "userId": "string",   // Target Jellyfin user id this notification is for
    "username": "string", // Target Jellyfin username this notification is for
    "isAdmin": false      // Boolean to determine if notification also targets admins.
  }
]
```

## Notifying All Users
To do this, all you have to do is populate the title and body. Other fields are not required.

---

# Examples

## Jellyfin
You can use the [jellyfin-webhook-plugin](https://github.com/jellyfin/jellyfin-plugin-webhook) to create a notification based on any event they offer.

- Visit the Webhooks configuration page
- Click "Add Generic Destination"
- Webhook URL should be the URL example from above
- Selected notification type

If we don't directly support an event, you'll want to create a separate webhook destination for each event so we can avoid filtering on our end.

**We are currently working on supporting as many Jellyfin events as possible so you don't have to worry about configuring them!**

### Examples

- [Item Added](#item-added-notification) 
  - We currently support this on our end with the following enhancements:
    - Reducing spam when multiple episodes are added for a season in a short period of time
    - Deep linking to the item page to start playing the item directly from the notification


### Item added notification
- Select event "Item Added"
- Paste in template below

```json
[
    {
        {{#if_equals ItemType 'Movie'}}
          "title": "{{{Name}}} ({{Year}}) added",
          "body": "Watch movie now"
        {{/if_equals}}
        {{#if_equals ItemType 'Season'}}
          "title": "{{{SeriesName}}} season added",
          "body": "Watch season '{{{Name}}}' now"
        {{/if_equals}}
        {{#if_equals ItemType 'Episode'}}
          "title": "{{{SeriesName}}} S{{SeasonNumber00}}E{{EpisodeNumber00}} added",
          "body": "Watch episode '{{{Name}}}' now"
        {{/if_equals}}
    }
]
```

---

## Seerr

You can go to your Seerr instance's notification settings to forward events

- Go to Settings > Notifications > Webhook
- Check "Enable Agent"
- Enter notification endpoint as "Webhook URL"
- Copy an example below

[Template variable help](https://https://docs.seerr.dev/using-seerr/notifications/webhooks#template-variables)


## Issues Notification 

- Copy the JSON below and paste in as JSON Payload
- Select Notification Types 
  - Issue Reported
  - Issue Commented
  - Issue Resolved
  - Issue Reopened

```json
[
  {
    "title": "{{event}}",
    "body": "{{subject}}: {{message}}",
    "isAdmin": true
  },
  {
    "title": "{{event}} - {{subject}}",
    "body": "{{commentedBy_username}}: {{comment_message}}",
    "isAdmin": true
  }
]
```

