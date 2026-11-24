// Reads what Jellyfin publishes and what this repository builds against, and prints the
// difference as JSON on stdout. nuget-watch.yml turns that into an issue, once.
//
// Two feeds, because Jellyfin uses two and they carry different things. Releases go to
// nuget.org. The weekly builds of master go to GitHub Packages, versioned
// <major>.0.0-<timestamp>, and that is where a new line appears first: master was bumped
// to 13.0.0 and 13.0.0-20260914101923 was published on 2026-09-14, months before
// anything 13 will reach nuget.org. Reading only nuget.org, as this did at first, means
// learning about a line on the day it ships rather than while there is time to prepare.

const fs = require('fs');
const { classify, builtVersionsFrom } = require('./jellyfin-versions');

const RELEASES = 'https://api.nuget.org/v3-flatcontainer/jellyfin.controller/index.json';

// GitHub Packages refuses anonymous reads even for a public package, so this one needs a
// token. A workflow has one. Without it the feed is skipped rather than fatal: losing
// sight of the prereleases is worth saying loudly, and is not worth a red job every
// morning when the releases half still answers.
const PRERELEASES = 'https://nuget.pkg.github.com/jellyfin/download/jellyfin.controller/index.json';

async function versionsFrom(url, token) {
    const headers = token
        ? { Authorization: `Basic ${Buffer.from(`x:${token}`).toString('base64')}` }
        : {};

    const response = await fetch(url, { headers });
    if (!response.ok) {
        throw new Error(`${url} answered ${response.status}`);
    }

    const body = await response.json();
    if (!Array.isArray(body.versions)) {
        throw new Error(`${url} answered no version list`);
    }

    return body.versions;
}

async function main() {
    const built = builtVersionsFrom(fs.readFileSync('Directory.Build.props', 'utf8'));

    const released = await versionsFrom(process.env.NUGET_FEED || RELEASES);

    // Which feed a version came from changes what to do about it, so it is carried
    // through rather than merged away. A line that exists only as a weekly prerelease on
    // an authenticated feed is not one to move a build onto.
    let prereleased = [];
    let unreadable = null;

    const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN || '';
    if (token) {
        try {
            prereleased = await versionsFrom(process.env.NUGET_PRERELEASE_FEED || PRERELEASES, token);
        } catch (error) {
            unreadable = error.message;
        }
    } else {
        unreadable = 'no token, so the prerelease feed was not read';
    }

    const result = classify([...released, ...prereleased], built);
    const fromPrerelease = new Set(prereleased);

    process.stdout.write(JSON.stringify({
        ...result,
        built,
        unreadable,
        prereleaseOnly: [...result.newLines, ...result.newerInLine]
            .filter((v) => fromPrerelease.has(v) && !released.includes(v)),
    }) + '\n');
}

main().catch((error) => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
});
