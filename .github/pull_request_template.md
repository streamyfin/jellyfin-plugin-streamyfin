<!--
The title is a conventional commit: feat(settings): …, fix(api): …, docs(rewrite): …
It becomes the commit message when this is squashed, so write it for someone reading
`git log` in a year.
-->

## What this changes

<!-- What it does, and what it was before. If it fixes something, say what was wrong
     rather than only what is now right. -->

## Why

<!-- Part of #114? Fixes #N? If a decision was taken here rather than elsewhere, this is
     where it is written down. -->

## How it was checked

<!-- Tests are the floor. Say which, and say what a test could not answer: the admin
     pages are JavaScript that only a dashboard exercises, and the plugin only loads on
     a real Jellyfin. A screenshot of a page goes here, with user names and addresses
     redacted. -->

- [ ] `dotnet test` on both targets, or CI says so
- [ ] Seen on a real server, and which one
