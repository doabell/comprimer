import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { appendFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const workflow = ".github/workflows/ci.yml";
const artifacts = ["Comprimer-win-x64", "Comprimer-debug-symbols-win-x64"];
const allChecks = () => ({ rust: true, package: true });
const validSha = (sha) => typeof sha === "string" && /^[a-f0-9]{40}$/i.test(sha) && !/^0+$/.test(sha);
const validRepo = (repo) => typeof repo === "string" && /^[\w.-]+\/[\w.-]+$/.test(repo);
const sameRepo = (value, repo) => value?.full_name?.toLowerCase() === repo.toLowerCase();

export function classifyChanges(paths) {
    if (paths === null) return allChecks();
    const checks = { rust: false, package: false };
    for (const path of paths) {
        if (path.startsWith("docs/") || path === "SECURITY.md") continue;
        if (["README.md", "LICENSE", "CHANGELOG.md"].includes(path)) {
            checks.package = true;
        } else if (path.startsWith("tests/") || path.startsWith("examples/")) {
            checks.rust = true;
        } else {
            // Unknown build/configuration inputs must never silently skip checks.
            return allChecks();
        }
    }
    return checks;
}

function trustedMainRun(run, repository) {
    return Number.isSafeInteger(run.id) && run.id > 0 && run.path === workflow &&
        run.event === "push" && run.head_branch === "main" &&
        run.status === "completed" && run.conclusion === "success" &&
        sameRepo(run.repository, repository) && sameRepo(run.head_repository, repository);
}

export async function comparisonBase({ eventName, event, repository, api }) {
    if (eventName === "pull_request") return event.pull_request?.base?.sha ?? null;
    if (eventName !== "push" || event.ref !== "refs/heads/main" || !validRepo(repository)) return null;
    // Compare against a successful run, not event.before: cancelled/failed pushes still need checks.
    const { workflow_runs: runs = [] } = await api(
        `repos/${repository}/actions/workflows/ci.yml/runs?branch=main&event=push&status=success&per_page=100`,
    );
    return runs.find((run) => trustedMainRun(run, repository))?.head_sha ?? null;
}

export function changedPaths(base, sha, cwd = process.cwd()) {
    if (!validSha(base) || !validSha(sha)) return null;
    const ancestor = spawnSync("git", ["merge-base", "--is-ancestor", base, sha], { cwd, windowsHide: true });
    if (ancestor.error || ancestor.status !== 0) return null;
    const diff = spawnSync("git", ["diff", "--no-renames", "--name-only", "-z", base, sha, "--"], {
        cwd, windowsHide: true, encoding: "utf8",
    });
    return diff.error || diff.status !== 0 ? null : diff.stdout.split("\0").filter(Boolean);
}

export function planChecks(eventName, ref, paths) {
    if (ref?.startsWith("refs/tags/") || eventName === "workflow_dispatch") return allChecks();
    const checks = classifyChanges(paths);
    return { rust: checks.rust, package: eventName === "push" && ref === "refs/heads/main" && checks.package };
}

export async function findPackage({ repository, sha, api, now = Date.now() }) {
    if (!validRepo(repository) || !validSha(sha)) throw new Error("An exact repository and commit are required.");
    const query = new URLSearchParams({ branch: "main", event: "push", status: "success", head_sha: sha, per_page: "100" });
    const { workflow_runs: runs = [] } = await api(`repos/${repository}/actions/workflows/ci.yml/runs?${query}`);
    for (const run of runs) {
        if (!trustedMainRun(run, repository) || run.head_sha !== sha) continue;
        const { artifacts: available = [] } = await api(`repos/${repository}/actions/runs/${run.id}/artifacts?per_page=100`);
        const matches = artifacts.map((name) => available.find((artifact) =>
            artifact.name === name && artifact.expired === false && Date.parse(artifact.expires_at) > now &&
            artifact.workflow_run?.id === run.id && artifact.workflow_run?.head_branch === "main" &&
            artifact.workflow_run?.head_sha === sha,
        ));
        if (matches.every(Boolean)) return { run, artifacts: matches };
    }
    return null;
}

export function verifyPackage(directory, sha) {
    if (!validSha(sha)) throw new Error("An exact package commit is required.");
    const manifest = JSON.parse(readFileSync(join(directory, "publish/build-info.json"), "utf8").replace(/^\uFEFF/, ""));
    if (manifest.commit !== sha) throw new Error("Package commit mismatch.");
    for (const path of ["publish/Comprimer.exe", "publish/README.md", "publish/LICENSE", "symbols/Comprimer.pdb"]) {
        const data = readFileSync(join(directory, path));
        if (!data.length || createHash("sha256").update(data).digest("hex") !== manifest.files?.[path]) {
            throw new Error(`Package integrity check failed: ${path}`);
        }
    }
    return manifest;
}

export async function restorePackage({ repository, sha, api, download, directory }) {
    if (existsSync(join(directory, "publish")) || existsSync(join(directory, "symbols"))) {
        throw new Error("Package output directories already exist.");
    }
    let match;
    let staging;
    try {
        match = await findPackage({ repository, sha, api });
        if (!match) return null;
        const parent = join(directory, "artifacts");
        mkdirSync(parent, { recursive: true });
        staging = mkdtempSync(join(parent, "reuse-"));
        await download(match, staging);
        verifyPackage(staging, sha);
    } catch {
        // Partial/expired downloads stay isolated from the fallback source build.
        console.log("Verified package unavailable; building from source.");
        return null;
    }
    renameSync(join(staging, "publish"), join(directory, "publish"));
    renameSync(join(staging, "symbols"), join(directory, "symbols"));
    return match;
}

function gh(args) {
    const result = spawnSync("gh", args, { encoding: "utf8", windowsHide: true, timeout: 120_000, maxBuffer: 10 * 1024 * 1024 });
    if (result.error || result.status !== 0) throw new Error("GitHub request failed.");
    return result.stdout;
}

function required(name) {
    if (!process.env[name]) throw new Error(`${name} is required.`);
    return process.env[name];
}

function output(values) {
    appendFileSync(required("GITHUB_OUTPUT"), Object.entries(values).map(([key, value]) => `${key}=${value}\n`).join(""));
}

async function main(mode) {
    const api = async (endpoint) => JSON.parse(gh(["api", endpoint]));
    const repository = required("GITHUB_REPOSITORY");
    const sha = required("GITHUB_SHA");
    if (mode === "plan") {
        let paths = null;
        try {
            const event = JSON.parse(readFileSync(required("GITHUB_EVENT_PATH"), "utf8"));
            const base = await comparisonBase({ eventName: process.env.GITHUB_EVENT_NAME, event, repository, api });
            paths = changedPaths(base, sha);
        } catch {
            console.log("Change detection unavailable; running full checks.");
        }
        const plan = planChecks(process.env.GITHUB_EVENT_NAME, process.env.GITHUB_REF, paths);
        console.log("CI plan:", plan);
        output(plan);
    } else if (mode === "reuse") {
        const match = await restorePackage({ repository, sha, api, directory: process.cwd(),
            download: async ({ run }, directory) => {
                for (const [index, name] of artifacts.entries()) {
                    gh(["run", "download", String(run.id), "--repo", repository, "--name", name,
                        "--dir", join(directory, index === 0 ? "publish" : "symbols")]);
                }
            },
        });
        output({ reused: Boolean(match) });
        const message = match ? `Reused verified main package from run ${match.run.id} (${sha}).` : "Building package from source.";
        console.log(message);
        if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${message}\n`);
    } else if (mode === "verify-package") {
        verifyPackage(process.cwd(), sha);
    } else {
        throw new Error("Expected plan, reuse, or verify-package.");
    }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main(process.argv[2]);
