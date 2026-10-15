// The measure's comment on a pull request. There is one, marked with the run and the attempt
// that wrote it, so an older run, an attempt re-run late or a run cancelled by a newer push,
// never replaces what a newer one said. When the measure could not judge, it says which
// measures failed rather than keep an earlier verdict on the page.

const MARKER = /<!-- streamyfin-measure(?: run=(\d+) attempt=(\d+))? -->/;

const markerOf = (run, attempt) => `<!-- streamyfin-measure run=${run} attempt=${attempt} -->`;

// The run a comment came from: { run, attempt }, the bare marker of the summary counting as
// the oldest, or null for a comment that is not the measure's.
const runOf = (body) => {
    const found = MARKER.exec(body ?? "");
    return found ? { run: Number(found[1] ?? 0), attempt: Number(found[2] ?? 0) } : null;
};

const newer = (one, other) => (one.run - other.run || one.attempt - other.attempt) > 0;

// Create the comment, update the measure's own, or leave one a newer run wrote.
const plan = ({ comments, run, attempt }) => {
    const mine = comments.find((comment) => comment.user?.type === "Bot" && runOf(comment.body));
    if (!mine) return { action: "create" };
    return newer(runOf(mine.body), { run, attempt }) ? { action: "skip" } : { action: "update", id: mine.id };
};

const failures = (needs) => Object.entries(needs)
    .filter(([, need]) => need.result === "failure" || need.result === "cancelled")
    .map(([job, need]) => (need.result === "failure" ? `${job} failed` : `${job} was cancelled`));

const bodyOf = ({ summary, run, attempt, needs }) => (summary
    ? summary.replace(MARKER, markerOf(run, attempt))
    : [
        markerOf(run, attempt),
        "## Unit tests, measured",
        "",
        `**Failed.** This push could not be judged: ${failures(needs).join(", ")}.`,
        "",
    ].join("\n"));

// Called from the workflow's github-script step.
const post = async ({ github, context, fs, needs }) => {
    const { owner, repo } = context.repo;
    const issue_number = context.issue.number;
    const run = context.runId;
    const attempt = Number(process.env.GITHUB_RUN_ATTEMPT ?? 1);
    const path = "artifacts/measure/summary.md";
    const body = bodyOf({ summary: fs.existsSync(path) ? fs.readFileSync(path, "utf8") : null, run, attempt, needs });
    const comments = await github.paginate(github.rest.issues.listComments, { owner, repo, issue_number, per_page: 100 });
    const next = plan({ comments, run, attempt });
    if (next.action === "create") await github.rest.issues.createComment({ owner, repo, issue_number, body });
    if (next.action === "update") await github.rest.issues.updateComment({ owner, repo, comment_id: next.id, body });
};

module.exports = { bodyOf, plan, post, runOf };
