// The list of sentences an administrator writes differently (#34).
//
// Kept apart from the page for the same reason the home editor is: what the rows mean is
// worth testing without a browser, and the page is then only the part that draws them.
//
// A wording is {key, locale?, text}. The key says which sentence it replaces, the locale
// which language it is for, and leaving the locale out means every language. The text
// keeps the placeholders the sentence has, and nothing new: this plugin consolidates its
// events, so there is less to interpolate than a webhook has, which is exactly the
// pushback the issue got.

/** How many things a wording asks to be given, counted from one. */
export const asks = (text) => {
    if (!text) return 0;

    let highest = 0;

    for (const match of String(text).matchAll(/\{(\d+)[^}]*\}/g)) {
        highest = Math.max(highest, Number(match[1]) + 1);
    }

    return highest;
};

/** The sentence a row is about, or undefined when the server has no such thing. */
export const sentenceOf = (sentences, key) => sentences.find((one) => one.key === key);

/** A new row for a sentence, starting from what that sentence says today. */
export const blank = (sentences, key) => {
    const sentence = sentenceOf(sentences, key);
    return { key, locale: "", text: sentence?.text ?? "" };
};

/** Adds a row, unless that sentence and language are already listed. */
export const add = (list, sentences, key) => {
    const row = blank(sentences, key);

    return list.some((one) => one.key === row.key && (one.locale ?? "") === row.locale)
        ? list
        : [...list, row];
};

export const remove = (list, index) => list.filter((_, at) => at !== index);

export const change = (list, index, patch) =>
    list.map((one, at) => (at === index ? { ...one, ...patch } : one));

/**
 * What is wrong with each row, by index. The server refuses both of these, and saying so
 * here means finding out before the save rather than after it.
 */
export const problems = (list, sentences) => {
    const found = new Map();

    list.forEach((one, at) => {
        const sentence = sentenceOf(sentences, one.key);

        if (!sentence) {
            found.set(at, "This server does not write that sentence.");
            return;
        }

        if (!String(one.text ?? "").trim()) {
            found.set(at, "Empty, so the plugin's own wording is used.");
            return;
        }

        const wanted = asks(one.text);

        if (wanted > sentence.placeholders) {
            found.set(at, sentence.placeholders === 0
                ? "That sentence names nothing, so it has no placeholders."
                : `That sentence names ${sentence.placeholders} thing(s), so {${wanted - 1}} has nothing to show.`);
        }

        // Two rows for the same sentence and language: the second never wins, so it is
        // worth saying rather than leaving somebody editing the one that does nothing.
        const same = list.findIndex((other) => other.key === one.key && (other.locale ?? "") === (one.locale ?? ""));

        if (same !== at) {
            found.set(at, "Another row already covers that sentence and language.");
        }
    });

    return found;
};

/**
 * What to store: the rows worth keeping, trimmed, and one per sentence and language. The
 * resolver takes the first match, so a second row for the same pair is a row nobody can
 * make do anything.
 */
export const toConfig = (list) => {
    const kept = new Map();

    for (const one of list) {
        if (!one.key || !String(one.text ?? "").trim()) continue;

        const locale = String(one.locale ?? "").trim();
        const at = `${one.key}\u0000${locale}`;

        if (kept.has(at)) continue;

        kept.set(at, locale ? { key: one.key, locale, text: one.text } : { key: one.key, text: one.text });
    }

    return [...kept.values()];
};

export const summarise = (list) => {
    const rows = toConfig(list).length;

    if (rows === 0) return "The plugin's own wording";

    return rows === 1 ? "1 sentence written differently" : `${rows} sentences written differently`;
};
