import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve, sep } from "node:path";
import test from "node:test";
import { changedPaths, classifyChanges, comparisonBase, findPackage, planChecks, restorePackage, verifyPackage } from "./ci.mjs";

const repository = "doabell/comprimer";
const sha = "a".repeat(40);
const run = { id: 42, path: ".github/workflows/ci.yml", event: "push", head_branch: "main", head_sha: sha,
    status: "completed", conclusion: "success", repository: { full_name: repository }, head_repository: { full_name: repository } };
const packageArtifacts = ["Comprimer-win-x64", "Comprimer-debug-symbols-win-x64"].map((name) => ({
    name, expired: false, expires_at: "2999-01-01T00:00:00Z", workflow_run: { id: run.id, head_branch: "main", head_sha: sha },
}));
const api = async (endpoint) => endpoint.includes("/artifacts?") ? { artifacts: packageArtifacts } : { workflow_runs: [run] };

function fixture(t) {
    const parent = resolve(tmpdir());
    const directory = mkdtempSync(join(parent, "comprimer-ci-"));
    t.after(() => {
        if (!resolve(directory).startsWith(parent + sep)) throw new Error("Unsafe test cleanup path.");
        rmSync(directory, { recursive: true, force: true });
    });
    return directory;
}

function writePackage(directory, commit = sha) {
    const files = {};
    for (const path of ["publish/Comprimer.exe", "publish/README.md", "publish/LICENSE", "symbols/Comprimer.pdb"]) {
        mkdirSync(join(directory, path.split("/")[0]), { recursive: true });
        const bytes = Buffer.from(`fixture ${path}`);
        writeFileSync(join(directory, path), bytes);
        files[path] = createHash("sha256").update(bytes).digest("hex");
    }
    writeFileSync(join(directory, "publish/build-info.json"), JSON.stringify({ commit, files }));
}

test("changed inputs select checks without building release binaries on PRs", () => {
    assert.deepEqual(classifyChanges(["docs/guide.md"]), { rust: false, package: false });
    assert.deepEqual(classifyChanges(["README.md"]), { rust: false, package: true });
    assert.deepEqual(classifyChanges(["tests/process.rs"]), { rust: true, package: false });
    for (const paths of [null, ["src/ui.rs"], ["Cargo.lock"], ["new-build-input"], [".github/workflows/ci.yml"]]) {
        assert.deepEqual(classifyChanges(paths), { rust: true, package: true });
    }
    assert.deepEqual(planChecks("pull_request", "refs/pull/5/merge", ["src/ui.rs"]), { rust: true, package: false });
    assert.deepEqual(planChecks("push", "refs/heads/main", ["src/ui.rs"]), { rust: true, package: true });
    assert.deepEqual(planChecks("push", "refs/tags/v2.0.0", []), { rust: true, package: true });
    assert.deepEqual(planChecks("workflow_dispatch", "refs/heads/main", []), { rust: true, package: true });
});

test("main checks compare against successful CI, including unfinished earlier pushes", async () => {
    const base = await comparisonBase({ eventName: "push", event: { ref: "refs/heads/main", before: "b".repeat(40) }, repository,
        api: async () => ({ workflow_runs: [{ ...run, conclusion: "cancelled", head_sha: "b".repeat(40) }, run] }) });
    assert.equal(base, sha);
    assert.equal(await comparisonBase({ eventName: "pull_request", event: { pull_request: { base: { sha } } }, repository, api }), sha);
    assert.equal(await comparisonBase({ eventName: "push", event: { ref: "refs/heads/main" }, repository,
        api: async () => ({ workflow_runs: [] }) }), null);
});

test("git change detection includes both sides of renames and rejects unknown history", (t) => {
    const directory = fixture(t);
    const git = (...args) => {
        const result = spawnSync("git", ["-c", "user.name=CI test", "-c", "user.email=ci@example.invalid", "-c", "commit.gpgsign=false", ...args],
            { cwd: directory, windowsHide: true, encoding: "utf8" });
        assert.equal(result.status, 0, result.stderr);
        return result.stdout.trim();
    };
    git("init");
    writeFileSync(join(directory, "README.md"), "fixture");
    git("add", ".");
    git("commit", "-m", "initial");
    const base = git("rev-parse", "HEAD");
    git("mv", "README.md", "source été.rs");
    git("commit", "-m", "rename");
    const head = git("rev-parse", "HEAD");
    assert.deepEqual(changedPaths(base, head, directory), ["README.md", "source été.rs"]);
    assert.equal(changedPaths(head, base, directory), null);
    assert.equal(changedPaths("0".repeat(40), head, directory), null);
});

test("release reuse accepts only successful same-repository main outputs at the exact commit", async () => {
    assert.equal((await findPackage({ repository, sha, api })).run.id, run.id);
    for (const mutation of [
        { event: "pull_request" }, { head_branch: "feature" }, { conclusion: "failure" }, { status: "in_progress" },
        { path: ".github/workflows/other.yml" }, { head_sha: "b".repeat(40) }, { repository: { full_name: "other/repo" } },
        { head_repository: { full_name: "fork/comprimer" } },
    ]) {
        const fakeApi = async (endpoint) => endpoint.includes("/artifacts?") ? { artifacts: packageArtifacts } : { workflow_runs: [{ ...run, ...mutation }] };
        assert.equal(await findPackage({ repository, sha, api: fakeApi }), null);
    }
});

test("missing, expired, or mismatched artifacts force a source build", async () => {
    for (const artifacts of [
        packageArtifacts.slice(0, 1),
        packageArtifacts.map((a) => ({ ...a, expired: true })),
        packageArtifacts.map((a) => ({ ...a, expires_at: "2000-01-01T00:00:00Z" })),
        packageArtifacts.map((a) => ({ ...a, workflow_run: { ...a.workflow_run, head_sha: "b".repeat(40) } })),
    ]) {
        const fakeApi = async (endpoint) => endpoint.includes("/artifacts?") ? { artifacts } : { workflow_runs: [run] };
        assert.equal(await findPackage({ repository, sha, api: fakeApi }), null);
    }
});

test("download failures and invalid manifests cannot contaminate the fallback build", async (t) => {
    for (const failure of ["download", "commit", "hash"]) {
        const directory = fixture(t);
        const match = await restorePackage({ repository, sha, api, directory, download: async (_, staging) => {
            writePackage(staging, failure === "commit" ? "b".repeat(40) : sha);
            if (failure === "download") throw new Error("expired during download");
            if (failure === "hash") writeFileSync(join(staging, "publish/Comprimer.exe"), "corrupt");
        } });
        assert.equal(match, null);
        assert.equal(existsSync(join(directory, "publish")), false);
        assert.equal(existsSync(join(directory, "symbols")), false);
    }
});

test("verified artifacts are restored with their matching symbols", async (t) => {
    const directory = fixture(t);
    const match = await restorePackage({ repository, sha, api, directory, download: async (_, staging) => writePackage(staging) });
    assert.equal(match.run.id, run.id);
    assert.equal(verifyPackage(directory, sha).commit, sha);
    writeFileSync(join(directory, "symbols/Comprimer.pdb"), "wrong symbols");
    assert.throws(() => verifyPackage(directory, sha), /integrity/);
});

test("release script creates drafts, reuses drafts, and refuses published releases", (t) => {
    const script = resolve("scripts/draft-release.ps1");
    const psQuote = (value) => `'${value.replaceAll("'", "''")}'`;
    for (const existing of ["none", "draft", "published"]) {
        const directory = fixture(t);
        writePackage(directory);
        const callsPath = join(directory, "calls.json");
        const command = `
            $global:ghCalls = @()
            function global:gh {
                $global:ghCalls += ,@($args)
                $global:LASTEXITCODE = 0
                if ($args[1] -eq 'view') {
                    if ('${existing}' -eq 'none') { $global:LASTEXITCODE = 1 }
                    elseif ('${existing}' -eq 'draft') { 'true' }
                    else { 'false' }
                }
            }
            $failed = $false
            try { & ${psQuote(script)} } catch { $failed = $true }
            ConvertTo-Json -InputObject @($global:ghCalls) -Depth 5 | Set-Content -LiteralPath ${psQuote(callsPath)}
            if ($failed) { exit 7 }
        `;
        const result = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", command], {
            cwd: directory, encoding: "utf8", windowsHide: true,
            env: { ...process.env, GITHUB_REF_NAME: "v2.0.0", GITHUB_REPOSITORY: repository, GITHUB_SHA: sha },
        });
        const calls = JSON.parse(readFileSync(callsPath, "utf8").replace(/^\uFEFF/, ""));
        assert.equal(result.status, existing === "published" ? 7 : 0, result.stderr);
        const create = calls.find((args) => args[1] === "create");
        if (existing === "none") {
            assert.ok(create, JSON.stringify(calls));
            assert.ok(create.includes("--draft"));
            assert.ok(create.includes("--verify-tag"));
        } else {
            assert.equal(create, undefined);
        }
        assert.equal(calls.some((args) => args[1] === "upload"), existing !== "published");
        assert.equal(calls.some((args) => args[1] === "edit"), false);
    }
});
