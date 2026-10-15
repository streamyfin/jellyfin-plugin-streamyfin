// The lines a pull request adds or changes: git diff with no context lines, from the commit
// its branch left the base at, and each hunk's new side. A deletion adds no line, and a moved
// file reads as new, every line of it.

import { ROOT } from "./settings.js";

// The prefixes are given, since diff.noprefix and diff.mnemonicPrefix change them otherwise,
// and quotePath is off so that a name past ASCII comes as it is, not in octal escapes.
export const diffArgs = (from) => [
    "-c", "core.quotePath=false",
    "diff", "--unified=0", "--no-color", "--no-ext-diff", "--no-renames", "--src-prefix=a/", "--dst-prefix=b/",
    from,
];

export const parseDiff = (text) => {
    const changed = new Map();
    let file = null;
    // The lines of the current hunk still to come, old side and new: inside a hunk every line
    // is content, one that reads like a header included.
    let left = 0;

    for (const raw of text.split(/\r?\n/)) {
        if (left > 0) {
            if (raw.startsWith("-") || raw.startsWith("+")) left -= 1;
            continue;
        }
        if (raw.startsWith("+++ ")) {
            const target = raw.slice(4);
            if (target.startsWith('"')) {
                throw new Error(`cannot read the path of ${raw}: git quotes a name with a quote, a backslash or a control character`);
            }
            file = target === "/dev/null" ? null : target.slice("b/".length);
            if (file !== null && !changed.has(file)) changed.set(file, new Set());
        } else if (raw.startsWith("@@ ")) {
            const [, removed = "1", start, added = "1"] = /@@ -\d+(?:,(\d+))? \+(\d+)(?:,(\d+))? @@/.exec(raw);
            left = Number(removed) + Number(added);
            for (let at = Number(start); file !== null && at < Number(start) + Number(added); at += 1) {
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
