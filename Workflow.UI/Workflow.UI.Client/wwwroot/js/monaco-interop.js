// 📜 Phase 3.3.b.3 (D13) — Lazy Monaco editor loader with a graceful textarea fallback.
// Monaco is fetched from CDN on first use so the initial WASM payload stays lean.
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

    return {
        create: async function (element, id, value, language, dotnetRef) {
            try {
                await loadMonaco();
            } catch (e) {
                return false; // caller falls back to <textarea>
            }

            const editor = window.monaco.editor.create(element, {
                value: value || "",
                language: language || "plaintext",
                theme: "vs-dark",
                minimap: { enabled: false },
                automaticLayout: true,
                fontSize: 13,
            });
            editor.onDidChangeModelContent(function () {
                dotnetRef.invokeMethodAsync("OnMonacoChanged", editor.getValue());
            });
            editors[id] = editor;
            return true;
        },
        dispose: function (id) {
            if (editors[id]) { editors[id].dispose(); delete editors[id]; }
        }
    };
})();
