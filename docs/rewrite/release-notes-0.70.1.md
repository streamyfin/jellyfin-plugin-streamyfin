<!-- The lines 0.70.1 carries, gathered as each pull request lands. They become the release text when 0.70.1.0 is published, as release-notes-0.70.0.md did. -->

## Before you update

- **The playback quality's empty value is the app's own default.** Earlier versions labelled it Max. The app with Auto quality reads it as Auto, which measures the connection to the server, and the apps before it as Max. Max is now a choice of its own, which every app reads. At the first start, once, the server's quality locked empty is locked at Max, and a group's or a person's left empty is set at Max, locked or not; the server's own, left unlocked, stays empty. A backup taken before this version is restored the same way.
- **In the YAML editor, an empty quality now means Auto.** Write `value: Max` for no limit: a document written for an earlier version that locks it with no value locks Auto.
- **Going back to 0.70.0.0** keeps Max as no limit: it shows it as Other and sends it on as a number no stream reaches. If you choose Max there, choose it again after updating, as 0.70.0.0 stores it empty.

## Fixed

- **The Latest Movies row of the Home tab's first example, "Watching, then new", fills on Jellyfin 12 and 13.** It lists movies by the date they were added, as Jellyfin answers no movie to Latest there without a library (#307). A server that already loaded the example keeps its own row: make that row an Items row of movies sorted by date added, newest first. Loading the example again also does it, but it replaces every row of the Home tab. The "Recently Added" row of "Libraries first" stays a Latest row, which showed no movie on the test servers' Jellyfin 12 and 13 (#311).
