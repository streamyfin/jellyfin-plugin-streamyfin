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

const tag = lastVersionTag();
const current = tag ? parseVersion(tag) : [0, 0, 0, 0];
const next = applyBump(current, tag ? determineBump(commitsSince(tag)) : 'minor');

if (next.some((n) => !Number.isInteger(n) || n < 0)) {
  throw new Error(`Computed invalid version: ${next.join('.')}`);
}
process.stdout.write(next.join('.') + '\n');
