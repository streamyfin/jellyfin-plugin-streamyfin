const { execFileSync } = require('child_process');

function getLastTag() {
    try {
        return execFileSync('git', ['describe', '--tags', '--abbrev=0'], { encoding: 'utf8' }).trim();
    } catch {
        return null;
    }
}

function getCommitsSince(tag) {
    try {
        const range = tag ? `${tag}..HEAD` : 'HEAD';
        const raw = execFileSync('git', ['log', range, '--format=%B%x00'], { encoding: 'utf8' });
        return raw.split('\x00').map(s => s.trim()).filter(Boolean);
    } catch {
        return [];
    }
}

function determineBump(commits) {
    let bump = 'patch';
    for (const msg of commits) {
        const subject = msg.split('\n')[0];
        const body = msg.split('\n').slice(1).join('\n');

        if (/^(\w+)(\([^)]*\))?!:/.test(subject) || /BREAKING(?: |-)CHANGE:/.test(body)) {
            return 'major';
        }
        if (/^feat(\([^)]*\))?:/.test(subject) && bump !== 'major') {
            bump = 'minor';
        }
    }
    return bump;
}

function parseVersion(tag) {
    const parts = tag.replace(/^v/, '').split('.').map(Number);
    if (parts.some(n => !Number.isFinite(n) || n < 0)) {
        throw new Error(`Invalid version tag: ${tag}`);
    }
    while (parts.length < 4) parts.push(0);
    return parts.slice(0, 4);
}

function applyBump([major, minor, patch], bump) {
    if (bump === 'major') return [major + 1, 0, 0, 0];
    if (bump === 'minor') return [major, minor + 1, 0, 0];
    return [major, minor, patch + 1, 0];
}

const lastTag = getLastTag();
const commits = getCommitsSince(lastTag);
const bump = commits.length > 0 ? determineBump(commits) : 'patch';
const current = lastTag ? parseVersion(lastTag) : [0, 0, 0, 0];

process.stdout.write(applyBump(current, bump).join('.') + '\n');
