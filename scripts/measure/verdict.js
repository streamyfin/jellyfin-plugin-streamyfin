// The verdict a pull request gets: every folder against its floors, every mutant that
// escapes on a line the pull request touched unless it was accepted with a reason, and every
// acceptance nothing matches any more.

import { folderOf, isExcluded } from "./folders.js";
import { counts } from "./lcov.js";
import { escaped, score } from "./mutants.js";

const METRICS = ["lines", "branches", "mutation"];

// Rounded down, so a folder never meets its floor by rounding up.
export const oneDecimal = (value) => Math.floor(value * 10 + 1e-9) / 10;

const percent = (covered, total) => (total === 0 ? null : oneDecimal((100 * covered) / total));

const counted = (path, owners) => folderOf(path) !== null && !isExcluded(path) && !owners.has(path);

export const measureFolders = ({ coverage, mutants, owners }) => {
    const folders = new Map();
    const folder = (name) => {
        if (!folders.has(name)) folders.set(name, { lines: [0, 0], branches: [0, 0], mutants: [] });
        return folders.get(name);
    };

    for (const [path, file] of coverage) {
        if (!counted(path, owners)) continue;
        const { lines, branches } = counts(file);
        const into = folder(folderOf(path));
        into.lines[0] += lines.covered;
        into.lines[1] += lines.total;
        into.branches[0] += branches.covered;
        into.branches[1] += branches.total;
    }

    for (const mutant of mutants) {
        if (counted(mutant.file, owners)) folder(folderOf(mutant.file)).mutants.push(mutant);
    }

    return [...folders.keys()].sort().map((name) => {
        const { lines, branches, mutants: its } = folders.get(name);
        const mutation = score(its).score;
        return {
            name,
            lines: percent(...lines),
            branches: percent(...branches),
            mutation: mutation === null ? null : oneDecimal(mutation),
        };
    });
};

const floorProblems = (folder, floor, slack) => {
    if (!floor) return ["has no floors yet: run bun run floors:raise"];

    return METRICS.flatMap((metric) => {
        const value = folder[metric];
        const min = floor[metric] ?? null;
        if (min === null) {
            return value === null ? [] : [`${metric} is now measured, at ${value} %: run bun run floors:raise`];
        }
        if (value === null) return [`${metric} is no longer measured, under a floor of ${min} %`];
        if (value < min) return [`${metric} ${value} % is under its floor of ${min} %`];
        if (value > min + slack + 1e-9) {
            return [`${metric} ${value} % is more than ${slack} point over its floor of ${min} %: run bun run floors:raise`];
        }
        return [];
    });
};

const keyOf = (file, text, mutator, replacement) => JSON.stringify([file, text, mutator, replacement]);

export const judge = ({ measured, floors, slack, mutants, accepted, owners, changed, lineText, missingReports }) => {
    const folders = measured.map((folder) => ({
        ...folder,
        floor: floors[folder.name] ?? null,
        problems: floorProblems(folder, floors[folder.name], slack),
    }));

    const problems = [
        ...Object.keys(floors)
            .filter((name) => !measured.some((folder) => folder.name === name))
            .map((name) => `${name} has floors but nothing is measured in it: run bun run floors:raise`),
        ...missingReports.map((shard) => `the mutation shard ${shard} left no report`),
    ];

    const acceptances = new Map(accepted.map((entry) => [keyOf(entry.file, entry.line, entry.mutator, entry.replacement), entry]));
    const used = new Set();
    const escapedOnChanged = [];

    for (const mutant of mutants) {
        if (!escaped(mutant) || !counted(mutant.file, owners)) continue;
        const text = lineText(mutant.file, mutant.line);
        const key = keyOf(mutant.file, text, mutant.mutator, mutant.replacement);
        if (acceptances.has(key)) {
            used.add(key);
        } else if (changed.get(mutant.file)?.has(mutant.line)) {
            const { file, line, mutator, replacement, status } = mutant;
            escapedOnChanged.push({ file, line, text, mutator, replacement, status });
        }
    }

    const stale = [...acceptances].filter(([key]) => !used.has(key)).map(([, entry]) => entry);
    const ok = problems.length === 0
        && escapedOnChanged.length === 0
        && stale.length === 0
        && folders.every((folder) => folder.problems.length === 0);

    return { ok, folders, escaped: escapedOnChanged, stale, problems };
};
