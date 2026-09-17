// Comparing what Jellyfin publishes against what this repository builds against.
//
// The first version of this lived in nuget-watch.yml as `cut -d. -f1 | sort -u`, which
// compares major numbers and nothing else. It works for the case it was written for, a
// whole new Jellyfin line appearing, and is blind to every other one: 12.1.0 was
// published on 2026-09-15 and the watch said nothing, because 12 was already known.
// Prereleases are the case that matters most here, since the first 13.0.0-rc is the
// moment a 13 target becomes possible at all.

// NuGet versions are semantic enough for this: numeric parts, then an optional
// prerelease suffix after a hyphen. Build metadata after a plus is not used by Jellyfin
// and is ignored.
// Shape checked before the numbers are read, the same reason next-version.js does it:
// Number('') is 0, so '', '1..2' and '-1.0' all parse as a valid version otherwise, and
// a feed entry nobody can install would be reported as one that outranks the build.
const CORE = /^\d+(?:\.\d+){0,3}$/;

function parse(version) {
    if (typeof version !== 'string') return null;

    const [core, prerelease = ''] = version.split('+')[0].split(/-(.+)/);
    if (!CORE.test(core)) return null;

    const numbers = core.split('.').map(Number);
    while (numbers.length < 4) numbers.push(0);

    return { numbers: numbers.slice(0, 4), prerelease, raw: version };
}

function compare(a, b) {
    for (let i = 0; i < 4; i++) {
        if (a.numbers[i] !== b.numbers[i]) return a.numbers[i] - b.numbers[i];
    }

    // A prerelease is below the release it leads to: 12.0.0-rc7 is not 12.0.0. Two
    // prereleases of the same version are ordered by their suffix, which is enough to
    // keep the list stable without implementing the whole precedence rule.
    if (!a.prerelease && !b.prerelease) return 0;
    if (!a.prerelease) return 1;
    if (!b.prerelease) return -1;
    return a.prerelease < b.prerelease ? -1 : a.prerelease > b.prerelease ? 1 : 0;
}

/**
 * Split what is published into what this repository already covers and what it does not.
 *
 * @param {string[]} published Every version of the package on the feed.
 * @param {string[]} built The JellyfinVersion values Directory.Build.props declares.
 * @returns {{newLines: string[], newerInLine: string[], newest: string}}
 */
function classify(published, built) {
    const builtVersions = built.map(parse);

    // A value that does not parse stops the watch rather than being skipped. Skipping it
    // drops a line out of the known set, and the report would then announce a line this
    // repository builds against as one that needs a new target.
    if (builtVersions.length === 0 || builtVersions.some((v) => v === null)) {
        throw new Error(`Directory.Build.props does not declare readable Jellyfin versions: ${built.join(', ') || 'none found'}`);
    }

    const newest = builtVersions.reduce((a, b) => (compare(a, b) >= 0 ? a : b));

    // The floor of each line, separately. Comparing every published version against the
    // single newest built one is what the first version of this did, and it hid a new
    // release of an older line: with 10.11.9 and 12.0.0 built, a published 10.12.0 is
    // below 12.0.0 and was discarded, although it is news for the line it belongs to.
    const floors = new Map();
    for (const version of builtVersions) {
        const major = version.numbers[0];
        const known = floors.get(major);
        if (!known || compare(version, known) > 0) floors.set(major, version);
    }

    // Deduplicated, because the caller hands in the union of two feeds and a version
    // published to both is one version. Without this it is named twice in the issue.
    const parsed = [...new Set(published)].map(parse).filter(Boolean);

    // A major older than everything built here is a line that was dropped on purpose,
    // not news, so the newest built major is what decides whether an unknown line counts.
    const newLines = parsed
        .filter((v) => !floors.has(v.numbers[0]) && v.numbers[0] > newest.numbers[0])
        .sort(compare);

    // Compared on the minor and not on the patch. The floor of a line is deliberately
    // old, 10.11.9 rather than 10.11.11, so every patch above it is expected and
    // permanent: reporting those means saying the same thing every day about a decision
    // that was already taken. A new minor is a different matter, and 12.1.0 arriving on
    // 2026-09-15 is the case this was rewritten for.
    const newerInLine = parsed
        .filter((v) => {
            const floor = floors.get(v.numbers[0]);
            if (!floor) return false;
            return v.numbers[1] > floor.numbers[1];
        })
        .sort(compare);

    return {
        // A line nobody here builds against. This is the one that needs a new target,
        // and the procedure for it is in Compat/README.md.
        newLines: newLines.map((v) => v.raw),

        // A newer version of a line already built. Usually nothing to do, since the
        // reference is a floor and a newer server satisfies it, but it is the moment to
        // check the host's EF Core pin and whether anything the plugin uses moved.
        newerInLine: newerInLine.map((v) => v.raw),

        newest: newest.raw,
    };
}

/**
 * The sentence an issue about this report is titled with.
 *
 * Short on purpose: the watch looks for the issue it may already have opened by searching
 * for its title, and GitHub refuses a search past 256 characters. Naming every version it
 * found made a title of that size the day a line started publishing weekly builds, and the
 * watch then failed every morning instead of saying anything. The full list belongs in the
 * body, which nothing searches.
 *
 * The newest version named, and how many others came with it, so the same news keeps the
 * same title and later news gets its own issue.
 */
function headline({ newLines = [], newerInLine = [] }) {
    const named = newLines.length > 0 ? newLines : newerInLine;

    if (named.length === 0) return null;

    const newest = named[named.length - 1];
    const others = named.length - 1;

    return others === 0
        ? `Jellyfin.Controller ${newest} is on NuGet`
        : `Jellyfin.Controller ${newest} and ${others} more are on NuGet`;
}

// The property is repeated once per target, so every value is read rather than the first.
function builtVersionsFrom(props) {
    return [...props.matchAll(/<JellyfinVersion>([^<]+)<\/JellyfinVersion>/g)].map((m) => m[1].trim());
}

module.exports = { parse, compare, classify, headline, builtVersionsFrom };
