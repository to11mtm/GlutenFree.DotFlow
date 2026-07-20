window.dotflowCanvas = {
    measure: function (el) {
        if (!el) { return { width: 0, height: 0 }; }
        const r = el.getBoundingClientRect();
        return { width: r.width, height: r.height };
    }
};

// Native HTML5 drag glue for the module palette. A drag only initiates if dataTransfer data is set
// synchronously inside the native `dragstart` — Blazor's async @ondragstart C# handler can't do that,
// so browsers (notably Firefox) show a "no-drop" cursor and the drag never starts. This delegated
// listener sets the payload from the item's data-module-id; PaletteDragState still carries the id to
// the drop handler, so this is purely additive.
(function () {
    if (window.__dotflowDragInstalled) { return; }
    window.__dotflowDragInstalled = true;

    document.addEventListener('dragstart', function (e) {
        const item = e.target && e.target.closest ? e.target.closest('[data-module-id]') : null;
        if (!item || !e.dataTransfer) { return; }
        const id = item.getAttribute('data-module-id');
        try {
            e.dataTransfer.setData('text/plain', id || '');
            e.dataTransfer.effectAllowed = 'copy';
        } catch (_) {
            // Some browsers throw if dataTransfer is restricted; the drag can still proceed.
        }
    }, false);

    // A drop is only allowed if `dragover` calls preventDefault(). Do it natively so it works
    // regardless of Blazor's @ondragover:preventDefault directive, and show the copy cursor.
    document.addEventListener('dragover', function (e) {
        const canvas = e.target && e.target.closest ? e.target.closest('.df-canvas-viewport') : null;
        if (canvas) {
            e.preventDefault();
            if (e.dataTransfer) { e.dataTransfer.dropEffect = 'copy'; }
        }
    }, false);
})();

