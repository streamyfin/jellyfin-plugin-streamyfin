// The list operations a manifest update performs, kept apart from the script that
// performs them. Everything else in validate-and-update-manifest.js is a network call
// or a file write, so this is the part that can be tested, and it is the part where an
// off by one silently drops a published entry.

const { parse, compare } = require('./jellyfin-versions');

// One comparator rather than two. The versions here are plain four segment numbers and
// so are the targetAbi values, which is the case that one already handles; writing a
// second would mean two places to get the same ordering wrong.
const order = (a, b) => {
    const [x, y] = [parse(a), parse(b)];
    if (!x || !y) return 0;
    return compare(x, y);
};

/**
 * Place an entry in a manifest's version list.
 *
 * @param {Array<{version: string, targetAbi: string}>} versions Existing entries.
 * @param {{version: string, targetAbi: string}} entry The entry being published.
 * @param {number|null} keep How many versions to keep, or null to keep every one.
 * @returns {{versions: Array, replaced: number, dropped: Array}}
 */
function place(versions, entry, keep) {
    // One version is one entry per Jellyfin line, so an entry is identified by its
    // version and its targetAbi together. Matching on the version alone was right while
    // each line had a file of its own and would now have the jf12 build delete the jf11
    // build of the same release.
    const kept = versions.filter(
        (v) => !(v.version === entry.version && v.targetAbi === entry.targetAbi));
    const replaced = versions.length - kept.length;

    kept.push(entry);

    // Newest first, and within one version the highest targetAbi first. Both halves
    // matter. The server filters on targetAbi <= its own version and then takes the
    // first of what is left, and Enumerable.OrderByDescending is a stable sort, so on a
    // 12 server the jf12 entry has to be the one written above the jf11 entry it shares
    // a version with.
    //
    // Sorted rather than unshifted: the publish workflow accepts any ref, so a build of
    // an older commit carries a lower version, and putting it at the front would break
    // the order the whole thing rests on.
    kept.sort((a, b) => order(b.version, a.version) || order(b.targetAbi, a.targetAbi));

    // Counted in versions rather than entries, since one version is several entries and
    // a limit of ten entries would mean five releases the day a third line is added.
    let dropped = [];
    if (keep !== null) {
        const newest = [];
        for (const v of kept) {
            if (!newest.includes(v.version)) newest.push(v.version);
        }

        const doomed = new Set(newest.slice(keep));
        dropped = kept.filter((v) => doomed.has(v.version));
    }

    return {
        versions: kept.filter((v) => !dropped.includes(v)),
        replaced,
        dropped,
    };
}

module.exports = { place };
