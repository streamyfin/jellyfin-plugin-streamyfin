// Paths as the repository writes them: relative to its root, with forward slashes, whatever
// the tool or the system wrote. A path outside the repository is left as it is.

const DRIVE = /^[A-Za-z]:\//;

export const slashed = (path) => path.replaceAll("\\", "/");

export const toRepoPath = (raw, root) => {
    const path = slashed(raw);
    if (!DRIVE.test(path) && !path.startsWith("/")) return path.replace(/^\.\//, "");

    const base = slashed(root).replace(/\/+$/, "");
    // A Windows drive letter can come back in either case.
    const inside = DRIVE.test(path)
        ? path.toLowerCase().startsWith(`${base.toLowerCase()}/`)
        : path.startsWith(`${base}/`);
    return inside ? path.slice(base.length + 1) : path;
};
