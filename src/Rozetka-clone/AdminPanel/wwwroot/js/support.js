window.trebaSupport = {
    latest: function (id) {
        const list = document.getElementById(id);
        if (!list) return;
        if (!list.dataset.initialized || list.scrollHeight - list.clientHeight - list.scrollTop < 240) list.scrollTop = list.scrollHeight;
        list.dataset.initialized = "true";
    }
};
