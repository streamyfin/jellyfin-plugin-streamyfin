// LCOV, as coverlet and bun write it: per source file, the lines with their hits and the
// branches line by line.

const empty = () => ({ lines: new Map(), branches: new Map() });

export const parseLcov = (text, toPath = (path) => path) => {
    const files = new Map();
    let current = null;

    for (const raw of text.split(/\r?\n/)) {
        const line = raw.trim();
        if (line.startsWith("SF:")) {
            const path = toPath(line.slice(3));
            current = files.get(path) ?? empty();
            files.set(path, current);
        } else if (line.startsWith("DA:") && current) {
            const [number, hits] = line.slice(3).split(",");
            const at = Number(number);
            current.lines.set(at, (current.lines.get(at) ?? 0) + Number(hits));
        } else if (line.startsWith("BRDA:") && current) {
            const [number, , , taken] = line.slice(5).split(",");
            const at = Number(number);
            const before = current.branches.get(at) ?? { covered: 0, total: 0 };
            // A branch never reached is written "-", which is not a number either.
            const hit = Number(taken) > 0;
            current.branches.set(at, { covered: before.covered + (hit ? 1 : 0), total: before.total + 1 });
        } else if (line === "end_of_record") {
            current = null;
        }
    }

    return files;
};

// Several runs as one: a line counts as covered when any run covers it, which is how jf11 and
// jf12 add up, and a line only one target compiled counts for that one. The two targets can
// number a line's branches differently, so a line keeps the most branches either saw.
export const mergeCoverage = (...reports) => {
    const merged = new Map();

    for (const report of reports) {
        for (const [path, file] of report) {
            const into = merged.get(path) ?? empty();
            for (const [at, hits] of file.lines) into.lines.set(at, (into.lines.get(at) ?? 0) + hits);
            for (const [at, seen] of file.branches) {
                const before = into.branches.get(at) ?? { covered: 0, total: 0 };
                into.branches.set(at, {
                    covered: Math.max(before.covered, seen.covered),
                    total: Math.max(before.total, seen.total),
                });
            }
            merged.set(path, into);
        }
    }

    return merged;
};

export const counts = (file) => ({
    lines: {
        covered: [...file.lines.values()].filter((hits) => hits > 0).length,
        total: file.lines.size,
    },
    branches: {
        covered: [...file.branches.values()].reduce((sum, at) => sum + at.covered, 0),
        total: [...file.branches.values()].reduce((sum, at) => sum + at.total, 0),
    },
});
