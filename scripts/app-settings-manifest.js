// Writes Jellyfin.Plugin.Streamyfin.Tests/AppSettingsManifest.json from a checkout of
// the app: every setting it reads, its type, its default, the other names it reads that
// setting under, and the values it offers for a setting it picks from a list of its own.
// SettingsParityTests holds the plugin to that file, and docs/rewrite/settings-parity.md
// says why it exists.
//
//     bun scripts/app-settings-manifest.js <path to a streamyfin checkout>
//
// Bun rather than node: the app's settingsOverrides.ts is loaded as it is, TypeScript
// and all. The checkout needs its node_modules, for the values the app takes from a
// package.

const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const SETTINGS = path.join('utils', 'atoms', 'settings.ts');
const OVERRIDES = path.join('utils', 'atoms', 'settingsOverrides.ts');
const MANIFEST = path.join(__dirname, '..', 'Jellyfin.Plugin.Streamyfin.Tests', 'AppSettingsManifest.json');

// Values settings.ts takes from a module of the app that cannot be read as plain data.
// Each is checked against its sources on every run, so an app change stops the run
// instead of leaving a stale value here. Anything else the generator cannot work out
// stops it too: guessing is how the first manifest came to record downloadQuality as
// having no default.
const APP_VALUES = {
    // The app's wrapper picks Expo's enum on a phone and its own on a TV, so the value
    // depends on the platform in the source even though both say 0.
    'ScreenOrientation.OrientationLock.DEFAULT': {
        value: 0,
        verify(appRoot) {
            // The types alone: the package's entry point pulls in React Native.
            const lock = loadPackage('expo-screen-orientation', appRoot, 'build/ScreenOrientation.types.js').OrientationLock;
            if (lock?.DEFAULT !== 0) {
                return 'expo-screen-orientation no longer has OrientationLock.DEFAULT = 0';
            }
            for (const wrapper of ['packages/expo-screen-orientation.ts', 'packages/expo-screen-orientation.tv.ts']) {
                const source = fs.readFileSync(path.join(appRoot, wrapper), 'utf8');
                if (!/OrientationLock\s*\{[^}]*\bDEFAULT\s*=\s*0\b/.test(source)) {
                    return `${wrapper} no longer declares OrientationLock with DEFAULT = 0`;
                }
            }
            return null;
        },
    },
};

// The keys normalizePluginValue reshapes on the way in, so the plugin sends another form
// than the one the app stores. Every run sends that form through the app's own
// normalizePluginValue and stops unless it comes back as the app's default.
const RESHAPED = {
    defaultBitrate: {
        note: 'the app looks the scalar up in BITRATES; null is the Max entry',
        wire: (value) => value.value ?? null,
    },
    maxAutoPlayEpisodeCount: {
        note: 'the app rebuilds { key, value } from the scalar',
        wire: (value) => value.value,
    },
    subtitleSize: {
        note: 'the app divides a value of 10 or more by 100',
        wire: (value) => Math.round(value * 100),
    },
};

// The settings whose value the app picks from a list of its own, where that list is, and
// the screens that offer it for the setting. The plugin offers the same list, which
// SettingsParityTests holds equal to this. Each screen is checked on every run: one that
// stops importing the list, or stops writing the setting, stops the run rather than leave
// the manifest describing a list nothing offers any more.
//
// A list is either { label, value } pairs, or, with `translated`, a record from each value
// to the translation key its label is shown under, read from the app's English strings.
const HERO = path.join('components', 'home', 'HomeHeroCarousel.tsx');
const CHOICES = {
    preferedLanguage: {
        file: 'i18n.ts',
        name: 'APP_LANGUAGES',
        offeredBy: [
            path.join('components', 'settings', 'AppLanguageSelector.tsx'),
            path.join('app', '(auth)', '(tabs)', '(home)', 'settings.tv.tsx'),
        ],
    },
    hiddenHomeHeroSections: { file: HERO, name: 'SECTION_LABEL_KEYS', translated: true, offeredBy: [HERO] },
    hiddenHomeHeroMediaTypes: { file: HERO, name: 'MEDIA_LABEL_KEYS', translated: true, offeredBy: [HERO] },
};

/** The app's English string for a translation key such as "home.next_up". */
function english(appRoot, key) {
    const strings = JSON.parse(fs.readFileSync(path.join(appRoot, 'translations', 'en.json'), 'utf8'));
    const text = key.split('.').reduce((node, part) => (isPlainObject(node) ? node[part] : undefined), strings);
    if (typeof text !== 'string' || text === '') {
        throw new Unreadable(`translations/en.json has no string for ${key}`);
    }
    return text;
}

// A default the app picks by platform has no single value to declare.
const PLATFORM = Symbol('platform');
// No default: the plugin proposes nothing and the app keeps whatever it holds.
const NONE = Symbol('none');

class Unreadable extends Error {}

/**
 * One module of the app, read rather than run: its constants, enums and imports.
 */
function readModule(file, source) {
    const parsed = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true,
        file.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
    const module = { file: parsed, path: file, consts: new Map(), enums: new Map(), imports: new Map(), declarations: new Map() };

    for (const statement of parsed.statements) {
        if (ts.isTypeAliasDeclaration(statement)) {
            module.declarations.set(statement.name.text, statement);
        } else if (ts.isEnumDeclaration(statement)) {
            module.enums.set(statement.name.text, enumValues(statement, parsed));
        } else if (ts.isVariableStatement(statement)) {
            for (const declaration of statement.declarationList.declarations) {
                if (ts.isIdentifier(declaration.name) && declaration.initializer) {
                    module.consts.set(declaration.name.text, declaration.initializer);
                    module.declarations.set(declaration.name.text, statement);
                }
            }
        } else if (ts.isImportDeclaration(statement)) {
            recordImports(statement, module.imports);
        }
    }
    return module;
}

/**
 * Every setting in the app's Settings type, with its type and its default.
 *
 * @param {string} source The text of utils/atoms/settings.ts.
 * @param {string} appRoot The checkout, where an imported module is looked up.
 * @returns {Array<object>} The manifest entries, sorted by key, without wire names.
 */
function readSettings(source, appRoot) {
    const module = readModule(path.join(appRoot, SETTINGS), source);
    const { file } = module;

    const shape = module.declarations.get('Settings')?.type;
    if (!shape || !ts.isTypeLiteralNode(shape)) {
        throw new Unreadable(`${SETTINGS} has no "type Settings = { ... }" to read`);
    }

    const defaults = module.consts.get('defaultValues');
    if (!defaults || !ts.isObjectLiteralExpression(defaults)) {
        throw new Unreadable(`${SETTINGS} has no "defaultValues" object to read`);
    }

    const written = new Map();
    for (const property of defaults.properties) {
        if (!ts.isPropertyAssignment(property)) {
            throw new Unreadable(`defaultValues: cannot read "${property.getText(file)}"`);
        }
        written.set(propertyName(property.name, file), property.initializer);
    }

    const scope = { module, appRoot, verified: new Set() };
    const values = new Map();

    for (const member of shape.members) {
        if (!ts.isPropertySignature(member) || !member.type) {
            throw new Unreadable(`Settings: cannot read "${member.getText(file)}"`);
        }

        const key = propertyName(member.name, file);
        const initializer = written.get(key);
        let value;
        try {
            value = initializer ? evaluate(initializer, scope) : NONE;
            plainJson(value === PLATFORM || value === NONE ? null : value);
        } catch (error) {
            throw new Unreadable(`the default of ${key}: ${error.message}`);
        }
        values.set(key, { type: member.type.getText(file).replace(/\s+/g, ' '), value });
    }

    const normalize = normalizer(scope, values);
    return [...values]
        .map(([key, { type, value }]) => entry(key, type, value, normalize))
        .sort((a, b) => (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
}

/** One manifest entry: the default as the app holds it, and the form the plugin sends. */
function entry(key, type, value, normalize) {
    // null reads as "nothing chosen", which is what the first manifest recorded for
    // home and the two language keys, so it is no default rather than a default of null.
    const none = value === NONE || value === null || value === undefined;
    const platform = value === PLATFORM;
    const reshaped = RESHAPED[key];

    if (!reshaped && isKeyValue(value)) {
        throw new Unreadable(
            `${key} defaults to { key, value }, which normalizePluginValue rebuilds from a `
            + 'scalar: add it to RESHAPED with the form the plugin has to send');
    }

    let wireDefault = null;
    if (reshaped && !none && !platform) {
        wireDefault = reshaped.wire(value);
        // The plugin's serializer leaves a null value out, so the app is handed nothing.
        const sent = wireDefault === null ? undefined : wireDefault;
        const back = normalize(key, sent);
        if (JSON.stringify(back) !== JSON.stringify(value)) {
            throw new Unreadable(
                `${key}: the plugin would send ${JSON.stringify(sent) ?? 'nothing'}, which `
                + `normalizePluginValue turns into ${JSON.stringify(back) ?? 'nothing'}, not the `
                + `app's default ${JSON.stringify(value)}: RESHAPED is out of date`);
        }
    }

    return {
        key,
        type,
        default: none || platform ? null : value,
        hasDefault: !none && !platform,
        noDefaultReason: platform ? 'platform' : none ? 'none' : null,
        wireDefault,
        wireNote: reshaped ? reshaped.note : null,
    };
}

/** A `{ key, value }` object, which normalizePluginValue rebuilds from a scalar. */
const isKeyValue = (value) =>
    isPlainObject(value) && hasOwn(value, 'key') && hasOwn(value, 'value');

/** An object literal, as opposed to an array, a class instance or a module. */
const isPlainObject = (value) =>
    typeof value === 'object' && value !== null && !Array.isArray(value)
    && [Object.prototype, null].includes(Object.getPrototypeOf(value));

/** Whether a step is the holder's own, so an inherited method is never read as a value. */
const hasOwn = (holder, step) => Object.prototype.hasOwnProperty.call(Object(holder), step);

/**
 * What a default may be: what JSON carries, an undefined field of an object aside, which
 * JSON leaves out just as the app's own JSON.stringify does.
 */
function plainJson(value, where = 'it') {
    if (value === null || typeof value === 'boolean' || typeof value === 'string') return;
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) throw new Unreadable(`${where} is ${value}, which JSON cannot carry`);
        return;
    }
    if (Array.isArray(value)) {
        value.forEach((item, index) => plainJson(item, `${where}[${index}]`));
        return;
    }
    if (isPlainObject(value)) {
        for (const [field, item] of Object.entries(value)) {
            if (item !== undefined) plainJson(item, `${where}.${field}`);
        }
        return;
    }
    throw new Unreadable(`${where} is a ${typeof value}, not a value JSON can carry`);
}

/**
 * The app's normalizePluginValue, run here: its source from settings.ts, given the
 * defaults just read, BITRATES from the app's own module, and nothing else.
 */
function normalizer(scope, values) {
    const { module } = scope;
    const declaration = module.declarations.get('normalizePluginValue');
    if (!declaration) {
        return () => {
            throw new Unreadable(`${SETTINGS} has no normalizePluginValue to check RESHAPED against`);
        };
    }

    const source = ['isFiniteNumber', 'normalizePluginValue']
        .map((name) => module.declarations.get(name))
        .filter(Boolean)
        .map((statement) => statement.getText(module.file).replace(/^export\s+/, ''))
        .join('\n');
    const javascript = ts.transpileModule(source, {
        compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.None },
    }).outputText;

    // Only these three names exist for it, so anything else it reaches for fails here, by
    // name, rather than quietly reading something else. BITRATES is read the first time
    // it is asked for.
    const context = {
        defaultValues: Object.fromEntries([...values].map(([name, { value: held }]) =>
            [name, held === NONE || held === PLATFORM ? undefined : held])),
        t: (text) => text,
    };
    let bitrates;
    Object.defineProperty(context, 'BITRATES', {
        enumerable: true,
        get: () => (bitrates ??= imported('BITRATES', [], scope)),
    });

    let run;
    return (key, value) => {
        try {
            run ??= vm.runInNewContext(`${javascript}\nnormalizePluginValue;`, context);
            return run(key, value);
        } catch (error) {
            if (error instanceof Unreadable) throw error;
            throw new Unreadable(`normalizePluginValue could not be run here: ${error.message}`);
        }
    };
}

/**
 * What one expression of a module comes to, as JSON would carry it.
 *
 * Literals, arrays and objects of them, the module's own constants and enums, a value
 * imported from another module of the app or from a package, and APP_VALUES. Anything
 * else throws, and so does anything that reads Platform, which is reported as PLATFORM
 * instead of a value.
 */
function evaluate(node, scope) {
    const { file } = scope.module;

    const known = APP_VALUES[node.getText(file)];
    if (known) {
        if (!scope.verified.has(known)) {
            const changed = known.verify(scope.appRoot);
            if (changed) {
                throw new Unreadable(`APP_VALUES has "${node.getText(file)}" as ${known.value}, but ${changed}`);
            }
            scope.verified.add(known);
        }
        return known.value;
    }

    if (mentions(node, 'Platform')) {
        return PLATFORM;
    }

    switch (node.kind) {
        case ts.SyntaxKind.TrueKeyword:
            return true;
        case ts.SyntaxKind.FalseKeyword:
            return false;
        case ts.SyntaxKind.NullKeyword:
            return null;
        case ts.SyntaxKind.NumericLiteral:
            return Number(node.text);
        case ts.SyntaxKind.StringLiteral:
        case ts.SyntaxKind.NoSubstitutionTemplateLiteral:
            return node.text;
        default:
            break;
    }

    if (ts.isPrefixUnaryExpression(node) && node.operator === ts.SyntaxKind.MinusToken) {
        const operand = evaluate(node.operand, scope);
        if (typeof operand !== 'number') {
            throw new Unreadable(`cannot read "${node.getText(file)}"`);
        }
        return -operand;
    }
    if (ts.isParenthesizedExpression(node) || ts.isAsExpression(node)
        || ts.isSatisfiesExpression(node) || ts.isNonNullExpression(node)) {
        return evaluate(node.expression, scope);
    }
    if (ts.isArrayLiteralExpression(node)) {
        return node.elements.map((element) => json(evaluate(element, scope)));
    }
    if (ts.isObjectLiteralExpression(node)) {
        const object = {};
        for (const property of node.properties) {
            if (ts.isPropertyAssignment(property)) {
                object[propertyName(property.name, file)] = json(evaluate(property.initializer, scope));
            } else if (ts.isShorthandPropertyAssignment(property)) {
                object[property.name.text] = json(evaluate(property.name, scope));
            } else {
                throw new Unreadable(`cannot read "${property.getText(file)}"`);
            }
        }
        return object;
    }
    if (ts.isIdentifier(node)) {
        if (node.text === 'undefined') {
            return NONE;
        }
        if (scope.module.consts.has(node.text)) {
            return evaluate(scope.module.consts.get(node.text), scope);
        }
        if (scope.module.enums.has(node.text)) {
            return Object.fromEntries(scope.module.enums.get(node.text));
        }
        return imported(node.text, [], scope);
    }
    if (ts.isPropertyAccessExpression(node) && ts.isIdentifier(node.expression)
        && scope.module.enums.has(node.expression.text)) {
        const members = scope.module.enums.get(node.expression.text);
        if (!members.has(node.name.text)) {
            throw new Unreadable(`${node.expression.text} has no member ${node.name.text}`);
        }
        return members.get(node.name.text);
    }
    if (ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) {
        const chain = accessChain(node);
        if (chain && !scope.module.consts.has(chain.root) && scope.module.imports.has(chain.root)) {
            return imported(chain.root, chain.path, scope);
        }

        const holder = evaluate(node.expression, scope);
        const step = ts.isPropertyAccessExpression(node)
            ? node.name.text
            : evaluate(node.argumentExpression, scope);
        if (typeof holder !== 'object' || holder === null || !hasOwn(holder, step)) {
            throw new Unreadable(`cannot read "${node.getText(file)}"`);
        }
        return holder[step];
    }

    return runPure(node, scope);
}

// What an expression built only from literals may call on. A call like BITRATES's
// `[...].sort(...)` is run rather than read; anything reaching further is refused.
const PURE_GLOBALS = { Number, Math, String, Boolean, Array, Object, JSON };

/**
 * An expression that names nothing but its own parameters and PURE_GLOBALS, run in a
 * context holding those alone, with a time limit. Anything naming more cannot be read.
 */
function runPure(node, scope) {
    const text = node.getText(scope.module.file);
    const free = [...freeIdentifiers(node)].filter((name) => !Object.hasOwn(PURE_GLOBALS, name)
        && !['undefined', 'Infinity', 'NaN'].includes(name));
    if (free.length > 0) {
        throw new Unreadable(`cannot read "${text}"`);
    }

    const javascript = ts.transpileModule(`(${text})`, {
        compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.None },
    }).outputText;
    let result;
    try {
        result = vm.runInNewContext(javascript, { ...PURE_GLOBALS }, { timeout: 1000 });
    } catch (error) {
        throw new Unreadable(`running "${text}" failed: ${error.message}`);
    }
    // A copy made here, so what comes back is an ordinary value of this script, and
    // anything JSON cannot carry fails now.
    try {
        return structuredClone(result);
    } catch {
        throw new Unreadable(`"${text}" does not come to a value JSON can carry`);
    }
}

/** The names an expression reads that it does not bind itself. */
function freeIdentifiers(node) {
    const bound = new Set();
    const used = new Set();
    const bind = (name) => {
        if (ts.isIdentifier(name)) {
            bound.add(name.text);
        } else {
            ts.forEachChild(name, (child) => ts.isBindingElement(child) && bind(child.name));
        }
    };
    const visit = (child) => {
        if (ts.isParameter(child) || ts.isVariableDeclaration(child)) {
            bind(child.name);
        }
        if (ts.isIdentifier(child)) {
            const { parent } = child;
            const isName = (ts.isPropertyAccessExpression(parent) && parent.name === child)
                || (ts.isPropertyAssignment(parent) && parent.name === child)
                || ((ts.isParameter(parent) || ts.isVariableDeclaration(parent) || ts.isBindingElement(parent))
                    && (parent.name === child || parent.propertyName === child));
            if (!isName) {
                used.add(child.text);
            }
        }
        ts.forEachChild(child, visit);
    };
    visit(node);
    return new Set([...used].filter((name) => !bound.has(name)));
}

/** A value placed inside an array or an object, where a missing one is a hole, not "no default". */
const json = (value) => {
    if (value === PLATFORM) {
        throw new Unreadable('a value that depends on the platform, inside another value');
    }
    return value === NONE ? undefined : value;
};

/**
 * A value one module imports: read from the source when it comes from the app, loaded
 * when it comes from a package.
 */
function imported(name, steps, scope) {
    const source = scope.module.imports.get(name);
    if (!source) {
        throw new Unreadable(`"${name}" is neither declared in ${path.basename(scope.module.path)} nor imported`);
    }
    const shown = [name, ...steps].join('.');

    let value;
    let rest = [...steps];
    if (source.module.startsWith('@/') || source.module.startsWith('.')) {
        const other = appModule(source.module, scope);
        const exported = source.namespace ? rest.shift() : source.name;
        const inner = { ...scope, module: other };
        if (other.consts.has(exported)) {
            value = evaluate(other.consts.get(exported), inner);
        } else if (other.enums.has(exported)) {
            value = Object.fromEntries(other.enums.get(exported));
        } else {
            throw new Unreadable(`${source.module} has no "${exported}" this script can read, for ${shown}`);
        }
        if (value === PLATFORM) {
            throw new Unreadable(`${shown} depends on the platform in ${source.module}: read it there and add it to APP_VALUES`);
        }
    } else {
        value = loadPackage(source.module, scope.appRoot);
        if (!source.namespace) {
            rest.unshift(source.name);
        }
    }

    for (const step of rest) {
        if (value === null || value === undefined || !hasOwn(value, step)) {
            throw new Unreadable(`${source.module} has no ${shown}`);
        }
        value = value[step];
    }
    return value;
}

/** A module of the app, found the way the app's own `@/` and relative imports resolve. */
function appModule(specifier, scope) {
    const base = specifier.startsWith('@/')
        ? path.join(scope.appRoot, specifier.slice(2))
        : path.resolve(path.dirname(scope.module.path), specifier);
    for (const candidate of [`${base}.ts`, `${base}.tsx`, path.join(base, 'index.ts'), path.join(base, 'index.tsx')]) {
        if (fs.existsSync(candidate)) {
            return readModule(candidate, fs.readFileSync(candidate, 'utf8'));
        }
    }
    throw new Unreadable(`cannot find ${specifier} in the checkout`);
}

/** A package from the checkout's node_modules, or one file of it when its entry point pulls in React Native. */
function loadPackage(name, appRoot, file) {
    try {
        if (file) {
            const manifest = require.resolve(`${name}/package.json`, { paths: [appRoot] });
            return require(path.join(path.dirname(manifest), file));
        }
        return require(require.resolve(name, { paths: [appRoot] }));
    } catch (error) {
        throw new Unreadable(`cannot load ${name} from the checkout: ${error.message}`);
    }
}

/**
 * `A.b.c` or `A.b[0]` as its root identifier and the steps after it, or null when the
 * chain goes through anything else.
 */
function accessChain(node) {
    const steps = [];
    let current = node;
    while (ts.isPropertyAccessExpression(current) || ts.isElementAccessExpression(current)) {
        if (ts.isPropertyAccessExpression(current)) {
            steps.unshift(current.name.text);
        } else if (ts.isNumericLiteral(current.argumentExpression) || ts.isStringLiteral(current.argumentExpression)) {
            steps.unshift(current.argumentExpression.text);
        } else {
            return null;
        }
        current = current.expression;
    }
    return ts.isIdentifier(current) ? { root: current.text, path: steps } : null;
}

/** The values one import declaration brings in, by local name. Type-only imports bring none. */
function recordImports(statement, imports) {
    const clause = statement.importClause;
    if (!clause || clause.isTypeOnly) {
        return;
    }
    const module = statement.moduleSpecifier.text;
    if (clause.name) {
        imports.set(clause.name.text, { module, name: 'default' });
    }
    if (clause.namedBindings && ts.isNamespaceImport(clause.namedBindings)) {
        imports.set(clause.namedBindings.name.text, { module, namespace: true });
    } else if (clause.namedBindings) {
        for (const element of clause.namedBindings.elements) {
            if (!element.isTypeOnly) {
                imports.set(element.name.text, { module, name: (element.propertyName ?? element.name).text });
            }
        }
    }
}

/**
 * An enum's members and values, in TypeScript's own numbering: a member without an
 * initializer is one more than the member before it, and the first is 0.
 */
function enumValues(declaration, file) {
    const members = new Map();
    let next = 0;
    for (const member of declaration.members) {
        let value;
        if (!member.initializer) {
            value = next;
        } else if (ts.isStringLiteral(member.initializer)) {
            value = member.initializer.text;
        } else if (ts.isNumericLiteral(member.initializer)) {
            value = Number(member.initializer.text);
        } else if (ts.isPrefixUnaryExpression(member.initializer)
            && member.initializer.operator === ts.SyntaxKind.MinusToken
            && ts.isNumericLiteral(member.initializer.operand)) {
            value = -Number(member.initializer.operand.text);
        } else {
            throw new Unreadable(`cannot read the enum member "${member.getText(file)}"`);
        }
        members.set(propertyName(member.name, file), value);
        next = typeof value === 'number' ? value + 1 : NaN;
    }
    return members;
}

/** The text of a property or member name, which may be written quoted. */
function propertyName(name, file) {
    if (ts.isIdentifier(name) || ts.isStringLiteral(name) || ts.isNumericLiteral(name)) {
        return name.text;
    }
    throw new Unreadable(`cannot read the name "${name.getText(file)}"`);
}

/** Whether an expression names an identifier anywhere inside it. */
function mentions(node, identifier) {
    let found = false;
    const visit = (child) => {
        if (found) return;
        if (ts.isIdentifier(child) && child.text === identifier) {
            found = true;
            return;
        }
        ts.forEachChild(child, visit);
    };
    visit(node);
    return found;
}

/**
 * The names other than its own that the app reads each setting under, found by running
 * the app's own readIntegrationBlocks rather than by reading it.
 *
 * Two kinds today, both for Seerr: the flat keys still spelt jellyseerr, which every
 * earlier app reads and the plugin still sends, and the seerr block. The old names tried
 * are the ones the app lists in LEGACY_SEERR_SETTINGS and the ones the manifest had
 * before, so a renamed export does not drop them in silence. A name the app no longer
 * reads is not found, leaves the manifest, and SettingsParityTests then fails on the
 * plugin still declaring it.
 *
 * @param {object} overrides The app's utils/atoms/settingsOverrides.ts, loaded.
 * @param {string[]} before The flat names the manifest listed until now.
 * @returns {Map<string, string[]>} Setting key to its other names, sorted.
 */
function readWireNames(overrides, before = []) {
    const { LEGACY_SEERR_SETTINGS: legacy, readIntegrationBlocks: read } = overrides;
    if (typeof read !== 'function') {
        throw new Unreadable(
            `${OVERRIDES} has no readIntegrationBlocks. If the app has stopped reading the `
            + 'seerr block, this script has to be changed to say so; it does not assume it');
    }

    const names = new Map();
    const add = (key, name) => names.set(key, [...new Set([...(names.get(key) ?? []), name])].sort());

    const listed = (legacy ?? []).map(([old]) => old);
    for (const old of new Set([...listed, ...before])) {
        // Handed on under its own name is not read: the name feeds nothing.
        const probe = `probe:${old}`;
        const keys = Object.entries(read({ [old]: { value: probe } }) ?? {})
            .filter(([key, setting]) => key !== old && setting?.value === probe)
            .map(([key]) => key);
        if (keys.length > 1 || (keys.length === 0 && listed.includes(old))) {
            throw new Unreadable(`readIntegrationBlocks fills ${keys.length} settings from ${old}, not one`);
        }
        if (keys.length === 1) {
            add(keys[0], old);
        }
    }

    // The block answers whichever field the app asks it for, with the field's own name,
    // so what comes back says which setting each field fills. Asking for its list of
    // fields instead has no answer here, and stops the run.
    const block = new Proxy({}, {
        get: (_, field) => (typeof field === 'string' ? { value: `seerr.${field}` } : undefined),
        has: (_, field) => typeof field === 'string',
        getOwnPropertyDescriptor: (_, field) => (typeof field === 'string'
            ? { value: { value: `seerr.${field}` }, enumerable: true, configurable: true, writable: true }
            : undefined),
        ownKeys: () => {
            throw new Unreadable(
                'readIntegrationBlocks lists the fields of the seerr block, and this script can '
                + 'only see the fields it asks for by name: read the function and extend the probe');
        },
    });
    let fromBlock = 0;
    for (const [key, setting] of Object.entries(read({ seerr: block }) ?? {})) {
        if (typeof setting?.value === 'string' && setting.value.startsWith('seerr.')) {
            add(key, setting.value);
            fromBlock += 1;
        }
    }
    if (fromBlock === 0) {
        throw new Unreadable('readIntegrationBlocks no longer reads a seerr block the way this script asks it to');
    }

    return names;
}

/**
 * The values the app offers for each setting it picks from a list, read from the list's
 * source the way a default is.
 *
 * Sorted by value. The app sorts its list by label in the collation of the device's own
 * language, so there is no one order to record, and a manifest written on a machine set
 * to another language would otherwise differ from this one by its order alone.
 *
 * @param {string} appRoot The checkout.
 * @param {Set<string>} keys The settings the app has. One in CHOICES the app no longer
 *     has is skipped: the plugin still declares it, and SettingsParityTests fails on that.
 * @returns {Map<string, Array<{value: string, label: string}>>} Setting key to its values.
 */
function readChoices(appRoot, keys) {
    const choices = new Map();

    for (const [key, { file, name, offeredBy, translated }] of Object.entries(CHOICES)) {
        if (!keys.has(key)) {
            continue;
        }

        const specifier = `@/${file.replace(/\.tsx?$/, '')}`;
        for (const screen of offeredBy) {
            const where = path.join(appRoot, screen);
            if (!fs.existsSync(where)) {
                throw new Unreadable(`${screen}, which CHOICES says offers ${name} for ${key}, is not in the checkout`);
            }
            const source = fs.readFileSync(where, 'utf8');
            const brought = readModule(where, source).imports.get(name);
            // A screen that defines the list itself has nothing to import it from.
            if (screen !== file && (brought?.module !== specifier || brought.name !== name)) {
                throw new Unreadable(`${screen} no longer imports ${name} from ${specifier}, so it may not offer it for ${key}`);
            }
            if (!new RegExp(`\\b${key}\\s*:`).test(source)) {
                throw new Unreadable(`${screen} no longer writes ${key}, so it may not offer ${name} for it`);
            }
        }

        const where = path.join(appRoot, file);
        const module = readModule(where, fs.readFileSync(where, 'utf8'));
        if (!module.consts.has(name)) {
            throw new Unreadable(`${file} has no "${name}" to read the values of ${key} from`);
        }
        let list;
        try {
            list = evaluate(module.consts.get(name), { module, appRoot, verified: new Set() });
        } catch (error) {
            throw error instanceof Unreadable ? new Unreadable(`${name} in ${file}: ${error.message}`) : error;
        }
        if (translated) {
            if (!isPlainObject(list)) {
                throw new Unreadable(`${name} in ${file} is not a record of translation keys`);
            }
            list = Object.entries(list).map(([value, label]) => ({ value, label: english(appRoot, String(label)) }));
        }
        choices.set(key, choiceList(list, `${name} in ${file}`));
    }

    return choices;
}

/** A list of `{ label, value }` pairs of strings, each value once, sorted by value. */
function choiceList(list, where) {
    if (!Array.isArray(list) || list.length === 0) {
        throw new Unreadable(`${where} is not a list of choices`);
    }

    const seen = new Set();
    return list
        .map((item, index) => {
            if (!isPlainObject(item) || typeof item.value !== 'string' || item.value === ''
                || typeof item.label !== 'string' || item.label === '') {
                throw new Unreadable(`${where}[${index}] is not a { label, value } pair of strings`);
            }
            if (seen.has(item.value)) {
                throw new Unreadable(`${where} offers ${item.value} twice`);
            }
            seen.add(item.value);
            return { value: item.value, label: item.label };
        })
        .sort((a, b) => (a.value < b.value ? -1 : a.value > b.value ? 1 : 0));
}

/**
 * The manifest for one checkout of the app.
 *
 * @param {string} appRoot The checkout.
 * @param {object} [options] `before`: the manifest until now, whose old names are tried.
 * @returns {Array<object>} The entries, in the order they are written.
 */
function buildManifest(appRoot, { before = [] } = {}) {
    const entries = readSettings(fs.readFileSync(path.join(appRoot, SETTINGS), 'utf8'), appRoot);
    const oldNames = before.flatMap((one) => one.wireNames ?? []).filter((name) => !name.includes('.'));
    const wireNames = readWireNames(require(path.join(appRoot, OVERRIDES)), oldNames);

    const keys = new Set(entries.map((one) => one.key));
    for (const key of wireNames.keys()) {
        if (!keys.has(key)) {
            throw new Unreadable(`readIntegrationBlocks fills ${key}, which Settings does not have`);
        }
    }
    const choices = readChoices(appRoot, keys);

    return entries.map((one) => ({
        ...one,
        ...(wireNames.has(one.key) ? { wireNames: wireNames.get(one.key) } : {}),
        ...(choices.has(one.key) ? { options: choices.get(one.key) } : {}),
    }));
}

if (require.main === module) {
    const appRoot = process.argv[2];
    if (!appRoot) {
        console.error('Usage: bun scripts/app-settings-manifest.js <path to a streamyfin checkout>');
        process.exit(2);
    }

    const before = fs.existsSync(MANIFEST) ? JSON.parse(fs.readFileSync(MANIFEST, 'utf8')) : [];
    const entries = buildManifest(path.resolve(appRoot), { before });
    fs.writeFileSync(MANIFEST, `${JSON.stringify(entries, null, 2)}\n`);

    const was = new Map(before.map((one) => [one.key, JSON.stringify(one)]));
    const now = new Map(entries.map((one) => [one.key, JSON.stringify(one)]));
    for (const [key] of now) if (!was.has(key)) console.log(`+ ${key}`);
    for (const [key] of was) if (!now.has(key)) console.log(`- ${key}`);
    for (const [key, text] of now) if (was.has(key) && was.get(key) !== text) console.log(`~ ${key}`);
    console.log(`${entries.length} settings written to ${path.relative(process.cwd(), MANIFEST)}`);
}

module.exports = { buildManifest, readChoices, readSettings, readWireNames, Unreadable };
