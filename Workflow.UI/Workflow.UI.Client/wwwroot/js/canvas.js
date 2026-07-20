window.dotflowCanvas = {
    measure: function (el) {
        if (!el) { return { width: 0, height: 0 }; }
        const r = el.getBoundingClientRect();
        return { width: r.width, height: r.height };
    }
};
