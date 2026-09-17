# Streamyfin Client Notifications

Our plugin can consume any event and forward them to your Streamyfin users

There are currently a few Jellyfin events directly supported by our plugin

Events:
- Item Added (everyone who can open the library it went into)
- Session Started (admins)
- User Locked Out (admins, and the user who was locked out)
- Playback Started (admins)
- Scheduled task failed (admins), with the reason the task gave
- Plugin changed (admins), when one is installed, updated or uninstalled
- Failed sign in (admins), with the name that was tried and where it came from

These can be enabled or disabled inside the plugin settings page, and each has a wait
between two of the same event so a server that keeps failing does not keep notifying.
Failed sign ins wait five minutes per address by default, since a server anyone can reach
is tried by machines that never stop.

A server that was already running when these three arrived has them off, since its stored
configuration does not mention them. A fresh install has them on.

## Languages

A notification is written in the language of the device it goes to. The app says which one
it is in when it registers, as a BCP 47 tag such as `fr-FR`, and the server writes one
message per language among the devices it is sending to rather than one per device. A
device that says nothing, or something that is not a language tag, is written to in the
server's language, which is what every device got before.

The plugin carries English, French, Dutch and Spanish (Mexico). A device asking for
anything else falls back to English, the same way the server does.


## Custom Webhook Notifications
If you want to start using the notification endpoint directly with other services, see our examples below

Custom webhook examples:
- [Jellyfin](#Jellyfin)
- [Seerr](#seerr)

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

Seerr is the project formerly called Jellyseerr. Two ways to connect it, and they can
be used together.

### Requests, without writing a template

`http(s)://server.instance/Streamyfin/v1/notifications/seerr`

This route takes Seerr's own webhook body, unchanged, and decides who each event is
for. That routing is the reason it exists: an approval is addressed to the person who
asked for the media and goes to nobody else, while what the server operator has to act
on goes to administrators.

| Seerr event | Who receives it |
|---|---|
| Request Pending | Administrators |
| Request Automatically Approved | Administrators |
| Request Processing Failed | Administrators |
| Request Approved | The person who requested it |
| Request Declined | The person who requested it |
| Media Available | The person who requested it |
| Anything with an issue | Nobody, see below |

To set it up:

- Go to Settings > Notifications > Webhook
- Check "Enable Agent"
- Webhook URL: the route above
- Authorization Header: `MediaBrowser Token="{apiKey}"`, the same key as the generic
  endpoint
- Leave the JSON Payload at its default. It is Seerr's own payload that is expected
  here, so editing it will stop this working
- Select the notification types in the table above

A user only receives their own notifications if their Seerr account signs in through
Jellyfin, since the routing matches Seerr's requester against Jellyfin usernames. A
Seerr account that is local to Seerr matches nothing and the notification goes nowhere.

An event this route does not know about, one Seerr adds later, is passed through with
Seerr's own subject and message rather than dropped.

### Issues, and anything else, with a template

Issue events are not handled by the route above. They are a conversation rather than a
request, and the comment body is not something the plugin models, so they stay on the
generic endpoint with a template you write.

- Go to Settings > Notifications > Webhook
- Check "Enable Agent"
- Enter the generic notification endpoint as "Webhook URL"
- Copy the example below

[Template variable help](https://docs.overseerr.dev/using-overseerr/notifications/webhooks#template-variables)


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

