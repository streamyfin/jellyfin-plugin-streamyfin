// What a person chose for their own notifications, in one line for the Targeting tab. An
// administrator cannot change it, and reads it when somebody says a notification never came.
//
// Kept apart from the page, like the home editor, so the line is tested without a browser.

// The page names the server's events. Seerr's two are only known on the person's side.
const SEERR_LABELS = {
    seerrRequests: "Seerr requests they made",
    seerrPending: "Seerr requests to approve",
};

const named = (choices, labels) => [
    ...(choices.events ?? []).filter((event) => !event.enabled).map((event) => labels[event.key] ?? event.key),
    ...(choices.libraries ?? []).filter((library) => !library.enabled).map((library) => library.name),
    ...(choices.mutedShows ?? []).map((show) => show.name),
];

// The server keeps a pause after it ends, and it then holds nothing back. One that ends on
// another day names the day, since it can last a week and a time alone reads as today.
const pauseText = (pause, { locale, timeZone, now }) => {
    if (!pause) return null;
    if (!pause.until) return "paused until they turn it back on";

    const end = new Date(pause.until);
    if (end <= now) return null;

    const day = (date) => date.toLocaleDateString(locale, { timeZone });
    const time = { hour: "2-digit", minute: "2-digit", timeZone };
    const when = day(end) === day(now)
        ? end.toLocaleTimeString(locale, time)
        : end.toLocaleString(locale, { weekday: "short", day: "numeric", month: "short", ...time });
    return `paused until ${when}`;
};

export const describeOwnChoices = (choices, labels, { locale = undefined, timeZone = undefined, now = new Date() } = {}) => {
    if (!choices) return null;

    const off = named(choices, { ...SEERR_LABELS, ...labels });
    const parts = [
        pauseText(choices.pause, { locale, timeZone, now }),
        off.length > 0 ? `off for ${off.join(", ")}` : null,
    ].filter(Boolean);

    return parts.length === 0 ? null : `Their own choices: ${parts.join("; ")}.`;
};
