// The build that every pull request runs can also be started by hand, on a branch whose
// build.yml has the trigger. Which jobs run, and which run a new one cancels, is worked out
// here from the file's own conditions, for each event that starts it.

import { describe, expect, test } from "bun:test";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const build = Bun.YAML.parse(readFileSync(resolve(import.meta.dir, "../../.github/workflows/build.yml"), "utf8"));

// The part of GitHub's expression syntax build.yml uses: contexts, strings, ==, !=, !, &&,
// || and always(). Strings compare without case, as GitHub compares them.
const evaluate = (expression, context) => {
    const tokens = expression.match(/'(?:[^']|'')*'|[A-Za-z_][\w.-]*\(\)|[A-Za-z_][\w.-]*|==|!=|&&|\|\||[!()]/g) ?? [];
    let at = 0;
    const primary = () => {
        const token = tokens[at++];
        if (token === "(") {
            const inner = or();
            if (tokens[at++] !== ")") throw new Error(`no closing parenthesis in ${expression}`);
            return inner;
        }
        if (token === "!") return !primary();
        if (token === "always()") return true;
        if (token?.startsWith("'")) return token.slice(1, -1).replaceAll("''", "'");
        if (token === undefined || token.endsWith("()")) throw new Error(`cannot read ${token} in ${expression}`);
        return token.split(".").reduce((object, key) => object?.[key], context) ?? null;
    };
    const comparison = () => {
        let left = primary();
        while (tokens[at] === "==" || tokens[at] === "!=") {
            const operator = tokens[at++];
            const right = primary();
            const same = typeof left === "string" && typeof right === "string"
                ? left.toLowerCase() === right.toLowerCase()
                : left === right;
            left = operator === "==" ? same : !same;
        }
        return left;
    };
    const and = () => {
        let left = comparison();
        while (tokens[at] === "&&") {
            at++;
            const right = comparison();
            left = left && right;
        }
        return left;
    };
    const or = () => {
        let left = and();
        while (tokens[at] === "||") {
            at++;
            const right = and();
            left = left || right;
        }
        return left;
    };
    const result = or();
    if (at !== tokens.length) throw new Error(`cannot read ${expression}`);
    return result;
};

const unwrap = (expression) => expression.trim().replace(/^\$\{\{([\s\S]*)\}\}$/, "$1");
const runs = (context) => Object.entries(build.jobs)
    .filter(([, job]) => job.if === undefined || evaluate(unwrap(String(job.if)), context))
    .map(([id]) => id);
const group = (context) => build.concurrency.group.replace(/\$\{\{([^}]*)\}\}/g, (_, expression) => String(evaluate(expression, context)));

const REPOSITORY = "streamyfin/jellyfin-plugin-streamyfin";
const event = (event_name, ref, payload = {}) => ({ github: { event_name, ref, repository: REPOSITORY, event: payload } });
const pullRequest = (from) => event("pull_request", "refs/pull/7/merge", { pull_request: { head: { repo: { full_name: from } } } });

test("the build can be started by hand", () => {
    expect(Object.keys(build.on)).toContain("workflow_dispatch");
});

// A run started by hand has no pull request, so the jobs that read one stay out of it, and a
// job added without a condition shows up here to be decided on.
describe("the jobs that run", () => {
    test.each([
        ["started by hand", event("workflow_dispatch", "refs/heads/fix/something"), ["build", "pages"]],
        ["on a push", event("push", "refs/heads/develop"), ["build", "pages"]],
        ["for a pull request from a branch here", pullRequest(REPOSITORY), ["dependencies", "build", "comment-start", "comment", "pages"]],
        ["for a pull request from a fork", pullRequest("someone/jellyfin-plugin-streamyfin"), ["dependencies", "build", "pages"]],
    ])("%s", (_, context, jobs) => {
        expect(runs(context)).toEqual(jobs);
    });
});

describe("a new run", () => {
    test("cancels the one before it of the same event on the same ref", () => {
        expect(group(event("push", "refs/heads/develop"))).toBe(group(event("push", "refs/heads/develop")));
    });

    test("started by hand leaves the push's run on that ref alone, and a push leaves it alone", () => {
        expect(group(event("workflow_dispatch", "refs/heads/develop"))).not.toBe(group(event("push", "refs/heads/develop")));
    });
});

describe("the conditions are read as GitHub reads them", () => {
    test.each([
        ["github.event_name == 'PULL_REQUEST'", true],
        ["!(github.event_name == 'pull_request')", false],
        ["always() || github.event_name == 'push'", true],
        ["github.event.pull_request.head.repo.full_name == github.repository", false],
        ["github.event_name != 'push' && github.event_name == 'pull_request'", true],
    ])("%s", (expression, expected) => {
        expect(evaluate(expression, pullRequest("someone/fork"))).toBe(expected);
    });

    test("a condition they cannot read fails rather than passes", () => {
        expect(() => evaluate("contains(github.ref, 'main')", pullRequest(REPOSITORY))).toThrow();
    });
});
