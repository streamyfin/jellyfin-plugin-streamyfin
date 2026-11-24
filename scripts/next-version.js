const { execFileSync } = require('child_process');

// Only consider real version tags (e.g. 1.2.3 or 1.2.3.4); ignore backup/* and
// other non-version tags so they can never pollute the computed version.
const VERSION_TAG_GLOB = '[0-9]*.[0-9]*';
// Strict shape check: optional `v`, then 2 to 4 numeric segments. Rejects
// malformed tags like `1..2` or `1.2.` that `Number()` would coerce to 0.
const VERSION_TAG_RE = /^v?\d+(?:\.\d+){1,3}$/;

const git = (args) => execFileSync('git', args, { encoding: 'utf8' });

function lastVersionTag() {
  try {
    return git(['describe', '--tags', '--abbrev=0', '--match', VERSION_TAG_GLOB]).trim() || null;
  } catch {
    return null;
  }
}

function commitsSince(tag) {
  try {
    const range = tag ? `${tag}..HEAD` : 'HEAD';
    return git(['log', range, '--format=%B%x00'])
      .split('\0').map((s) => s.trim()).filter(Boolean);
  } catch {
    return [];
  }
}

function commitCountSince(tag) {
  return Number(git(['rev-list', '--count', `${tag}..HEAD`]).trim());
}

function determineBump(commits) {
  let bump = 'patch';
  for (const msg of commits) {
    const subject = msg.split('\n', 1)[0];
    if (/^\w+(\([^)]*\))?!:/.test(subject) || /BREAKING[ -]CHANGE:/.test(msg)) return 'major';
    if (/^feat(\([^)]*\))?:/.test(subject)) bump = 'minor';
  }
  return bump;
}

function parseVersion(tag) {
  if (!VERSION_TAG_RE.test(tag)) {
    throw new Error(`Invalid version tag: ${tag}`);
  }
  const parts = tag.replace(/^v/, '').split('.').map(Number);
  while (parts.length < 4) parts.push(0);
  return parts.slice(0, 4);
}

function applyBump([major, minor, patch], bump) {
  if (bump === 'major') return [major + 1, 0, 0, 0];
  if (bump === 'minor') return [major, minor + 1, 0, 0];
  return [major, minor, patch + 1, 0];
}

// The unstable channel numbers a build above the release it follows, never below the
// release it is heading towards. The tempting scheme is the other one: develop is on its
// way to 0.69.0, so call the builds 0.69.0.1 and upwards. It breaks the way back. Jellyfin
// updates a plugin by comparing versions and nothing else, so a tester sitting on 0.69.0.5
// would never be offered the 0.69.0.0 that eventually ships, and removing the unstable
// repository would strand them on a version no manifest carries. Numbering above the last
// release keeps both channels in one increasing order: 0.68.1.0 ships, unstable builds are
// 0.68.1.1 and upwards, and the next release 0.69.0.0 supersedes all of them.
//
// The revision is the commit count since that tag, which is monotonic on a linear develop
// and says something true about the build, rather than a run number that says nothing.
function unstable() {
  const tag = lastVersionTag();
  if (!tag) {
    throw new Error('No release tag to count from. An unstable build is numbered above the last release.');
  }

  const count = commitCountSince(tag);
  if (!Number.isInteger(count) || count < 1) {
    throw new Error(`Nothing to publish: HEAD is ${tag}. An unstable build needs at least one commit past the last release.`);
  }

  const [major, minor, patch] = parseVersion(tag);
  return [major, minor, patch, count];
}

// An explicit version wins over the computed one. The conventional commit rule answers
// "what does this change deserve", which is the right answer for a routine release and
// the wrong one when a release is a decision: the rewrite is a minor bump by the rule
// and a bigger number by intent, and there is no commit subject that says 0.70 without
// also claiming a breaking change.
function chosen() {
  const explicit = (process.env.RELEASE_VERSION || '').trim();
  if (!explicit) return null;
  if (!VERSION_TAG_RE.test(explicit)) {
    throw new Error(`RELEASE_VERSION is not a version: ${explicit}`);
  }
  return parseVersion(explicit);
}

function computed() {
  const tag = lastVersionTag();
  const current = tag ? parseVersion(tag) : [0, 0, 0, 0];
  return applyBump(current, tag ? determineBump(commitsSince(tag)) : 'minor');
}

// The channel decides which of the three rules applies. An explicit version on the
// unstable channel is refused rather than honoured: writing 0.70.0.0 into the unstable
// manifest would collide with the stable release of that number the day it is cut, and
// the two manifests would then disagree about what 0.70.0.0 is.
//
// One write and one exit path, and no process.exit: on a pipe, which is how the release
// workflow reads this, exiting can cut a write that has not flushed.
const channel = (process.env.CHANNEL || 'stable').trim();
if (channel !== 'stable' && channel !== 'unstable') {
  throw new Error(`CHANNEL is stable or unstable, not: ${channel}`);
}
if (channel === 'unstable' && (process.env.RELEASE_VERSION || '').trim()) {
  throw new Error('RELEASE_VERSION cannot be set on the unstable channel: the revision is counted from the last release tag.');
}

const next = channel === 'unstable' ? unstable() : (chosen() ?? computed());

if (next.some((n) => !Number.isInteger(n) || n < 0)) {
  throw new Error(`Computed invalid version: ${next.join('.')}`);
}
process.stdout.write(next.join('.') + '\n');
