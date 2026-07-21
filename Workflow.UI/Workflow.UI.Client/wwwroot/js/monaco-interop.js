// 📜 Phase 3.3.b.3 (D13) + Phase 3.4.0 — Lazy Monaco loader with a graceful textarea fallback.
// Monaco is fetched from CDN on first use so the initial WASM payload stays lean.
// 3.4.0 adds option/language switching, cursor insertion, and completion/hover provider seams
// (wired by 3.4.1). Every entry point is no-op-safe when Monaco never loaded (textarea fallback).
window.dotflowMonaco = (function () {
    let loaderPromise = null;

    function loadMonaco() {
        if (window.monaco) { return Promise.resolve(); }
        if (loaderPromise) { return loaderPromise; }
        loaderPromise = new Promise(function (resolve, reject) {
            const base = "https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min";
            const loader = document.createElement("script");
            loader.src = base + "/vs/loader.js";
            loader.onload = function () {
                window.require.config({ paths: { vs: base + "/vs" } });
                window.require(["vs/editor/editor.main"], function () { resolve(); });
            };
            loader.onerror = function () { reject(new Error("monaco loader failed")); };
            document.head.appendChild(loader);
        });
        return loaderPromise;
    }

    const editors = {};
    // language -> array of provider disposables, so re-registering doesn't stack duplicates.
    const providers = {};

    function applyMonacoOptions(opts) {
        opts = opts || {};
        return {
            theme: opts.theme || "vs-dark",
            fontSize: opts.fontSize || 13,
            lineNumbers: (opts.lineNumbers === false) ? "off" : "on",
            minimap: { enabled: !!opts.minimap },
            wordWrap: opts.wordWrap ? "on" : "off",
        };
    }

    function makeEditor(element, id, value, language, opts, dotnetRef) {
        const applied = applyMonacoOptions(opts);
        const editor = window.monaco.editor.create(element, {
            value: value || "",
            language: language || "plaintext",
            theme: applied.theme,
            minimap: applied.minimap,
            lineNumbers: applied.lineNumbers,
            wordWrap: applied.wordWrap,
            fontSize: applied.fontSize,
            automaticLayout: true,
            scrollBeyondLastLine: false,
        });
        editor.onDidChangeModelContent(function () {
            dotnetRef.invokeMethodAsync("OnMonacoChanged", editor.getValue());
        });
        editors[id] = editor;
        return editor;
    }

    return {
        // Back-compat entry used by the designer's CodeEditor (Phase 3.3).
        create: async function (element, id, value, language, dotnetRef) {
            try { await loadMonaco(); } catch (e) { return false; }
            makeEditor(element, id, value, language, null, dotnetRef);
            return true;
        },

        // Richer entry used by Script Studio's ScriptEditor (Phase 3.4).
        createEditor: async function (element, id, value, language, options, dotnetRef) {
            try { await loadMonaco(); } catch (e) { return false; }
            makeEditor(element, id, value, language, options, dotnetRef);
            return true;
        },

        setLanguage: function (id, mode) {
            const ed = editors[id];
            if (ed && window.monaco) { window.monaco.editor.setModelLanguage(ed.getModel(), mode || "plaintext"); }
        },

        setValue: function (id, value) {
            const ed = editors[id];
            if (ed && ed.getValue() !== value) { ed.setValue(value || ""); }
        },

        setOptions: function (id, options) {
            const ed = editors[id];
            if (!ed || !window.monaco) { return; }
            const applied = applyMonacoOptions(options);
            ed.updateOptions({
                fontSize: applied.fontSize,
                lineNumbers: applied.lineNumbers,
                minimap: applied.minimap,
                wordWrap: applied.wordWrap,
            });
            window.monaco.editor.setTheme(applied.theme);
        },

        // Inserts text at the current cursor (or replaces the selection).
        insertText: function (id, text) {
            const ed = editors[id];
            if (!ed || !window.monaco) { return false; }
            const sel = ed.getSelection();
            ed.executeEdits("insert", [{ range: sel, text: text, forceMoveMarkers: true }]);
            ed.focus();
            return true;
        },

        // Registers completion items for a language (Phase 3.4.1). items: [{label, insertText, detail, documentation}].
        registerCompletions: function (language, items) {
            if (!window.monaco) { return; }
            providers[language] = providers[language] || [];
            const d = window.monaco.languages.registerCompletionItemProvider(language, {
                triggerCharacters: ["."],
                provideCompletionItems: function () {
                    return {
                        suggestions: (items || []).map(function (it) {
                            return {
                                label: it.label,
                                kind: window.monaco.languages.CompletionItemKind.Method,
                                insertText: it.insertText,
                                detail: it.detail,
                                documentation: it.documentation,
                            };
                        }),
                    };
                },
            });
            providers[language].push(d);
        },

        // Registers hover docs for a language (Phase 3.4.1). items: [{label, detail, documentation}].
        registerHover: function (language, items) {
            if (!window.monaco) { return; }
            providers[language] = providers[language] || [];
            const map = {};
            (items || []).forEach(function (it) { map[it.label] = it; });
            const d = window.monaco.languages.registerHoverProvider(language, {
                provideHover: function (model, position) {
                    const word = model.getWordAtPosition(position);
                    if (!word) { return null; }
                    const it = map[word.word];
                    if (!it) { return null; }
                    return {
                        contents: [
                            { value: "```" + language + "\n" + (it.detail || it.label) + "\n```" },
                            { value: it.documentation || "" },
                        ],
                    };
                },
            });
            providers[language].push(d);
        },

        dispose: function (id) {
            if (editors[id]) { editors[id].dispose(); delete editors[id]; }
        }
    };
})();
