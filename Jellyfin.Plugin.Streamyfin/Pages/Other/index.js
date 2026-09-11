const homePage = () => document.getElementById('home-page');
const saveBtn = () => document.getElementById('save-other-btn');
const backupBtn = () => document.getElementById('backup-btn');
const restoreBtn = () => document.getElementById('restore-btn');
const restoreFile = () => document.getElementById('restore-file');
const said = () => document.getElementById('backup-said');

const url = (path) => window.ApiClient.getUrl(`streamyfin/${path}`);

// The name says which server and when, since a folder of backups with the same name is
// a folder of files nobody can tell apart.
const fileName = () => {
    const server = (window.ApiClient.serverInfo?.()?.Name ?? 'jellyfin').replace(/[^a-z0-9]+/gi, '-').toLowerCase();
    return `streamyfin-${server}-${new Date().toISOString().slice(0, 10)}.json`;
};

const download = (text, name) => {
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
    link.download = name;
    link.click();
    URL.revokeObjectURL(link.href);
};

// One sentence rather than a count of four things: what an administrator wants to know
// is whether the server they backed up is the server they have now.
const restored = (report) => {
    const parts = [];
    if (report.configuration) parts.push('the configuration');
    if (report.groups) parts.push(`${report.groups} group${report.groups === 1 ? '' : 's'}`);
    if (report.users) parts.push(`${report.users} user${report.users === 1 ? '' : 's'}`);

    const said = parts.length ? `Restored ${parts.join(', ')}.` : 'That file had nothing in it.';

    return report.unknownUsers
        ? `${said} ${report.unknownUsers} member${report.unknownUsers === 1 ? '' : 's'} of it are not on this server, and were left out.`
        : said;
};

const getValues = () => ({
    other: {
        homePage: homePage()?.value
    }
})

export default function (view, params) {

    // init code here
    view.addEventListener('viewshow', (e) => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then((shared) => {
            shared.setPage("Other");

            homePage().options.length = 0;
            shared.StreamyfinTabs().forEach(tab => homePage().add(new Option(tab.name, tab.resource)))

            homePage().value = shared.getConfig()?.other?.homePage;

            shared.setOnConfigUpdatedListener('other', (config) => {
                console.log("updating dom for other")
                const {other} = config;

                homePage().value = other.homePage
            })

            shared.keyedEventListener(saveBtn(), 'click', function (e) {
                e.preventDefault();
                shared.saveConfig()
            })

            shared.keyedEventListener(backupBtn(), 'click', async function (e) {
                e.preventDefault();
                said().textContent = 'Collecting…';
                try {
                    const backup = await window.ApiClient.ajax({
                        type: 'GET', url: url('v1/backup'), contentType: 'application/json',
                    }).then((response) => response.text());

                    download(backup, fileName());
                    said().textContent = 'Downloaded.';
                } catch {
                    said().textContent = 'The server could not be asked for a backup.';
                }
            })

            shared.keyedEventListener(restoreBtn(), 'click', function (e) {
                e.preventDefault();
                restoreFile().value = '';
                restoreFile().click();
            })

            shared.keyedEventListener(restoreFile(), 'change', async function () {
                const file = restoreFile().files?.[0];
                if (!file) return;

                said().textContent = 'Restoring…';
                try {
                    const report = await window.ApiClient.ajax({
                        type: 'POST', url: url('v1/backup'), contentType: 'application/json',
                        data: await file.text(),
                    }).then((response) => response.json());

                    said().textContent = restored(report);
                    // The page holds what it read at load, and every tab reads from it.
                    window.location.reload();
                } catch (rejected) {
                    const body = await (typeof rejected?.text === 'function' ? rejected.text() : Promise.resolve(null))
                        .catch(() => null);
                    let problem = null;
                    try { problem = JSON.parse(body ?? '').problem; } catch { /* not ours */ }
                    said().textContent = problem ?? 'That file could not be restored.';
                }
            })
        })
    });
}