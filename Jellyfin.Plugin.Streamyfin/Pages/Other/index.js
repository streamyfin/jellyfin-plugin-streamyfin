// The dashboard keeps the views it has already shown, so more than one page can carry
// an element with the same id. Everything here is looked up inside this view.
let page = null;

const homePage = () => page.querySelector('#home-page');
const saveBtn = () => page.querySelector('#save-other-btn');
const backupBtn = () => page.querySelector('#backup-btn');
const restoreBtn = () => page.querySelector('#restore-btn');
const restoreFile = () => page.querySelector('#restore-file');
const said = () => page.querySelector('#backup-said');

// The dock only has one thing to save here, so it says so rather than counting.
const edited = () => {
    page.querySelector('#sf-dot').hidden = false;
    page.querySelector('#sf-dock-summary').textContent = 'Start page changed';
    saveBtn().disabled = false;
};

const saved = () => {
    page.querySelector('#sf-dot').hidden = true;
    page.querySelector('#sf-dock-summary').textContent = 'Nothing to save';
    saveBtn().disabled = true;
};

// Says it and colours it in one call, so a caller cannot set one without the other.
const say = (text, tone) => {
    said().textContent = text;
    said().classList.toggle('sf-said--ok', tone === 'ok');
    said().classList.toggle('sf-said--no', tone === 'no');
};

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

export default function (view, params) {

    // init code here
    view.addEventListener('viewshow', (e) => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then(async (shared) => {
            shared.setPage("Other");

            page = view;

            // The dashboard's theme is a user choice and its stylesheet only sets a
            // background, so the page reads that rather than guessing.
            const renderer = await import(window.ApiClient.getUrl("web/configurationpage?name=settings-form.js"));
            renderer.applyTheme(view.querySelector("#sf-app"));

            homePage().options.length = 0;
            shared.StreamyfinTabs().forEach(tab => homePage().add(new Option(tab.name, tab.resource)))

            homePage().value = shared.getConfig()?.other?.homePage;

            shared.setOnConfigUpdatedListener('other', (config) => {
                console.log("updating dom for other")
                const {other} = config;

                homePage().value = other.homePage
            })

            // The value has to reach the configuration, not just the dock: saving writes
            // what the page holds, so a select nobody wrote back saved nothing at all.
            shared.keyedEventListener(homePage(), 'change', function () {
                const config = shared.getConfig() ?? {};
                shared.setConfig({
                    ...config,
                    other: {
                        ...(config.other ?? {}),
                        homePage: homePage().value,
                    },
                });
                edited();
            })

            shared.keyedEventListener(saveBtn(), 'click', function (e) {
                e.preventDefault();
                shared.saveConfig().then((stored) => {
                    if (stored) saved();
                })
            })

            shared.keyedEventListener(backupBtn(), 'click', async function (e) {
                e.preventDefault();
                say('Collecting…');
                try {
                    const backup = await window.ApiClient.ajax({
                        type: 'GET',
                        url: window.ApiClient.getUrl('streamyfin/v1/backup'),
                        contentType: 'application/json',
                    }).then((response) => response.text());

                    try {
                        download(backup, fileName());
                        say('Downloaded.', 'ok');
                    } catch (error) {
                        // The server answered. Saying it did not would send an
                        // administrator to the wrong place.
                        console.error(error);
                        say('The backup could not be saved by this browser.', 'no');
                    }
                } catch (error) {
                    console.error(error);
                    say('The server could not be asked for a backup.', 'no');
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

                say('Restoring…');
                try {
                    const report = await window.ApiClient.ajax({
                        type: 'POST',
                        url: window.ApiClient.getUrl('streamyfin/v1/backup'),
                        contentType: 'application/json',
                        data: await file.text(),
                    }).then((response) => response.json());

                    // Read before the page is reloaded, since the tabs hold what they
                    // read at load and a reload on the same turn paints nothing.
                    say(`${restored(report)} Reload the page to see it.`, 'ok');
                } catch (rejected) {
                    console.error(rejected);
                    const response = typeof rejected?.text === 'function' ? rejected : rejected?.response;
                    const body = await response?.text?.().catch(() => null);
                    let problem = null;
                    try { problem = JSON.parse(body ?? '').problem; } catch { /* not ours */ }
                    say(problem ?? 'That file could not be restored.', 'no');
                }
            })
        })
    });
}