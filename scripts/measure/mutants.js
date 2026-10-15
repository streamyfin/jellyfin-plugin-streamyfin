// Mutants from Stryker's report. Stryker.NET and StrykerJS write the same schema: files by
// path, each with its mutants, their line counted from 1, what they changed and their status.

const DETECTED = new Set(["Killed", "Timeout"]);
const UNDETECTED = new Set(["Survived", "NoCoverage"]);

export const readMutants = (report, toPath) =>
    Object.entries(report.files ?? {}).flatMap(([path, file]) =>
        (file.mutants ?? []).map((mutant) => ({
            file: toPath(path),
            line: mutant.location.start.line,
            mutator: mutant.mutatorName,
            replacement: mutant.replacement ?? "",
            status: mutant.status,
        })));

export const escaped = (mutant) => UNDETECTED.has(mutant.status);

// Stryker's score: detected over detected and undetected. A mutant that did not compile,
// broke the run or was ignored counts in neither, and with none to judge there is no score.
export const score = (mutants) => {
    const detected = mutants.filter((mutant) => DETECTED.has(mutant.status)).length;
    const undetected = mutants.filter(escaped).length;
    const judged = detected + undetected;
    return { detected, undetected, score: judged === 0 ? null : (100 * detected) / judged };
};
