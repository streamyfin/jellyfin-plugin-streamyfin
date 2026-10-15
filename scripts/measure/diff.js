// The lines a pull request adds or changes: git diff with no context lines, from the commit
// its branch left the base at, and each hunk's new side. A deletion adds no line, and a moved
// file reads as new, every line of it.

import { ROOT } from "./settings.js";

export const diffArgs = (from) => ["diff", "--unified=0", "--no-color", "--no-ext-diff", "--no-renames", from];

const unquoted = (path) => (path.startsWith('"') && path.endsWith('"') ? path.slice(1, -1) : path);

export const parseDiff = (text) => {
    const changed = new Map();
    let file = null;

    for (const raw of text.split(/\r?\n/)) {
        if (raw.startsWith("+++ ")) {
            const target = unquoted(raw.slice(4));
            file = target === "/dev/null" ? null : target.replace(/^b\//, "");
            if (file !== null && !changed.has(file)) changed.set(file, new Set());
        } else if (raw.startsWith("@@ ") && file !== null) {
            const [, start, count = "1"] = /\+(\d+)(?:,(\d+))? @@/.exec(raw);
            for (let at = Number(start); at < Number(start) + Number(count); at += 1) {
                changed.get(file).add(at);
            }
        }
    }

    return changed;
};

const runGit = (args, cwd) => {
    const run = Bun.spawnSync(["git", ...args], { cwd });
    if (run.exitCode !== 0) {
        throw new Error(`git ${args.join(" ")} failed: ${run.stderr.toString().trim()}`);
    }
    return run.stdout.toString();
};

// Against the working tree, so a run on a machine sees what is not committed yet. In CI the
// working tree is the commit.
export const changedLines = (base, { cwd = ROOT, git = runGit } = {}) => {
    const from = git(["merge-base", base, "HEAD"], cwd).trim();
    return parseDiff(git(diffArgs(from), cwd));
};
