export default function (view) {
    const SCHEMA_URL = window.ApiClient.getUrl('streamyfin/config/schema');
    const YAML_URL = window.ApiClient.getUrl('streamyfin/config/yaml')
    
    const yamlEditorEl = document.getElementById('yaml-editor');
    const jsonEditorEl = document.getElementById('json-editor');
    
    const Streamyfin = {
        pluginId: "1e9e5d38-6e67-4615-8719-e98a5c34f004",
        btnSave: document.querySelector("#saveConfig"),
        switchBtn: document.getElementById("switch-btn"),
        resetBtn: document.getElementById("reset-btn"),
        editor: null,
        jsonEditor: null,
        isYamlEditorActive: () => yamlEditorEl.style.display === 'block',
        toggleEditor: () => {
            if (Streamyfin.isYamlEditorActive()) {
                const yamlString = Streamyfin.editor.getModel().getValue();

                Streamyfin.jsonEditor.setValue(window.jsYaml.load(yamlString));
                yamlEditorEl.style.display = 'none';
                jsonEditorEl.style.display = 'block';
            }
            else {
                const json = Streamyfin.jsonEditor.getValue();

                Streamyfin.editor.getModel().setValue(window.jsYaml.dump(json));
                yamlEditorEl.style.display = 'block';
                jsonEditorEl.style.display = 'none';
            }
        },
        saveConfig: function (e) {
            e.preventDefault();
            Dashboard.showLoadingMsg();
            const data = JSON.stringify({
                Value: Streamyfin.isYamlEditorActive() 
                    ? Streamyfin.editor.getModel().getValue()
                    : window.jsYaml.dump(Streamyfin.jsonEditor.getValue())
            });

            window.ApiClient.ajax({type: 'POST', url: YAML_URL, data, contentType: 'application/json'})
                .then(async (response) => {
                    const {Error, Message} = await response.json();

                    if (Error) {
                        Dashboard.hideLoadingMsg();
                        Dashboard.alert(Message);
                    } else {
                        Dashboard.processPluginConfigurationUpdateResult();
                    }
                })
                .catch((error) => console.error(error))
                .finally(Dashboard.hideLoadingMsg);
        },
        loadConfig: function () {
            Dashboard.showLoadingMsg();
            const url = window.ApiClient.getUrl('streamyfin/config/yaml');
            
            window.ApiClient.ajax({ type: 'GET', url, contentType: 'application/json'})
                .then(async function (response) {
                    const { Value } = await response.json();
                    const yamlModelUri = monaco.Uri.parse('streamyfin.yaml');

                    Streamyfin.editor = monaco.editor.create(yamlEditorEl, {
                        automaticLayout: true,
                        language: 'yaml',
                        suggest: {
                            showWords: false
                        },
                        model: monaco.editor.createModel(Value, 'yaml', yamlModelUri),
                    });

                    Streamyfin.editor.onDidChangeModelContent(function (e) {
                        if (e.eol === '\n' && e.changes[0].text.endsWith(" ")) {
                            // need timeout so it triggers after auto formatting
                            setTimeout(() => {
                                Streamyfin.editor.trigger('', 'editor.action.triggerSuggest', {});
                            }, "100");
                        }
                    });
                })
                .catch((error) => console.error(error))
                .finally(Dashboard.hideLoadingMsg);
        },
        resetConfig: function () {
            Dashboard.showLoadingMsg();
            const url = window.ApiClient.getUrl('streamyfin/config/default');

            window.ApiClient.ajax({ type: 'GET', url, contentType: 'application/json'})
                .then(async function (response) {
                    const { Value } = await response.json();

                    Streamyfin.editor.getModel().setValue(Value);
                    Streamyfin.jsonEditor.setValue(window.jsYaml.load(Value));
                })
                .catch((error) => console.error(error))
                .finally(Dashboard.hideLoadingMsg);
        },
        init: function () {
            fetch(SCHEMA_URL).then(async (response) => {
                const schema = await response.json();

                // Yaml Editor
                monaco.editor.setTheme('vs-dark');
                monacoYaml.configureMonacoYaml(monaco, {
                    enableSchemaRequest: true,
                    hover: true,
                    completion: true,
                    validate: true,
                    format: true,
                    titleHidden: true,
                    schemas: [
                        {
                            uri: SCHEMA_URL,
                            fileMatch: ["*"],
                            schema
                        },
                    ],
                });

                // Json Editor
                Streamyfin.jsonEditor = new JSONEditor(jsonEditorEl, {
                    schema: schema,
                    disable_edit_json: true,
                    disable_properties: true,
                    disable_collapse: true,
                    no_additional_properties: true,
                    // use_default_values: false
                });
                
                Streamyfin.jsonEditor.on('ready', stylizeFormForJellyfin);
                Streamyfin.jsonEditor.on('change', stylizeFormForJellyfin);
                Streamyfin.jsonEditor.on('add', stylizeFormForJellyfin);
                Streamyfin.jsonEditor.on('switch', stylizeFormForJellyfin);
            })

            console.log("init");
            Streamyfin.loadConfig();
            Streamyfin.btnSave.addEventListener("click", Streamyfin.saveConfig);
            Streamyfin.switchBtn.addEventListener("click", Streamyfin.toggleEditor);
            Streamyfin.resetBtn.addEventListener("click", Streamyfin.resetConfig);
        }
    }

    view.addEventListener("viewshow", function (e) {
        waitForScript();
    });

    function waitForScript() {
        if (typeof monaco === "undefined") {
            setTimeout(waitForScript, 50);
        } else {
            // monaco?.editor?.getModels?.()?.forEach?.(model => model.dispose());
            Streamyfin.init();
        }
    }
    
    function stylizeFormForJellyfin() {
        // Main objects inside config
        document.getElementById('json-editor').childNodes.forEach((child, index) => {
            if (child.className === "je-object__container") {
                child.className = "verticalSection-extrabottompadding"

                child.childNodes.forEach((innerChild, index) => {
                    if (innerChild.className === "je-header je-object__title") {
                        // innerChild.className = "";

                        innerChild.childNodes.forEach((c, index) => {
                            if (c.tagName === "SPAN") {
                                const innerText = c.innerText
                                const header = document.createElement("h2")
                                header.innerText = innerText
                                c.replaceWith(header)
                            }
                        })
                    }
                })
            }
        })

        // Remove panel classes/styling
        document.querySelectorAll(".je-indented-panel")?.forEach?.(panel => {
            panel.className = "";
            panel.setAttribute("style", `
                padding: unset !important;
                margin: unset !important;
                border: unset !important;

                padding-left: 4px !important;
                margin-left: 4px !important;
                border-left: 0.1em solid rgba(255, 255, 255, 0.125) !important;
            `)
        })

        // Field Titles
        document.querySelectorAll(".je-object__title")?.forEach?.(titleContainer => {
            titleContainer.childNodes.forEach((child, index) => {
                if (child.tagName === "SPAN") {
                    child.remove()
                }
            })
        })

        // region Remove empty elements wasting space
        document.querySelectorAll(".je-object__controls")?.forEach?.(el => 
            el.remove()
        )

        // Json-editor sets duplicate titles & titleHidden does not work so remove one
        document.querySelectorAll(".je-switcher")?.forEach?.(el => {
            el.previousSibling.remove()
            el.remove()
        })
        // endregion Remove empty elements wasting space


        document.querySelectorAll(".je-child-editor-holder")?.forEach?.(field => {
            field?.querySelector?.(".je-header")?.remove?.()
            const description = field.querySelector("p");

            if (description) {
                description.className = "fieldDescription";
            }

            // Divider for each field
            // if (!Array.prototype.find.call(field.childNodes, n => n.nodeName === "HR")) {
            //     const divider = document.createElement("hr");
            //     divider.style.border = "0.025em solid rgba(255, 255, 255, 0.1)"
            //     field.appendChild(divider)
            // }
        })

        // region Formatting remaining elements
        // Dropdowns
        document.querySelectorAll(".form-control select")?.forEach?.(dropdown => {
            dropdown.className = "emby-select-withcolor emby-select";
            dropdown.parentElement.className = "selectContainer";

            const iconContainer = document.createElement("div");
            const icon = document.createElement("span");
            
            iconContainer.className = "selectArrowContainer";
            icon.className = "selectArrow material-icons keyboard_arrow_down";
            iconContainer.appendChild(icon);
            dropdown.parentElement.appendChild(iconContainer);
        });

        // Checkboxes
        // json-editor does not respect inputAttributes to allow us to auto set this
        document.querySelectorAll(".je-checkbox")?.forEach?.(el => {
            el.className = ""
            el.parentElement.style = "";
            const container = el.parentElement.parentElement.parentElement;
            const dupeContainer = el.parentElement.parentElement.parentElement.parentElement.parentElement;

            if (
                container.className === "checkboxContainer emby-checkbox-label" &&
                dupeContainer.className === "checkboxContainer emby-checkbox-label"
            ) {
                dupeContainer.className = "";
                dupeContainer.style = "";
                dupeContainer.querySelector("b")?.remove?.()
            }
            
            el.setAttribute("style", `
                align-items: center;
                border: .14em solid currentcolor;
                border-top-color: currentcolor;
                border-right-color: currentcolor;
                border-bottom-color: currentcolor;
                border-left-color: currentcolor;
                border-top-color: currentcolor;
                border-right-color: currentcolor;
                border-bottom-color: currentcolor;
                border-left-color: currentcolor;
                border-radius: .14em;
                box-sizing: border-box;
                display: -webkit-flex;
                display: flex;
                height: 1.83em;
                -webkit-justify-content: center;
                justify-content: center;
                left: 0;
                margin: 0;
                overflow: hidden;
                position: absolute;
                top: 3px;
                width: 1.83em;
                z-index: 2;
                color: #fff;
                font-size: 1em;
            `);
        })
        
        document.querySelectorAll(".json-editor-btn-moveup.moveup.json-editor-btntype-move,.json-editor-btn-movedown.movedown.json-editor-btntype-move, .json-editor-btn-add.json-editor-btntype-add, .json-editor-btn-subtract.json-editor-btntype-deletelast, .json-editor-btn-delete.delete.json-editor-btntype-delete, .json-editor-btn-delete.json-editor-btntype-deleteall")?.forEach?.(button => {
            button.className = "raised raised-mini emby-button";
        })
        // endregion Formatting remaining elements
    }
}