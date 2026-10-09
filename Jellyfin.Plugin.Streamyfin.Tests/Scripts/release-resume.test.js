// The two publishing steps of release.yml, run for real against a throwaway origin: a
// first attempt that stops part way, then the second attempt GitHub offers as "Re-run
// failed jobs". The tag, the commit on main and the release are public the moment they
// exist, so the second attempt has to finish what the first one started rather than
// stop on it, and nothing else may pass for that attempt. git is real; make and gh are
// stand-ins that record what they are asked.

import { afterAll, describe, expect, test } from "bun:test";
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const steps = Bun.YAML.parse(readFileSync(resolve(".github/workflows/release.yml"), "utf8")).jobs.release.steps;
const scriptOf = (name) => steps.find((step) => step.name === name).run;
const TAG_STEP = "Tag and release";
const MANIFEST_STEP = "Add both builds to the manifest";

const VERSION = "0.70.0.0";
const RUN = "101";

// Git run from a hook exports GIT_DIR, GIT_INDEX_FILE and GIT_WORK_TREE with absolute
// paths, and every git below would then act on the developer's own repository. None of
// them, and no global or system configuration either.
const isolated = Object.fromEntries(Object.entries(process.env).filter(([key]) => !key.startsWith("GIT_")));
const base = { ...isolated, GIT_CONFIG_GLOBAL: "/dev/null", GIT_CONFIG_NOSYSTEM: "1" };

const decode = (bytes) => new TextDecoder().decode(bytes);
const sh = (cwd, args, env = {}) => {
    const result = Bun.spawnSync(args, { cwd, env: { ...base, ...env }, stdout: "pipe", stderr: "pipe" });
    return { code: result.exitCode, output: decode(result.stdout) + decode(result.stderr) };
};
const git = (cwd, ...args) => {
    const result = sh(cwd, ["git", ...args]);
    if (result.code !== 0) throw new Error(`git ${args.join(" ")} failed: ${result.output}`);
    return result.output.trim();
};
const configure = (dir) => {
    git(dir, "config", "user.email", "test@example.com");
    git(dir, "config", "user.name", "test");
    git(dir, "config", "commit.gpgsign", "false");
    git(dir, "config", "tag.gpgsign", "false");
};

// The two make targets the steps run. update-manifest checks the zip against the one the
// release serves, as validate-and-update-manifest.js does by downloading it, reads each
// build's targetAbi from the tree it runs in, as the Makefile does from
// Directory.Build.props, stamps the time, and keeps one entry per version and line, the
// way place() does.
const MAKE = `#!/usr/bin/env bash
set -eu
case "$1" in
  update-version)
    printf '<AssemblyVersion>%s</AssemblyVersion>\\n' "$VERSION" > Jellyfin.Plugin.Streamyfin/Jellyfin.Plugin.Streamyfin.csproj ;;
  update-manifest)
    if [ -f "$STATE/fail-manifest" ]; then echo "Error verifying checksum for URL" >&2; exit 1; fi
    target="\${2#JELLYFIN_TARGET=}"
    zip="streamyfin-$VERSION-$target.zip"
    served="$STATE/release-$VERSION/assets/$zip"
    if [ ! -f "$served" ] || [ "$(shasum "dist/$zip" | cut -d' ' -f1)" != "$(shasum "$served" | cut -d' ' -f1)" ]; then
      echo "Checksum mismatch for URL: $zip" >&2; exit 1
    fi
    node -e '
      const fs = require("fs");
      const [target, targetAbi] = process.argv.slice(1);
      const entry = { version: process.env.VERSION, target, targetAbi, timestamp: new Date().toISOString() };
      const kept = JSON.parse(fs.readFileSync("manifest.json", "utf8"))
        .filter((e) => !(e.version === entry.version && e.target === entry.target));
      fs.writeFileSync("manifest.json", JSON.stringify([...kept, entry]) + "\\n");
    ' "$target" "$(cat "abi/$target")" ;;
  *) echo "unexpected: make $*" >&2; exit 2 ;;
esac
`;

// gh as the workflow uses it. release create makes a draft, uploads, then publishes, and
// deletes its draft when an upload fails; only a gh that is killed leaves the draft
// behind. list includes drafts, view gives each asset's digest, and each can be told to
// fail the way a call to GitHub can.
const GH = `#!/usr/bin/env bash
set -eu
verb="$2"
case "$verb" in
  list)
    if [ -f "$STATE/fail-list" ]; then echo "HTTP 502: Bad Gateway" >&2; exit 1; fi
    printf '['; first=1
    for r in "$STATE"/release-*; do
      [ -d "$r" ] || continue
      [ $first = 1 ] || printf ','; first=0
      draft=false; [ -f "$r/draft" ] && draft=true
      printf '{"tagName":"%s","isDraft":%s}' "\${r##*/release-}" "$draft"
    done
    printf ']\\n'; exit 0 ;;
esac
tag="$3"; shift 3
release="$STATE/release-$tag"
case "$verb" in
  view)
    [ -d "$release" ] || { echo "release not found" >&2; exit 1; }
    draft=false; [ -f "$release/draft" ] && draft=true
    printf '{"isDraft":%s,"assets":[' "$draft"; first=1
    for f in "$release"/assets/*; do
      [ -e "$f" ] || continue
      [ $first = 1 ] || printf ','; first=0
      printf '{"name":"%s","size":%s,"digest":"sha256:%s","state":"uploaded"}' "$(basename "$f")" "$(wc -c < "$f" | tr -d ' ')" "$(shasum -a 256 "$f" | cut -d' ' -f1)"
    done
    printf ']}\\n' ;;
  create)
    if [ -f "$STATE/fail-create" ]; then echo "HTTP 502: release not created" >&2; exit 1; fi
    if [ -d "$release" ]; then mv "$release" "$STATE/other-$tag"; fi
    mkdir -p "$release/assets"; touch "$release/draft"
    for f in "$@"; do
      case "$f" in -*) continue ;; esac
      if [ -n "$(ls "$release/assets")" ]; then
        if [ -f "$STATE/kill-upload" ]; then exit 137; fi
        if [ -f "$STATE/fail-upload" ]; then rm -rf "$release"; echo "upload failed, draft cleaned up" >&2; exit 1; fi
      fi
      cp "$f" "$release/assets/"
    done
    rm -f "$release/draft"; echo "create $tag" >> "$STATE/log" ;;
  upload)
    [ -d "$release" ] || exit 1
    for f in "$@"; do case "$f" in -*) ;; *) cp "$f" "$release/assets/"; echo "upload $(basename "$f")" >> "$STATE/log" ;; esac; done ;;
  edit)
    [ -d "$release" ] || exit 1; rm -f "$release/draft"; echo "publish $tag" >> "$STATE/log" ;;
  *) echo "unexpected: gh release $verb $tag $*" >&2; exit 2 ;;
esac
`;

const sandboxes = [];
afterAll(() => {
    for (const dir of sandboxes) rmSync(dir, { recursive: true, force: true });
});

// An origin whose main carries the commit the release is dispatched on.
const sandbox = () => {
    const root = mkdtempSync(join(tmpdir(), "release-resume-"));
    sandboxes.push(root);
    const box = { root, origin: join(root, "origin.git"), bin: join(root, "bin"), state: join(root, "state"), attempts: 0 };
    for (const dir of [box.bin, box.state, join(root, "tmp")]) mkdirSync(dir);
    writeFileSync(join(box.bin, "make"), MAKE);
    writeFileSync(join(box.bin, "gh"), GH);
    chmodSync(join(box.bin, "make"), 0o755);
    chmodSync(join(box.bin, "gh"), 0o755);

    git(root, "init", "-q", "--bare", "-b", "main", box.origin);
    box.seed = join(root, "seed");
    git(root, "init", "-q", "-b", "main", box.seed);
    configure(box.seed);
    mkdirSync(join(box.seed, "Jellyfin.Plugin.Streamyfin"));
    mkdirSync(join(box.seed, "abi"));
    writeFileSync(join(box.seed, "Jellyfin.Plugin.Streamyfin/Jellyfin.Plugin.Streamyfin.csproj"), "<AssemblyVersion>0.68.1.0</AssemblyVersion>\n");
    writeFileSync(join(box.seed, "abi/jf11"), "10.11.9.0");
    writeFileSync(join(box.seed, "abi/jf12"), "12.0.0.0");
    writeFileSync(join(box.seed, "manifest.json"), "[]\n");
    writeFileSync(join(box.seed, ".gitignore"), "dist/\n");
    git(box.seed, "add", "-A");
    git(box.seed, "commit", "-q", "-m", "chore: what the release is dispatched on");
    git(box.seed, "remote", "add", "origin", box.origin);
    git(box.seed, "push", "-q", "origin", "main");
    box.dispatched = git(box.seed, "rev-parse", "HEAD");
    return box;
};

// A commit that reaches main from somewhere else, during the build or between attempts.
const landOnMain = (box, file, content, subject) => {
    git(box.seed, "pull", "-q", "--ff-only", "origin", "main");
    writeFileSync(join(box.seed, file), content);
    git(box.seed, "add", file);
    git(box.seed, "commit", "-q", "-m", subject);
    git(box.seed, "push", "-q", "origin", "main");
};

// What a runner starts each attempt with. actions/checkout fetches every branch and tag,
// then pins refs/remotes/origin/main to the commit the run was dispatched on and checks
// main out there, so on a rerun origin/main is stale until a step fetches it.
// The zips are the build jobs' artifacts: "Re-run failed jobs" hands the same ones back,
// "Re-run all jobs" builds them again, same size, other bytes.
const attempt = (box, { rebuilt = false } = {}) => {
    const dir = join(box.root, `attempt-${++box.attempts}`);
    git(box.root, "init", "-q", dir);
    configure(dir);
    git(dir, "remote", "add", "origin", box.origin);
    git(dir, "fetch", "-q", "origin", "+refs/heads/*:refs/remotes/origin/*", "+refs/tags/*:refs/tags/*");
    git(dir, "update-ref", "refs/remotes/origin/main", box.dispatched);
    git(dir, "checkout", "-q", "-B", "main", box.dispatched);
    mkdirSync(join(dir, "dist"));
    if (rebuilt) box.builds = (box.builds ?? 1) + 1;
    for (const target of ["jf11", "jf12"]) {
        writeFileSync(join(dir, "dist", `streamyfin-${VERSION}-${target}.zip`), `${target} built by run ${box.run ?? RUN}, build ${box.builds ?? 1}`);
    }
    return dir;
};

// A step the way the runner runs one when the workflow names no shell: bash -e.
const run = (box, dir, step, runId = RUN) =>
    sh(dir, ["bash", "--noprofile", "--norc", "-e", "-c", scriptOf(step)], {
        PATH: `${box.bin}:${process.env.PATH}`,
        VERSION,
        GITHUB_SHA: box.dispatched,
        GITHUB_RUN_ID: runId,
        GITHUB_TOKEN: "unused",
        RUNNER_TEMP: join(box.root, "tmp"),
        STATE: box.state,
    });

const onOrigin = (box, ref) => {
    const result = sh(box.root, ["git", "--git-dir", box.origin, "rev-parse", "--verify", "--quiet", `${ref}^{commit}`]);
    return result.code === 0 ? result.output.trim() : null;
};
const releaseCommits = (box) =>
    git(box.root, "--git-dir", box.origin, "log", "--format=%s", "main").split("\n").filter((s) => s === `new release: ${VERSION}`);
const manifestOnMain = (box) =>
    JSON.parse(git(box.root, "--git-dir", box.origin, "show", "main:manifest.json")).map(({ target, targetAbi }) => ({ target, targetAbi }));
// What gh was asked to publish, nothing when it was never reached.
const ghLog = (box) => {
    const log = join(box.state, "log");
    return existsSync(log) ? readFileSync(log, "utf8").trim().split("\n").filter(Boolean) : [];
};
const published = (box) => {
    const release = join(box.state, `release-${VERSION}`);
    return {
        draft: existsSync(join(release, "draft")),
        assets: existsSync(release) ? readdirSync(join(release, "assets")).sort() : [],
    };
};
const fail = (box, what) => writeFileSync(join(box.state, `fail-${what}`), "");
const recover = (box, what) => rmSync(join(box.state, `fail-${what}`));
const BOTH = [`streamyfin-${VERSION}-jf11.zip`, `streamyfin-${VERSION}-jf12.zip`];
const ENTRIES = [
    { target: "jf11", targetAbi: "10.11.9.0" },
    { target: "jf12", targetAbi: "12.0.0.0" },
];

describe("publishing a release", () => {
    test("one attempt tags a commit naming its run, publishes the release and writes both entries", () => {
        const box = sandbox();
        const dir = attempt(box);

        expect(run(box, dir, TAG_STEP).code).toBe(0);
        expect(run(box, dir, MANIFEST_STEP).code).toBe(0);

        const tagged = onOrigin(box, `refs/tags/${VERSION}`);
        expect(git(box.root, "--git-dir", box.origin, "rev-parse", `${tagged}^`)).toBe(box.dispatched);
        expect(git(box.root, "--git-dir", box.origin, "log", "-1", "--format=%B", tagged)).toBe(`new release: ${VERSION}\n\nRelease-Run: ${RUN}`);
        expect(ghLog(box)).toEqual([`create ${VERSION}`]);
        expect(published(box)).toEqual({ draft: false, assets: BOTH });
        expect(manifestOnMain(box)).toEqual(ENTRIES);
    });
});

describe("a rerun of the same run", () => {
    test("creates the release a first attempt could not create, without a second release commit", () => {
        const box = sandbox();
        fail(box, "create");
        expect(run(box, attempt(box), TAG_STEP).code).not.toBe(0);
        expect(onOrigin(box, `refs/tags/${VERSION}`)).not.toBeNull();

        recover(box, "create");
        const second = attempt(box);
        const tag = run(box, second, TAG_STEP);
        expect(tag.code).toBe(0);
        expect(tag.output).toContain("was tagged by an earlier attempt of this run");
        expect(run(box, second, MANIFEST_STEP).code).toBe(0);

        expect(releaseCommits(box)).toHaveLength(1);
        expect(published(box)).toEqual({ draft: false, assets: BOTH });
        expect(manifestOnMain(box)).toEqual(ENTRIES);
    });

    test("creates the release again after an upload failed and gh cleaned its draft up", () => {
        const box = sandbox();
        fail(box, "upload");
        expect(run(box, attempt(box), TAG_STEP).code).not.toBe(0);
        expect(published(box)).toEqual({ draft: false, assets: [] });

        recover(box, "upload");
        const second = attempt(box);
        expect(run(box, second, TAG_STEP).code).toBe(0);
        expect(run(box, second, MANIFEST_STEP).code).toBe(0);

        expect(ghLog(box)).toEqual([`create ${VERSION}`]);
        expect(published(box)).toEqual({ draft: false, assets: BOTH });
    });

    test("uploads the zip a killed attempt left out and publishes the draft it left", () => {
        const box = sandbox();
        // A cancelled job or a lost runner: gh is killed between two uploads.
        writeFileSync(join(box.state, "kill-upload"), "");
        expect(run(box, attempt(box), TAG_STEP).code).not.toBe(0);
        expect(published(box)).toEqual({ draft: true, assets: [BOTH[0]] });

        rmSync(join(box.state, "kill-upload"));
        const second = attempt(box);
        expect(run(box, second, TAG_STEP).code).toBe(0);
        expect(run(box, second, MANIFEST_STEP).code).toBe(0);

        expect(ghLog(box)).toEqual([`upload ${BOTH[1]}`, `publish ${VERSION}`]);
        expect(published(box)).toEqual({ draft: false, assets: BOTH });
    });

    test("writes the manifest a first attempt could not write, without uploading the zips again", () => {
        const box = sandbox();
        fail(box, "manifest");
        const first = attempt(box);
        expect(run(box, first, TAG_STEP).code).toBe(0);
        expect(run(box, first, MANIFEST_STEP).code).not.toBe(0);

        recover(box, "manifest");
        const second = attempt(box);
        expect(run(box, second, TAG_STEP).code).toBe(0);
        expect(run(box, second, MANIFEST_STEP).code).toBe(0);

        expect(ghLog(box)).toEqual([`create ${VERSION}`]);
        expect(releaseCommits(box)).toHaveLength(1);
        expect(manifestOnMain(box)).toEqual(ENTRIES);
    });

    test("uploads zips the build jobs built again, which have their old size and other bytes", () => {
        const box = sandbox();
        fail(box, "manifest");
        const first = attempt(box);
        expect(run(box, first, TAG_STEP).code).toBe(0);
        expect(run(box, first, MANIFEST_STEP).code).not.toBe(0);

        recover(box, "manifest");
        const second = attempt(box, { rebuilt: true });
        expect(run(box, second, TAG_STEP).code).toBe(0);
        const manifest = run(box, second, MANIFEST_STEP);

        expect(manifest.output).not.toContain("Checksum mismatch");
        expect(manifest.code).toBe(0);
        expect(ghLog(box)).toEqual([`create ${VERSION}`, `upload ${BOTH[0]}`, `upload ${BOTH[1]}`]);
        expect(manifestOnMain(box)).toEqual(ENTRIES);
    });

    test("fails when gh cannot say which releases exist, rather than create a second one", () => {
        const box = sandbox();
        fail(box, "create");
        expect(run(box, attempt(box), TAG_STEP).code).not.toBe(0);

        recover(box, "create");
        fail(box, "list");
        expect(run(box, attempt(box), TAG_STEP).code).not.toBe(0);

        expect(ghLog(box)).toEqual([]);
    });

    test("takes each targetAbi from the commit the zips were built from, not from main as it is by then", () => {
        const box = sandbox();
        fail(box, "manifest");
        const first = attempt(box);
        expect(run(box, first, TAG_STEP).code).toBe(0);
        expect(run(box, first, MANIFEST_STEP).code).not.toBe(0);
        landOnMain(box, "abi/jf12", "12.0.3.0", "build: raise the jf12 floor");

        recover(box, "manifest");
        const second = attempt(box);
        expect(run(box, second, TAG_STEP).code).toBe(0);
        expect(run(box, second, MANIFEST_STEP).code).toBe(0);

        expect(manifestOnMain(box)).toEqual(ENTRIES);
        expect(git(box.root, "--git-dir", box.origin, "log", "-2", "--format=%s", "main").split("\n"))
            .toEqual([`new release: ${VERSION} manifests`, "build: raise the jf12 floor"]);
    });
});

describe("a tag this run did not make stops it before anything is published", () => {
    const refused = (box, dir, runId = RUN) => {
        const before = onOrigin(box, "refs/heads/main");
        const tag = run(box, dir, TAG_STEP, runId);
        expect(tag.code).not.toBe(0);
        expect(tag.output).toContain(`::error::${VERSION} is already tagged`);
        expect(onOrigin(box, "refs/heads/main")).toBe(before);
    };

    test("another run dispatched on the same commit", () => {
        const box = sandbox();
        const first = attempt(box);
        expect(run(box, first, TAG_STEP).code).toBe(0);
        expect(run(box, first, MANIFEST_STEP).code).toBe(0);
        const log = ghLog(box);

        box.run = "202";
        refused(box, attempt(box), "202");

        expect(ghLog(box)).toEqual(log);
        expect(readFileSync(join(box.state, `release-${VERSION}`, "assets", BOTH[1]), "utf8")).toBe(`jf12 built by run ${RUN}, build 1`);
    });

    test("a release commit for this version made on top of another commit", () => {
        const box = sandbox();
        landOnMain(box, "elsewhere.txt", "x\n", "fix: something else");
        git(box.seed, "commit", "-q", "--allow-empty", "-m", `new release: ${VERSION}`, "-m", `Release-Run: ${RUN}`);
        git(box.seed, "tag", VERSION);
        git(box.seed, "push", "-q", "origin", "main", `refs/tags/${VERSION}`);

        refused(box, attempt(box));
        expect(ghLog(box)).toEqual([]);
    });

    test("a commit on top of the dispatched one that is not a release commit", () => {
        const box = sandbox();
        git(box.seed, "commit", "-q", "--allow-empty", "-m", "chore: not a release", "-m", `Release-Run: ${RUN}`);
        git(box.seed, "tag", VERSION);
        git(box.seed, "push", "-q", "origin", "main", `refs/tags/${VERSION}`);

        refused(box, attempt(box));
        expect(ghLog(box)).toEqual([]);
    });
});

test("a draft somebody prepared for the version is left alone, and the release created beside it", () => {
    const box = sandbox();
    const prepared = join(box.state, `release-${VERSION}`);
    mkdirSync(join(prepared, "assets"), { recursive: true });
    writeFileSync(join(prepared, "draft"), "");
    writeFileSync(join(prepared, "notes"), "Notes a maintainer is still writing");
    const dir = attempt(box);

    expect(run(box, dir, TAG_STEP).code).toBe(0);

    expect(ghLog(box)).toEqual([`create ${VERSION}`]);
    expect(published(box)).toEqual({ draft: false, assets: BOTH });
    const setAside = join(box.state, `other-${VERSION}`);
    expect(existsSync(join(setAside, "draft"))).toBe(true);
    expect(readFileSync(join(setAside, "notes"), "utf8")).toBe("Notes a maintainer is still writing");
});

test("the tag never reaches origin when main moved during the build, and the error says to start again", () => {
    const box = sandbox();
    const dir = attempt(box);
    landOnMain(box, "elsewhere.txt", "a commit that reached main during the build\n", "fix: something merged meanwhile");
    const moved = onOrigin(box, "refs/heads/main");

    const tag = run(box, dir, TAG_STEP);

    expect(tag.code).not.toBe(0);
    expect(tag.output).toContain("start the workflow again from main");
    expect(onOrigin(box, `refs/tags/${VERSION}`)).toBeNull();
    expect(onOrigin(box, "refs/heads/main")).toBe(moved);
    expect(ghLog(box)).toEqual([]);
});
