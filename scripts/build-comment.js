// The comment a pull request gets about its own build: what each Jellyfin line did, and
// the file to download when it worked. build.yml runs it twice, once beside the builds and
// once after them, and both renders read the state of the run at the moment they run, so a
// render that starts late is still right. Which of the two ends up on the pull request is
// decided by the marker at the bottom rather than by the order they happen to finish in.

const MARKER = 'streamyfin-plugin-build';

// The comment the sticky action used to leave. It is replaced in place rather than left
// above the new one, so an open pull request ends up with one comment about its build.
const OLD_MARKER = '<!-- Sticky Pull Request Commentbuild-artifacts -->';

const TARGETS = [
    { job: 'jf11', line: 'Jellyfin 10.11.9 and later', artifact: 'Jellyfin.Plugin.Streamyfin-jf11' },
    { job: 'jf12', line: 'Jellyfin 12', artifact: 'Jellyfin.Plugin.Streamyfin-jf12' },
];

const took = (ms) => `${Math.floor(ms / 60000)}m ${Math.floor((ms % 60000) / 1000)}s`;

const weighs = (bytes) => {
    const mb = bytes / (1024 * 1024);
    return mb >= 0.1 ? `${mb.toFixed(1)} MB` : `${Math.round(bytes / 1024)} KB`;
};

const link = (text, url) => `[${text}](${url})`;

// What one line's row says, and whether that row can still change.
function rowOf(target, { owner, repo, runId, jobs, artifacts, reference }) {
    const job = jobs.find((candidate) => candidate.name === target.job);
    const artifact = artifacts.find((candidate) => candidate.name === target.artifact);
    const usually = reference?.[target.job] ? `, usually ${took(reference[target.job])} on develop` : '';
    const nothing = 'Nothing yet';

    if (!job) {
        return { line: target.line, build: 'Pending', download: nothing, done: false };
    }

    if (job.status === 'queued') {
        return { line: target.line, build: `Queued${usually}`, download: nothing, done: false };
    }

    if (job.status !== 'completed') {
        return { line: target.line, build: `${link('Building', job.html_url)}${usually}`, download: nothing, done: false };
    }

    if (job.conclusion === 'success' && !artifact) {
        // upload-artifact publishes as it finishes, so this is the few seconds between the
        // build being done and its file being listed, and the next render clears it.
        return { line: target.line, build: 'Built', download: 'Uploading', done: false };
    }

    if (job.conclusion === 'success') {
        const spent = job.started_at && job.completed_at
            ? ` in ${took(Date.parse(job.completed_at) - Date.parse(job.started_at))}`
            : '';
        const url = `https://github.com/${owner}/${repo}/actions/runs/${runId}/artifacts/${artifact.id}`;

        return {
            line: target.line,
            build: `Built${spent}`,
            download: `${link(target.artifact, url)} (${weighs(artifact.size_in_bytes)})`,
            done: true,
        };
    }

    if (job.conclusion === 'failure') {
        return { line: target.line, build: link('Failed', job.html_url), download: 'Nothing to download', done: true };
    }

    if (job.conclusion === 'cancelled') {
        return { line: target.line, build: link('Cancelled', job.html_url), download: nothing, done: true };
    }

    return { line: target.line, build: link(job.conclusion || 'Finishing', job.html_url), download: nothing, done: !!job.conclusion };
}

const rowsOf = (state) => TARGETS.map((target) => rowOf(target, state));

/**
 * Whether every line has said its last word, artifact included.
 *
 * @param {object} state The run, its jobs and its artifacts.
 * @returns {boolean} True when nothing in the table can change any more.
 */
const finished = (state) => rowsOf(state).every((row) => row.done);

/**
 * The comment body for the state of a run.
 *
 * @param {object} state owner, repo, runId, runAttempt, sha, jobs, artifacts, reference.
 * @returns {string} Markdown, ending with the marker the next render reads.
 */
function commentBody(state) {
    const { owner, repo, runId, runAttempt, sha } = state;
    const rows = rowsOf(state);
    const repoUrl = `https://github.com/${owner}/${repo}`;

    const table = [
        '| Jellyfin | Build | Download |',
        '|---|---|---|',
        ...rows.map((row) => `| ${row.line} | ${row.build} | ${row.download} |`),
    ].join('\n');

    return [
        '### This pull request, built',
        '',
        `Commit ${link(`\`${sha.slice(0, 7)}\``, `${repoUrl}/commit/${sha}`)}, `
            + `on ${link('the run that built it', `${repoUrl}/actions/runs/${runId}`)}.`,
        '',
        table,
        '',
        'To try one: stop the server, extract the download over the plugin\'s folder, the',
        '`Streamyfin_<version>` under `/config/plugins/` in the official Docker image or under',
        '`/var/lib/jellyfin/plugins/` on Debian, and start it again. Copy that folder somewhere',
        'first, since it is the way back. The download carries every file the plugin needs, its',
        'translations included, and no `meta.json`, so the folder keeps the one it has and the',
        'server still knows which plugin it is.',
        '',
        'Downloading one needs a GitHub account, and they are kept for 7 days.',
        '',
        `<!-- ${MARKER} run=${runId} attempt=${runAttempt} final=${finished(state)} -->`,
    ].join('\n');
}

/**
 * Whether this render may take the place of the comment already on the pull request.
 *
 * @param {string} existing The comment body found there, which may carry no marker.
 * @param {object} mine runId, runAttempt and whether this render is the last word.
 * @returns {boolean} True when the existing comment is older than this render.
 */
function replaces(existing, mine) {
    const found = new RegExp(`<!-- ${MARKER} run=(\\d+) attempt=(\\d+) final=(true|false) -->`).exec(existing || '');
    if (!found) {
        return true;
    }

    const theirs = { runId: Number(found[1]), runAttempt: Number(found[2]), final: found[3] === 'true' };

    if (theirs.runId !== mine.runId) {
        return mine.runId > theirs.runId;
    }

    if (theirs.runAttempt !== mine.runAttempt) {
        return mine.runAttempt > theirs.runAttempt;
    }

    // The two renders of one attempt: the one that knows how it ended wins, whichever of
    // them writes second.
    return mine.final || !theirs.final;
}

// How long each line took on the last develop build that worked, so a build in progress
// can say how long it usually is. Best effort: without it the row simply says less.
async function durationsOnDevelop({ github, core, owner, repo }) {
    try {
        const { data } = await github.rest.actions.listWorkflowRuns({
            owner,
            repo,
            workflow_id: 'build.yml',
            branch: 'develop',
            event: 'push',
            status: 'success',
            per_page: 1,
        });

        const last = data.workflow_runs[0];
        if (!last) {
            return {};
        }

        const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun, {
            owner,
            repo,
            run_id: last.id,
            per_page: 100,
        });

        const reference = {};
        for (const target of TARGETS) {
            const job = jobs.find((candidate) =>
                candidate.name === target.job
                && candidate.conclusion === 'success'
                && candidate.started_at
                && candidate.completed_at);

            if (job) {
                reference[target.job] = Date.parse(job.completed_at) - Date.parse(job.started_at);
            }
        }

        return reference;
    } catch (error) {
        core.info(`No reference durations from develop: ${error.message}`);
        return {};
    }
}

/**
 * Writes the comment, or leaves a newer one alone.
 *
 * @param {object} actions The github, context and core of actions/github-script.
 * @returns {Promise<void>} When the pull request has been told.
 */
async function post({ github, context, core }) {
    const { owner, repo } = context.repo;
    const pull = context.payload.pull_request;

    if (!pull) {
        core.info('Not a pull request, so there is nothing to comment on');
        return;
    }

    const runId = context.runId;
    const runAttempt = Number(process.env.GITHUB_RUN_ATTEMPT || '1');

    const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun, {
        owner,
        repo,
        run_id: runId,
        filter: 'latest',
        per_page: 100,
    });

    const artifacts = await github.paginate(github.rest.actions.listWorkflowRunArtifacts, {
        owner,
        repo,
        run_id: runId,
        per_page: 100,
    });

    const state = {
        owner,
        repo,
        runId,
        runAttempt,
        sha: pull.head.sha,
        jobs,
        artifacts,
        reference: await durationsOnDevelop({ github, core, owner, repo }),
    };

    const mine = { runId, runAttempt, final: finished(state) };
    const comments = await github.paginate(github.rest.issues.listComments, {
        owner,
        repo,
        issue_number: pull.number,
        per_page: 100,
    });

    const existing = comments.find((comment) =>
        comment.user?.login === 'github-actions[bot]'
        && (comment.body?.includes(`<!-- ${MARKER} `) || comment.body?.includes(OLD_MARKER)));

    if (existing && !replaces(existing.body, mine)) {
        core.info(`Comment ${existing.id} is newer than this render, leaving it alone`);
        return;
    }

    const body = commentBody(state);

    if (existing) {
        await github.rest.issues.updateComment({ owner, repo, comment_id: existing.id, body });
        core.info(`Updated comment ${existing.id}, final=${mine.final}`);
        return;
    }

    const { data: created } = await github.rest.issues.createComment({
        owner,
        repo,
        issue_number: pull.number,
        body,
    });
    core.info(`Wrote comment ${created.id}, final=${mine.final}`);
}

module.exports = { commentBody, finished, replaces, post, TARGETS };
