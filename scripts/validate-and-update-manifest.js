const https = require('https');
const crypto = require('crypto');
const fs = require('fs');
const { URL } = require('url');
const { place } = require('./manifest-versions');

const repository = process.env.GITHUB_REPO;
const version = process.env.VERSION;
const file = process.env.FILE;

// The tag the release lives under. It is the version on the stable channel and
// `unstable-<version>` on the other, so the links here are built from the tag while
// the entry is keyed on the version Jellyfin compares.
const tag = process.env.RELEASE_TAG || process.env.VERSION;

// The minimum server this build actually runs on, taken from the build rather
// than written here. It used to be hardcoded to 10.11.11, which is why the
// published manifest demanded a server three patches newer than necessary.
const targetAbi = process.env.JELLYFIN_ABI;

// One file per channel, named by the Makefile. Every Jellyfin line shares it, which
// is what manifest.json has always done: the entries on main already carry three
// different targetAbi values between them.
const manifestPath = `./${process.env.MANIFEST || 'manifest.json'}`;

const dryRun = process.env.DRY_RUN === '1';

// How many versions the manifest keeps. A release manifest keeps every version it ever
// published, because somebody may want to pin an old one, so unset means keep everything
// and the stable channel never sets it. An unstable manifest has no such claim on
// anyone: its entries point at prereleases that exist to be replaced, and left unpruned
// the file grows with every build.
const keep = (() => {
    const raw = (process.env.MANIFEST_KEEP || '').trim();
    if (!raw) return null;

    const n = Number(raw);
    if (!Number.isInteger(n) || n < 1) {
        console.error(`MANIFEST_KEEP must be a positive whole number, not: ${raw}`);
        process.exit(1);
    }
    return n;
})();

for (const [name, value] of Object.entries({ GITHUB_REPO: repository, VERSION: version, FILE: file, JELLYFIN_ABI: targetAbi })) {
    if (!value) {
        console.error(`${name} is not set. Run this through the Makefile, which computes all of them.`);
        process.exit(1);
    }
}

console.log(`Updating ${manifestPath} with ${file} (targetAbi ${targetAbi})`);

if (!fs.existsSync(manifestPath)) {
    console.error(`${manifestPath} not found`);
    process.exit(1);
}

const jsonData = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));

const newVersion = {
    version,
    changelog: `- See the full changelog at [GitHub](https://github.com/${repository}/releases/tag/${tag})\n`,
    targetAbi,
    sourceUrl: `https://github.com/${repository}/releases/download/${tag}/${file}`,
    checksum: getMD5FromFile(),
    timestamp: new Date().toISOString().replace(/\.\d{3}Z$/, 'Z')
};

async function updateManifest() {
    await validVersion(newVersion);

    const { versions, replaced, dropped } = place(jsonData[0].versions, newVersion, keep);
    jsonData[0].versions = versions;

    if (replaced > 0) {
        console.log(`Replaced ${replaced} existing entr${replaced === 1 ? 'y' : 'ies'} for ${newVersion.version}`);
    }

    // Trimming drops builds nobody can reach any more. The release they point at stays on
    // GitHub, only the manifest stops offering it.
    if (dropped.length > 0) {
        const versions = [...new Set(dropped.map((v) => v.version))];
        console.log(`Keeping the newest ${keep} versions, dropping ${versions.join(', ')}`);
    }

    const updated = JSON.stringify(jsonData, null, 4);

    // Everything above still runs on a dry run: the version, the checksum of the zip
    // that was just built, dropping any entry for this version, and serializing the
    // result. Only the write is skipped. The manifest is a tracked file, so writing it
    // leaves the working tree carrying a version entry for a release that does not
    // exist, and on a pull request the version is the 0.0.0.0 placeholder.
    if (dryRun) {
        console.log(`DRY_RUN set, ${manifestPath} not written. It would have gained:`);
        console.log(JSON.stringify(newVersion, null, 4));
        process.exit(0);
    }

    // Write the updated manifest to file if validation is successful
    fs.writeFileSync(manifestPath, updated);
    console.log('Manifest updated successfully.');
    process.exit(0); // Exit with no error
}

async function validVersion(version) {
    console.log(`Validating version ${version.version}...`);

    // On a pull request the release does not exist yet, so there is nothing to
    // download and compare against. Everything else still runs, which is the
    // point: the packaging path gets exercised outside of a real release. The
    // write itself is skipped further down, in updateManifest.
    if (dryRun) {
        console.log('DRY_RUN set, skipping the remote checksum verification.');
        return;
    }

    const isValidChecksum = await verifyChecksum(version.sourceUrl, version.checksum);
    if (!isValidChecksum) {
        console.error(`Checksum mismatch for URL: ${version.sourceUrl}`);
        process.exit(1); // Exit with an error code
    } else {
        console.log(`Version ${version.version} is valid.`);
    }
}

async function verifyChecksum(url, expectedChecksum) {
    try {
        const hash = await downloadAndHashFile(url);
        return hash === expectedChecksum;
    } catch (error) {
        console.error(`Error verifying checksum for URL: ${url}`, error);
        return false;
    }
}

async function downloadAndHashFile(url, redirects = 5) {
    if (redirects === 0) {
        throw new Error('Too many redirects');
    }

    return new Promise((resolve, reject) => {
        https.get(url, (response) => {
            if (response.statusCode >= 300 && response.statusCode < 400 && response.headers.location) {
                // Follow redirect
                const redirectUrl = new URL(response.headers.location, url).toString();
                downloadAndHashFile(redirectUrl, redirects - 1)
                    .then(resolve)
                    .catch(reject);
            } else if (response.statusCode === 200) {
                const hash = crypto.createHash('md5');
                response.pipe(hash);
                response.on('end', () => {
                    resolve(hash.digest('hex'));
                });
                response.on('error', (err) => {
                    reject(err);
                });
            } else {
                reject(new Error(`Failed to get '${url}' (${response.statusCode})`));
            }
        }).on('error', (err) => {
            reject(err);
        });
    });
}

function getMD5FromFile() {
    const fileBuffer = fs.readFileSync(`./dist/${file}`);
    return crypto.createHash('md5').update(fileBuffer).digest('hex');
}

async function run() {
    await updateManifest();
}

run();
