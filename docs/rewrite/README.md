# Plugin rewrite

Working documents for the rewrite tracked in issue #114.

| Document | What it holds |
|---|---|
| [state-of-the-plugin.md](state-of-the-plugin.md) | What exists today, and what is wrong with it. Written from reading the code, not from memory. |
| [issue-triage.md](issue-triage.md) | Every open issue, diagnosed and mapped onto the plan. |
| [plan.md](plan.md) | The seven parts, their sub parts, and the order they land in. |

## How the work is organised

`develop` is the integration branch. Every sub part gets its own branch named
`refonte/pX-N-slug` and its own pull request onto `develop`. Related branches
are chained with `gh stack` so each pull request shows only its own layer.
`main` keeps serving the published plugin until the rewrite is coherent end to
end, then `develop` merges into it in one go.

If a hotfix ever lands on `main` during the rewrite, merge `main` into
`develop` the same day. A long lived branch is only cheap while it stays close
to the trunk.
