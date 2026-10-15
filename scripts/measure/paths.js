// Paths as the repository writes them: relative to its root, with forward slashes, whatever
// the tool or the system wrote. A path outside the repository is left as it is.

const DRIVE = /^[A-Za-z]:\//;

export const toRepoPath = (path, root) => {
    const slashed = path.replaceAll("\\", "/");
    if (!DRIVE.test(slashed) && !slashed.startsWith("/")) return slashed.replace(/^\.\//, "");

    const base = root.replaceAll("\\", "/").replace(/\/+$/, "");
    // A Windows drive letter can come back in either case.
    const inside = DRIVE.test(slashed)
        ? slashed.toLowerCase().startsWith(`${base.toLowerCase()}/`)
        : slashed.startsWith(`${base}/`);
    return inside ? slashed.slice(base.length + 1) : slashed;
};
