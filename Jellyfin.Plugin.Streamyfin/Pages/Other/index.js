const homePage = () => document.getElementById('home-page');
const saveBtn = () => document.getElementById('save-other-btn');
const backupBtn = () => document.getElementById('backup-btn');
const restoreBtn = () => document.getElementById('restore-btn');
const restoreFile = () => document.getElementById('restore-file');
const said = () => document.getElementById('backup-said');

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
    // In the document and revoked on a later turn: a detached anchor and a URL revoked
    // on the same tick have both produced a cancelled save.
    link.hidden = true;
    document.body.appendChild(link);
    link.click();
    setTimeout(() => {
        URL.revokeObjectURL(link.href);
        link.remove();
    }, 0);
};

// One sentence rather than a count of four things: what an administrator wants to know
// is whether the server they backed up is the server they have now.
const restored = (report) => {
    const parts = [];
    if (report.configuration) parts.push('the configuration');
    if (report.groups) parts.push(`${report.groups} group${report.groups === 1 ? '' : 's'}`);
    if (report.users) parts.push(`${report.users} user${report.users === 1 ? '' : 's'}`);

    const sentence = parts.length ? `Restored ${parts.join(', ')}.` : 'That file had nothing in it.';
    const missing = [];

    if (report.unknownMembers) {
        missing.push(`${report.unknownMembers} group member${report.unknownMembers === 1 ? '' : 's'}`);
    }

    if (report.unknownUsers) {
        missing.push(`${report.unknownUsers} user${report.unknownUsers === 1 ? '' : 's'} it targets`);
    }

    return missing.length
        ? `${sentence} ${missing.join(' and ')} are not on this server, and were left out.`
        : sentence;
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
                        type: 'GET',
                        url: window.ApiClient.getUrl('streamyfin/v1/backup'),
                        contentType: 'application/json',
                    }).then((response) => response.text());

                    try {
                        download(backup, fileName());
                        said().textContent = 'Downloaded.';
                    } catch (error) {
                        // The server answered. Saying it did not would send an
                        // administrator to the wrong place.
                        console.error(error);
                        said().textContent = 'The backup could not be saved by this browser.';
                    }
                } catch (error) {
                    console.error(error);
                    said().textContent = 'The server could not be asked for a backup.';
                }
            })

            shared.keyedEventListener(restoreBtn(), 'click', async function (e) {
                e.preventDefault();

                // The one action here that cannot be undone: it replaces the
                // configuration and drops every group and every per user override.
                const sure = await shared.confirmed(
                    'Restoring replaces the configuration and every group and per user setting on this server. '
                    + 'What is there now is not kept. Continue?');
                if (!sure) return;

                restoreFile().value = '';
                restoreFile().click();
            })

            shared.keyedEventListener(restoreFile(), 'change', async function () {
                const file = restoreFile().files?.[0];
                if (!file) return;

                said().textContent = 'Restoring…';
                try {
                    const report = await window.ApiClient.ajax({
                        type: 'POST',
                        url: window.ApiClient.getUrl('streamyfin/v1/backup'),
                        contentType: 'application/json',
                        data: await file.text(),
                    }).then((response) => response.json());

                    // Read before the page is reloaded, since the tabs hold what they
                    // read at load and a reload on the same turn paints nothing.
                    said().textContent = `${restored(report)} Reload the page to see it.`;
                } catch (rejected) {
                    console.error(rejected);
                    const response = typeof rejected?.text === 'function' ? rejected : rejected?.response;
                    const body = await response?.text?.().catch(() => null);
                    let problem = null;
                    try { problem = JSON.parse(body ?? '').problem; } catch { /* not ours */ }
                    said().textContent = problem ?? 'That file could not be restored.';
                }
            })
        })
    });
}