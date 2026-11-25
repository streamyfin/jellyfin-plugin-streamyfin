<!-- The lines 0.70.1 carries, gathered as each pull request lands. They become the release text when 0.70.1.0 is published, as release-notes-0.70.0.md did. -->

## Before you update

- **The playback quality's empty value is the app's own default.** Earlier versions labelled it Max. The app with Auto quality reads it as Auto, which measures the connection to the server, and the apps before it as Max. Max is now a choice of its own, which every app reads. At the first start, once, the server's quality locked empty is locked at Max, and a group's or a person's left empty is set at Max, locked or not; the server's own, left unlocked, stays empty. A backup taken before this version is restored the same way.
- **In the YAML editor, an empty quality now means Auto.** Write `value: Max` for no limit: a document written for an earlier version that locks it with no value locks Auto.
- **Going back to 0.70.0.0** keeps Max as no limit: it shows it as Other and sends it on as a number no stream reaches. If you choose Max there, choose it again after updating, as 0.70.0.0 stores it empty.

## Fixed

- **The Latest Movies row of the Home tab's first example, "Watching, then new", fills on Jellyfin 12 and 13.** It lists movies by the date they were added, as Jellyfin answers no movie to Latest there without a library (#307). A server that already loaded the example keeps its own row: make that row an Items row of movies sorted by date added, newest first. Loading the example again also does it, but it replaces every row of the Home tab.
- **The Home tab's second example, "Libraries first", shows new movies on Jellyfin 12 and 13.** Its Recently Added row, a Latest row of every type, had series and episodes only there. It is now two rows: Recently Added Movies, by the date they were added, and Recently Added Episodes, from Latest (#311). A server that already loaded the example keeps its own row.
- **A value of the wrong type in the YAML editor names its setting.** The refusal said which internal type could not be read, such as `System.Int32`. It now says the setting and what it takes, after the line and column: `Forward skip time (settings.forwardSkipTime.value) takes a whole number, and this is a list.` (#283)
