// The "Tag and release" step of prerelease.yml, run for real against a throwaway origin. gh
// creates a release as a draft, uploads, then publishes, so an attempt cancelled or killed in
// between leaves the draft; the rerun found it with `gh release view`, which a draft answers,
// uploaded into it and stopped, and the manifest then pointed testers at a download only a
// published release serves (#250). It also took any draft carrying the tag for its own. git is
// real; gh is a stand-in that records what it is asked, as in release-resume.test.js.

import { afterAll, describe, expect, test } from "bun:test";
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const steps = Bun.YAML.parse(readFileSync(resolve(".github/workflows/prerelease.yml"), "utf8")).jobs.publish.steps;
const TAG_STEP = steps.find((step) => step.name === "Tag and release").run;

const VERSION = "0.68.1.60";
const TAG = `unstable-${VERSION}`;
const BOTH = [`streamyfin-${VERSION}-jf11.zip`, `streamyfin-${VERSION}-jf12.zip`];

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

// gh as this step uses it. Each release is a folder with its tag and author, so a tag can carry
// several, as GitHub allows for drafts. create makes the release published and a prerelease when
// asked, reading the values of --title, --notes and --target as values; view prints the fields
// --json asks for and, with several releases on a tag, takes the published one, else the first
// draft; list includes drafts; edit --draft=false publishes.
const GH = `#!/usr/bin/env bash
set -eu
verb="$2"
mkdir -p "$STATE/releases"
if [ "$verb" = list ]; then
  printf '['; first=1
  for r in "$STATE"/releases/*; do
    [ -d "$r" ] || continue
    [ $first = 1 ] || printf ','; first=0
    draft=false; [ -f "$r/draft" ] && draft=true
    printf '{"tagName":"%s","isDraft":%s}' "$(cat "$r/tag")" "$draft"
  done
  printf ']\\n'; exit 0
fi
tag="$3"; shift 3
named() {
  local r first=""
  for r in "$STATE"/releases/*; do
    [ -d "$r" ] && [ "$(cat "$r/tag")" = "$tag" ] || continue
    if [ ! -f "$r/draft" ]; then echo "$r"; return; fi
    [ -n "$first" ] || first=$r
  done
  echo "$first"
}
release=$(named)
case "$verb" in
  view)
    [ -n "$release" ] || { echo "release not found" >&2; exit 1; }
    fields=""; while [ $# -gt 0 ]; do [ "$1" = "--json" ] && fields="$2"; shift; done
    [ -n "$fields" ] || exit 0
    out=""
    for field in $(echo "$fields" | tr ',' ' '); do
      case "$field" in
        isDraft) value=false; [ -f "$release/draft" ] && value=true ;;
        author) value="{\\"login\\":\\"$(cat "$release/author")\\"}" ;;
        assets)
          value="["; first=1
          for f in "$release"/assets/*; do
            [ -e "$f" ] || continue
            [ $first = 1 ] || value="$value,"; first=0
            value="$value{\\"name\\":\\"$(basename "$f")\\",\\"digest\\":\\"sha256:$(shasum -a 256 "$f" | cut -d' ' -f1)\\",\\"state\\":\\"uploaded\\"}"
          done
          value="$value]" ;;
        *) echo "unexpected field: $field" >&2; exit 2 ;;
      esac
      out="$out\${out:+,}\\"$field\\":$value"
    done
    printf '{%s}\\n' "$out" ;;
  create)
    if [ -n "$release" ] && [ ! -f "$release/draft" ]; then echo "a release with the same tag name already exists: $tag" >&2; exit 1; fi
    last=$(ls "$STATE/releases" | sort | tail -1)
    release="$STATE/releases/$(printf '%03d' $(( 10#\${last:-0} + 1 )))"
    mkdir -p "$release/assets"; echo "$tag" > "$release/tag"; echo "github-actions[bot]" > "$release/author"
    while [ $# -gt 0 ]; do
      case "$1" in
        --title|--notes|--target) shift 2; continue ;;
        --prerelease) touch "$release/prerelease"; shift; continue ;;
        -*) shift; continue ;;
      esac
      cp "$1" "$release/assets/"; shift
    done
    echo "create $tag" >> "$STATE/log" ;;
  upload)
    [ -n "$release" ] || exit 1
    for f in "$@"; do
      case "$f" in -*) continue ;; esac
      cp "$f" "$release/assets/"; echo "upload $(basename "$f") to $(basename "$release")" >> "$STATE/log"
    done ;;
  edit)
    [ -n "$release" ] || exit 1; rm -f "$release/draft"; echo "publish $tag" >> "$STATE/log" ;;
  *) echo "unexpected: gh release $verb $tag $*" >&2; exit 2 ;;
esac
`;

const sandboxes = [];
afterAll(() => {
    for (const dir of sandboxes) rmSync(dir, { recursive: true, force: true });
});

// An origin holding the commit the build is made from, and a runner's checkout of it with the
// two zips the build jobs handed over.
const sandbox = () => {
    const root = mkdtempSync(join(tmpdir(), "prerelease-resume-"));
    sandboxes.push(root);
    const box = { root, origin: join(root, "origin.git"), bin: join(root, "bin"), state: join(root, "state"), work: join(root, "work") };
    for (const dir of [box.bin, box.state]) mkdirSync(dir);
    writeFileSync(join(box.bin, "gh"), GH);
    chmodSync(join(box.bin, "gh"), 0o755);

    git(root, "init", "-q", "--bare", "-b", "develop", box.origin);
    git(root, "init", "-q", "-b", "develop", box.work);
    for (const [key, value] of [["user.email", "test@example.com"], ["user.name", "test"], ["commit.gpgsign", "false"], ["tag.gpgsign", "false"]]) {
        git(box.work, "config", key, value);
    }
    writeFileSync(join(box.work, "README"), "what the unstable build is made from\n");
    writeFileSync(join(box.work, ".gitignore"), "dist/\n");
    git(box.work, "add", "-A");
    git(box.work, "commit", "-q", "-m", "chore: develop");
    git(box.work, "remote", "add", "origin", box.origin);
    git(box.work, "push", "-q", "origin", "develop");
    box.sha = git(box.work, "rev-parse", "HEAD");
    mkdirSync(join(box.work, "dist"));
    for (const target of ["jf11", "jf12"]) writeFileSync(join(box.work, "dist", `streamyfin-${VERSION}-${target}.zip`), `${target} build`);
    return box;
};

const run = (box) =>
    sh(box.work, ["bash", "--noprofile", "--norc", "-e", "-c", TAG_STEP], {
        PATH: `${box.bin}:${process.env.PATH}`,
        VERSION,
        REF: "develop",
        SHA: box.sha,
        GITHUB_TOKEN: "unused",
        STATE: box.state,
    });

// What an earlier attempt or a person left: the tag on origin, and a release made by `author`.
const tagged = (box) => git(box.work, "push", "-q", "origin", `${box.sha}:refs/tags/${TAG}`);
const leave = (box, { author, draft, assets }) => {
    const root = join(box.state, "releases");
    mkdirSync(root, { recursive: true });
    const last = readdirSync(root).sort().at(-1);
    const dir = join(root, String((last ? Number(last) : 0) + 1).padStart(3, "0"));
    mkdirSync(join(dir, "assets"), { recursive: true });
    writeFileSync(join(dir, "tag"), `${TAG}\n`);
    writeFileSync(join(dir, "author"), `${author}\n`);
    if (draft) writeFileSync(join(dir, "draft"), "");
    for (const asset of assets) writeFileSync(join(dir, "assets", asset), "an earlier upload");
};
const releases = (box) => {
    const root = join(box.state, "releases");
    if (!existsSync(root)) return [];
    return readdirSync(root).sort().map((name) => join(root, name)).map((dir) => ({
        author: readFileSync(join(dir, "author"), "utf8").trim(),
        draft: existsSync(join(dir, "draft")),
        assets: readdirSync(join(dir, "assets")).sort(),
    }));
};
const tagOnOrigin = (box) => {
    const result = sh(box.root, ["git", "--git-dir", box.origin, "rev-parse", "--verify", "--quiet", `refs/tags/${TAG}^{commit}`]);
    return result.code === 0 ? result.output.trim() : null;
};

describe("an unstable build's release", () => {
    test("a first attempt tags the commit and publishes the release", () => {
        const box = sandbox();

        expect(run(box).code).toBe(0);

        expect(tagOnOrigin(box)).toBe(box.sha);
        expect(releases(box)).toEqual([{ author: "github-actions[bot]", draft: false, assets: BOTH }]);
    });

    test("the draft an attempt killed between gh's uploads left is finished and published", () => {
        const box = sandbox();
        tagged(box);
        leave(box, { author: "github-actions[bot]", draft: true, assets: [BOTH[0]] });

        const result = run(box);

        expect(result.code).toBe(0);
        expect(releases(box)).toEqual([{ author: "github-actions[bot]", draft: false, assets: BOTH }]);
    });

    test("a draft somebody made for the tag is left as it is", () => {
        const box = sandbox();
        tagged(box);
        leave(box, { author: "a-maintainer", draft: true, assets: [] });

        const result = run(box);

        expect(result.code).not.toBe(0);
        expect(result.output).toContain("was made by a-maintainer, not by this workflow");
        expect(releases(box)).toEqual([{ author: "a-maintainer", draft: true, assets: [] }]);
    });

    test("two releases on the tag stop the rerun before either is touched", () => {
        const box = sandbox();
        tagged(box);
        leave(box, { author: "github-actions[bot]", draft: true, assets: [BOTH[0]] });
        leave(box, { author: "a-maintainer", draft: true, assets: [] });

        const result = run(box);

        expect(result.code).not.toBe(0);
        expect(result.output).toContain(`2 releases carry ${TAG}`);
        expect(releases(box).map(({ draft, assets }) => ({ draft, assets }))).toEqual([
            { draft: true, assets: [BOTH[0]] },
            { draft: true, assets: [] },
        ]);
    });

    test("a draft waiting before the tag exists stops the build before the tag is pushed", () => {
        const box = sandbox();
        leave(box, { author: "a-maintainer", draft: true, assets: [] });

        const result = run(box);

        expect(result.code).not.toBe(0);
        expect(result.output).toContain("Nothing was pushed");
        expect(tagOnOrigin(box)).toBeNull();
        expect(releases(box)).toEqual([{ author: "a-maintainer", draft: true, assets: [] }]);
    });
});
