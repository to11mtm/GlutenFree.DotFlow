// 📥 Phase 3.6 (E1) — file download + read helpers for workflow export/import.
// Blazor can't hand the browser a file directly, so we build a blob, click a synthetic anchor,
// and revoke the object URL. Kept tiny and dependency-free on purpose~ ✨
window.dotflowFiles = (function () {
    return {
        // Triggers a download of `content` as `filename`.
        download: function (filename, content, mimeType) {
            const blob = new Blob([content], { type: mimeType || "application/json" });
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement("a");
            anchor.href = url;
            anchor.download = filename;
            anchor.style.display = "none";
            document.body.appendChild(anchor);
            anchor.click();
            document.body.removeChild(anchor);

            // Revoke on the next tick — revoking synchronously can cancel the download in some
            // browsers before it has started reading the blob.
            setTimeout(function () { URL.revokeObjectURL(url); }, 0);
            return true;
        }
    };
})();
