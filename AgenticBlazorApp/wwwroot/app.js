// Browser notifications + in-page toasts, called from Blazor via JSInterop.
window.appNotify = (function () {
    function ensureRoot() {
        let root = document.getElementById("toast-root");
        if (!root) {
            root = document.createElement("div");
            root.id = "toast-root";
            document.body.appendChild(root);
        }
        return root;
    }

    function ensurePermission() {
        if (!("Notification" in window)) return;
        if (Notification.permission === "default") {
            try { Notification.requestPermission(); } catch (_) { /* ignored */ }
        }
    }

    function show(title, body) {
        const root = ensureRoot();

        const toast = document.createElement("div");
        toast.className = "toast";

        const t = document.createElement("div");
        t.className = "toast-title";
        t.textContent = title;

        const b = document.createElement("div");
        b.className = "toast-body";
        b.textContent = body;

        toast.appendChild(t);
        toast.appendChild(b);
        root.appendChild(toast);

        // Auto-dismiss after 8s.
        setTimeout(function () {
            toast.style.transition = "opacity 0.35s ease, transform 0.35s ease";
            toast.style.opacity = "0";
            toast.style.transform = "translateX(20px)";
        }, 8000);
        setTimeout(function () { toast.remove(); }, 8500);

        // OS-level browser notification.
        if ("Notification" in window && Notification.permission === "granted") {
            try { new Notification(title, { body: body }); } catch (_) { /* ignored */ }
        }
    }

    return { ensurePermission: ensurePermission, show: show };
})();

// Scroll a chat window to the bottom after new messages.
window.chatScroll = {
    toBottom: function (id) {
        const el = document.getElementById(id);
        if (el) el.scrollTop = el.scrollHeight;
    }
};
