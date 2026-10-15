// What a run says, in the job's summary and the pull request's comment, and the floors a
// raise writes.

export const MARKER = "<!-- streamyfin-measure -->";

const shown = (value) => (value === null ? "not measured" : `${value} %`);
const cell = (value, floor) => (floor === null || floor === undefined ? shown(value) : `${shown(value)} (floor ${floor})`);
// A pipe would end the table's cell, and a backtick the code span.
const code = (text) => `\`${String(text).replaceAll("|", "\\|").replaceAll("`", "'")}\``;

export const summarize = (result) => {
    const failures = [
        ...result.problems,
        ...result.folders.flatMap((folder) => folder.problems.map((problem) => `${folder.name}: ${problem}`)),
    ];

    const lines = [
        MARKER,
        "## Unit tests, measured",
        "",
        result.ok ? "**Passed.**" : "**Failed.**",
        "",
        "| Folder | Lines | Branches | Mutation |",
        "| --- | --- | --- | --- |",
        ...result.folders.map((folder) =>
            `| ${folder.name} | ${cell(folder.lines, folder.floor?.lines)} | ${cell(folder.branches, folder.floor?.branches)} | ${cell(folder.mutation, folder.floor?.mutation)} |`),
    ];

    if (failures.length > 0) {
        lines.push("", "### What fails", "", ...failures.map((failure) => `- ${failure}`));
    }

    if (result.escaped.length > 0) {
        lines.push(
            "",
            "### Mutants that escape on lines this pull request changed",
            "",
            "Each one needs a test that kills it, or an entry in `tests/accepted-mutants.json` with the reason.",
            "",
            "| File | Line | Code | Mutation | Status |",
            "| --- | --- | --- | --- | --- |",
            ...result.escaped.map((mutant) =>
                `| ${mutant.file} | ${mutant.line} | ${code(mutant.text)} | ${mutant.mutator}: ${code(mutant.replacement)} | ${mutant.status} |`),
        );
    }

    if (result.stale.length > 0) {
        lines.push(
            "",
            "### Accepted mutants nothing matches any more",
            "",
            "Remove them from `tests/accepted-mutants.json`.",
            "",
            ...result.stale.map((entry) => `- ${entry.file}: ${code(entry.line)}, ${entry.mutator} ${code(entry.replacement)}`),
        );
    }

    return `${lines.join("\n")}\n`;
};

// The floors a raise writes: each lifted to what was measured, never lowered, a folder
// measured for the first time added and one no longer measured dropped. Lowering a floor is
// an edit somebody makes by hand, where a review sees it.
export const raisedFloors = (floors, measured) => Object.fromEntries(measured.map((folder) => {
    const before = floors[folder.name] ?? {};
    const lifted = (metric) => {
        const value = folder[metric];
        return value === null ? null : Math.max(value, before[metric] ?? value);
    };
    return [folder.name, { lines: lifted("lines"), branches: lifted("branches"), mutation: lifted("mutation") }];
}));
