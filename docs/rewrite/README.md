# Plugin rewrite

Working documents for the rewrite tracked in issue #114.

Everything about the chantier lives here. If a decision, a finding or a piece of
work exists only in a pull request thread or a chat, it is not findable, so it
goes in one of these files instead.

| Document | What it holds |
|---|---|
| [log.md](log.md) | What happened and why, newest first. Read this first to catch up. |
| [state-of-the-plugin.md](state-of-the-plugin.md) | What exists today, and what is wrong with it. Written from reading the code, not from memory. |
| [plan.md](plan.md) | The seven parts, their sub parts, and the order they land in. |
| [issue-triage.md](issue-triage.md) | Every open issue, diagnosed and mapped onto the plan. |
| [pull-request-triage.md](pull-request-triage.md) | The pull requests that were open when the rewrite started. |
| [app-side-work.md](app-side-work.md) | What the app repository has to do, and what it is waiting on. |
| [settings-parity.md](settings-parity.md) | Which of the app's settings the plugin declares, and the manifest that keeps the two from drifting. |

## How the work is organised

`develop` is the integration branch. Every sub part gets its own branch named
`refonte/pX-N-slug` and its own pull request onto `develop`. Related branches
are chained with `gh stack` so each pull request shows only its own layer.
`main` keeps serving the published plugin until the rewrite is coherent end to
end, then `develop` merges into it in one go.

Pull request #121 is the draft from `develop` onto `main`. It stays open for the
whole chantier and shows the cumulative diff in one place.

If a hotfix ever lands on `main` during the rewrite, merge `main` into
`develop` the same day. A long lived branch is only cheap while it stays close
to the trunk.

## Keeping this current

When a pull request merges, in the same sitting:

1. Tick its box in issue #114 and update the Progress table there.
2. Add its row to the table in #121.
3. Add an entry to [log.md](log.md). Same rule as the file states: whenever
   something lands or a decision is taken.
4. Move the item in [app-side-work.md](app-side-work.md) from Open to Done if it
   was one.

Fixes that close a user issue also get a comment on that issue saying what
changed for them, in plain terms, before it is closed.
