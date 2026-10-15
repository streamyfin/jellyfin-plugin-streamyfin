# Test settings

`settings.json` holds what the test layers read. Any value can be overridden for one run by
an environment variable: `STREAMYFIN_TEST_` followed by the value's path in capitals, dots as
underscores, so `iterations.unit` is `STREAMYFIN_TEST_ITERATIONS_UNIT`. A number, a boolean or
a string is written as is; a list or an object is written as JSON.

| Setting | What it decides |
| --- | --- |
| `seed` | The seed random tests start from. Empty draws one and prints it. |
| `iterations.unit` | How many cases each property draws (CsCheck, fast-check). |
| `iterations.integration` | How many cases each random integration suite draws. Used from P2. |
| `timeBudgetSeconds` | A time limit per property, on top of the draws. 0 is none. |
| `jellyfin.lines` | The Jellyfin versions the integration tests run against. Used from P2. |
| `faults` | The faults the integration tests inject, with their odds and length. Used from P2. |
| `floors.slack` | How many points a folder may measure above its floor before `bun run floors:raise` is required. |
| `mutation.iterations` | How many cases a property draws while mutants run, so each run stays short. |
| `mutation.cs.concurrency` | How many test sessions Stryker.NET runs at once. |
| `mutation.cs.additionalTimeoutMs` | The time Stryker.NET adds to a test run before it calls a mutant a timeout. |
| `mutation.cs.shards` | The C# mutation cut into jobs. Each shard names the files it mutates, and every source file is in exactly one. |
| `mutation.js.concurrency`, `mutation.js.timeoutMS`, `mutation.js.shards` | The same for StrykerJS. A shard's `tests` are what each of its mutants runs. |

Left out of every measure, and why:

- `obj/**`: code the compiler generates.
- `Migrations/*.Designer.cs` and `Migrations/*ModelSnapshot.cs`: code Entity Framework generates.
- `Jellyfin.Plugin.Streamyfin/Pages/Libraries/**`: libraries the pages ship as they come (Monaco, js-yaml).

The files in `owners.json` are measured by the integration tests rather than by these, each
with its reason.
