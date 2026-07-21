// ⌨️ Phase 3.3.b.4 — window-level keyboard shortcuts + unsaved-changes guard.
// Shortcuts are suppressed while focus is in an editable field (input/textarea/select/contenteditable).
window.dotflowKeys = (function () {
    let handler = null;
    let dirty = false;

    function isEditable(el) {
        if (!el) { return false; }
        const tag = el.tagName;
        return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable;
    }

    function onKeyDown(e) {
        if (!handler) { return; }
        if (isEditable(document.activeElement)) { return; }

        const key = e.key.toLowerCase();
        const ctrl = e.ctrlKey || e.metaKey;
        const interesting =
            (ctrl && ["z", "y", "s", "a", "c", "v"].includes(key)) ||
            key === "delete" || key === "backspace";
        if (!interesting) { return; }

        e.preventDefault();
        handler.invokeMethodAsync("OnShortcut", e.key, ctrl, e.shiftKey);
    }

    function onBeforeUnload(e) {
        if (dirty) {
            e.preventDefault();
            e.returnValue = "";
            return "";
        }
    }

    return {
        register: function (dotnetRef) {
            handler = dotnetRef;
            window.addEventListener("keydown", onKeyDown);
            window.addEventListener("beforeunload", onBeforeUnload);
        },
        unregister: function () {
            handler = null;
            window.removeEventListener("keydown", onKeyDown);
            window.removeEventListener("beforeunload", onBeforeUnload);
        },
        setDirty: function (value) { dirty = value; }
    };
})();
