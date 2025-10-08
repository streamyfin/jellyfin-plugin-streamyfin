const yamlEditor = () => document.getElementById('yaml-editor');
const exampleBtn = () => document.getElementById("example-btn")
const saveBtn = () => document.getElementById("save-btn");

export default function (view, params) {

    // init code here
    view.addEventListener('viewshow', (e) => {
        import(window.ApiClient.getUrl("web/configurationpage?name=shared.js")).then((shared) => {
            shared.setPage("Yaml");
            return shared;
        }).then(async (shared) => {
            // Import monaco after shared resources and wait until its done before continuing
            if (!window.monaco) {
                Dashboard.showLoadingMsg();
                await import(window.ApiClient.getUrl('web/configurationpage?name=monaco-editor.bundle.js'))
            }

            const Page = {
                editor: null,
                yaml: null,
                parentIdProvider: null,
                parentIdSuggestions: null,
                // Fetch libraries and collections from Jellyfin and map to Monaco suggestions
                loadParentIdSuggestions: async function () {
                    try {
                        const userId = await window.ApiClient.getCurrentUserId?.() ?? null;

                        // Build URLs using ApiClient to preserve base path and auth
                        // Prefer user views over raw media folders for broader compatibility
                        const libsUrl = userId
                            ? window.ApiClient.getUrl(`Users/${userId}/Views`)
                            : window.ApiClient.getUrl('Library/MediaFolders');
                        const collectionsUrl = userId
                            ? window.ApiClient.getUrl(`Users/${userId}/Items`, {
                                IncludeItemTypes: 'BoxSet',
                                Recursive: true,
                                SortBy: 'SortName',
                                SortOrder: 'Ascending'
                              })
                            : null;

                        // Fetch in parallel using ApiClient.ajax to include auth
                        const [libsRes, colRes] = await Promise.all([
                            window.ApiClient.ajax({ type: 'GET', url: libsUrl, contentType: 'application/json' }),
                            collectionsUrl
                                ? window.ApiClient.ajax({ type: 'GET', url: collectionsUrl, contentType: 'application/json' })
                                : Promise.resolve(null)
                        ]);

                        const libsJson = libsRes ? libsRes : { Items: [] };
                        const colsJson = colRes ? colRes : { Items: [] };

                        // Normalize arrays (Jellyfin usually returns { Items: [...] })
                        const libraries = Array.isArray(libsJson?.Items) ? libsJson.Items : (Array.isArray(libsJson) ? libsJson : []);
                        const collections = Array.isArray(colsJson?.Items) ? colsJson.Items : (Array.isArray(colsJson) ? colsJson : []);

                        const libSuggestions = libraries
                            .filter(i => i?.Id && i?.Name)
                            .map(i => ({
                                label: `${i.Name} (${i.Id})`,
                                kind: monaco.languages.CompletionItemKind.Value,
                                insertText: i.Id,
                                detail: 'Library folder',
                                documentation: i.Path ? `Path: ${i.Path}` : undefined
                            }));

                        const colSuggestions = collections
                            .filter(i => i?.Id && i?.Name)
                            .map(i => ({
                                label: `${i.Name} (${i.Id})`,
                                kind: monaco.languages.CompletionItemKind.Value,
                                insertText: i.Id,
                                detail: 'Collection',
                                documentation: i.Overview || undefined
                            }));

                        Page.parentIdSuggestions = [...libSuggestions, ...colSuggestions];
                    } catch (e) {
                        console.warn('Failed to load parentId suggestions', e);
                        Page.parentIdSuggestions = [];
                    }
                },
                // Register a YAML completion provider that triggers when value for key 'parentId' is being edited
                registerParentIdProvider: function () {
                    if (Page.parentIdProvider) return; // avoid duplicates

                    Page.parentIdProvider = monaco.languages.registerCompletionItemProvider('yaml', {
                        triggerCharacters: [':', ' ', '-', '\n', '"', "'"],
                        provideCompletionItems: async (model, position) => {
                            try {
                                const line = model.getLineContent(position.lineNumber);
                                const beforeCursor = line.substring(0, position.column - 1);
                                // Heuristic: we're in a value position for a key named 'parentId'
                                // Match lines like: "parentId: |" or "id: |" with optional indent or list dash
                                const isTargetLine = /(^|\s|-)\b(parentId|id)\b\s*:\s*[^#]*$/i.test(beforeCursor);
                                if (!isTargetLine) {
                                    return { suggestions: [] };
                                }

                                if (!Array.isArray(Page.parentIdSuggestions)) {
                                    await Page.loadParentIdSuggestions();
                                }

                                // Compute replacement range: from word start to cursor
                                const word = model.getWordUntilPosition(position);
                                const startColFromColon = (() => {
                                    const idx = beforeCursor.lastIndexOf(':');
                                    if (idx === -1) return word.startColumn;
                                    let start = idx + 1; // first char after colon
                                    // skip spaces
                                    while (beforeCursor.charAt(start) === ' ') start++;
                                    // skip optional opening quotes
                                    while (beforeCursor.charAt(start) === '"' || beforeCursor.charAt(start) === "'") start++;
                                    // Monaco columns are 1-based
                                    return start + 1;
                                })();
                                const range = new monaco.Range(
                                    position.lineNumber,
                                    Math.max(1, startColFromColon),
                                    position.lineNumber,
                                    position.column
                                );

                                const suggestions = Page.parentIdSuggestions.map(s => ({ ...s, range }));
                                return { suggestions };
                            } catch (err) {
                                console.warn('parentId provider error', err);
                                return { suggestions: [] };
                            }
                        }
                    });
                },
                saveConfig: function (e) {
                    e.preventDefault();
                    shared.setYamlConfig(Page.editor.getModel().getValue())
                    shared.saveConfig()
                },
                loadConfig: function (config) {
                    Dashboard.hideLoadingMsg();
                    const yamlModelUri = monaco.Uri.parse('streamyfin.yaml');

                    Page.editor = monaco.editor.create(yamlEditor(), {
                        automaticLayout: true,
                        language: 'yaml',
                        suggest: {
                            showWords: false
                        },
                        model: monaco.editor.createModel(shared.tools.jsYaml.dump(config), 'yaml', yamlModelUri),
                    });

                    Page.editor.onDidChangeModelContent(function (e) {
                        if (e.eol === '\n' && e.changes[0].text.endsWith(" ")) {
                            // need timeout so it triggers after auto formatting
                            setTimeout(() => {
                                Page.editor.trigger('', 'editor.action.triggerSuggest', {});
                            }, "100");
                        }
                    });

                },
                resetConfig: function () {
                    const example = shared.getDefaultConfig();
                    Page.editor.getModel().setValue(shared.tools.jsYaml.dump(example));
                },
                init: function () {
                    console.log("init");

                    // Yaml Editor
                    monaco.editor.setTheme('vs-dark');
                    
                    
                    Page.yaml = monacoYaml.configureMonacoYaml(monaco, {
                        enableSchemaRequest: true,
                        hover: true,
                        completion: true,
                        validate: true,
                        format: true,
                        titleHidden: true,
                        schemas: [
                            {
                                uri: shared.SCHEMA_URL,
                                fileMatch: ["**/*"]
                            },
                        ],
                    });

                    saveBtn().addEventListener("click", Page.saveConfig);
                    exampleBtn().addEventListener("click", Page.resetConfig);

                    // Register dynamic intellisense for parentId values
                    Page.registerParentIdProvider();

                    if (shared.getConfig() && Page.editor == null) {
                        Page.loadConfig(shared.getConfig());
                    }

                    shared.setOnConfigUpdatedListener('yaml-editor', (config) => {
                        // only set if editor isn't instantiated 
                        if (Page.editor == null) {
                            console.log("loading")
                            Page.loadConfig(config)
                        } else {
                            Page.editor.getModel().setValue(shared.tools.jsYaml.dump(config))
                        }
                    })
                }
            };

            if (!Page.editor && monaco?.editor?.getModels?.()?.length === 0) {
                Page.init();
            } else {
                console.log("Monaco editor model already exists")
            }

            view.addEventListener('viewhide', function (e) {
                console.log("Hiding")
                Page?.editor?.dispose()
                Page?.yaml?.dispose()
                Page?.parentIdProvider?.dispose?.()
                Page.editor = undefined;
                Page.yaml = undefined;
                Page.parentIdProvider = undefined;
                monaco?.editor?.getModels?.()?.forEach(model => model.dispose())
                monaco?.editor?.getEditors?.()?.forEach(editor => editor.dispose());
            });
        })
    });
}