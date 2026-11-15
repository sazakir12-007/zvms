// Sidebar expand/collapse, remembered per browser.
(function () {
    'use strict';

    var root = document.documentElement;
    var button = document.getElementById('sidebar-collapse');
    if (!button) return;

    function sync() {
        var collapsed = root.classList.contains('sidebar-collapsed');
        button.setAttribute('aria-expanded', String(!collapsed));
        button.setAttribute('aria-label', collapsed ? 'Expand sidebar' : 'Collapse sidebar');
    }

    button.addEventListener('click', function () {
        var collapsed = root.classList.toggle('sidebar-collapsed');
        try { localStorage.setItem('vms-sidebar', collapsed ? 'collapsed' : 'expanded'); } catch (e) { }
        sync();
    });

    sync();
})();

// Department filter for host pickers: only hosts in the chosen department are listed.
(function () {
    'use strict';

    document.querySelectorAll('.host-picker').forEach(function (root) {
        var department = root.querySelector('.host-department');
        var host = root.querySelector('.host-select');

        function filter() {
            var dept = department.value;
            Array.prototype.forEach.call(host.options, function (opt) {
                if (!opt.dataset.department) return; // placeholder
                opt.hidden = dept !== '' && opt.dataset.department !== dept;
            });
            if (host.selectedOptions[0] && host.selectedOptions[0].hidden) host.value = '0';
        }

        department.addEventListener('change', filter);
        // Picking a host directly also shows which department they are in.
        host.addEventListener('change', function () {
            var opt = host.selectedOptions[0];
            if (opt && opt.dataset.department) department.value = opt.dataset.department;
        });

        // After a failed submit only the host comes back; restore its department.
        var selected = host.selectedOptions[0];
        if (selected && selected.dataset.department) department.value = selected.dataset.department;
        filter();
    });
})();

// "Until about 15:30" under the expected-stay picker. Counts from the appointment time
// when the form has one, otherwise from now.
(function () {
    'use strict';

    document.querySelectorAll('.stay-select').forEach(function (select) {
        var form = select.closest('form');
        var hint = select.parentElement.querySelector('.stay-until');
        var start = form && form.querySelector('input[name="Visit.ExpectedAt"]');

        function update() {
            var from = start && start.value ? new Date(start.value) : new Date();
            if (isNaN(from)) { hint.textContent = ''; return; }
            var until = new Date(from.getTime() + Number(select.value) * 60000);
            hint.textContent = 'Expected check-out around ' + until.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) +
                (until.toDateString() !== from.toDateString() ? ' (next day)' : '');
        }

        select.addEventListener('change', update);
        if (start) start.addEventListener('change', update);
        update();
    });
})();

// Sidebar groups (Admin): click to open/close; remembered per browser. A group containing the
// current page always starts open.
(function () {
    'use strict';

    document.querySelectorAll('.side-group').forEach(function (group) {
        var toggle = group.querySelector('.side-group-toggle');
        var key = 'vms-group-' + group.dataset.group;

        function set(open) {
            group.classList.toggle('open', open);
            toggle.setAttribute('aria-expanded', String(open));
        }

        if (!group.classList.contains('has-active')) {
            try { if (localStorage.getItem(key) === 'open') set(true); } catch (e) { }
        }

        toggle.addEventListener('click', function () {
            // With the sidebar collapsed the items show as a hover flyout instead.
            if (document.documentElement.classList.contains('sidebar-collapsed')) return;
            var open = !group.classList.contains('open');
            set(open);
            try { localStorage.setItem(key, open ? 'open' : 'closed'); } catch (e) { }
        });
    });
})();

// Top bar notifications: polls /notifications every minute. "Unread" means newer than the last
// time this browser opened the menu.
(function () {
    'use strict';

    var button = document.querySelector('.notif-btn');
    if (!button) return;
    var count = button.querySelector('.notif-count');
    var list = document.querySelector('.notif-list');
    var summary = document.querySelector('.notif-summary');
    var seenKey = 'vms-notif-seen';
    var items = [];

    function lastSeen() {
        try { return Number(localStorage.getItem(seenKey)) || 0; } catch (e) { return 0; }
    }

    function render() {
        var seen = lastSeen();
        var unread = items.filter(function (n) { return Date.parse(n.at) > seen; }).length;
        count.textContent = unread > 9 ? '9+' : String(unread);
        count.hidden = unread === 0;
        count.classList.toggle('is-danger', items.some(function (n) { return n.level === 'danger' && Date.parse(n.at) > seen; }));
        button.setAttribute('aria-label', unread ? 'Notifications, ' + unread + ' unread' : 'Notifications');
        summary.textContent = items.length ? items.length + ' active' : '';

        list.textContent = '';
        if (!items.length) {
            var empty = document.createElement('div');
            empty.className = 'notif-empty';
            empty.textContent = 'All clear. Nothing needs attention.';
            list.appendChild(empty);
            return;
        }
        items.forEach(function (n) {
            var a = document.createElement('a');
            a.className = 'notif-item notif-' + n.level + (Date.parse(n.at) > seen ? ' is-unread' : '');
            a.href = n.url;
            var icon = document.createElement('span');
            icon.className = 'notif-icon';
            icon.innerHTML = n.icon; // server-generated SVG from a fixed icon set
            var text = document.createElement('span');
            text.className = 'notif-text';
            var title = document.createElement('span');
            title.className = 'notif-title';
            title.textContent = n.title;
            var detail = document.createElement('span');
            detail.className = 'notif-detail';
            detail.textContent = n.detail;
            var time = document.createElement('span');
            time.className = 'notif-time';
            time.textContent = n.ago;
            text.append(title, detail, time);
            a.append(icon, text);
            list.appendChild(a);
        });
    }

    function load() {
        return fetch(button.dataset.url, { credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (data) { items = data; render(); })
            .catch(function () { /* keep the last list */ });
    }

    // Opening the menu refreshes it and marks everything as seen (after it has been shown).
    button.addEventListener('shown.bs.dropdown', function () {
        load().then(function () {
            setTimeout(function () {
                try { localStorage.setItem(seenKey, String(Date.now())); } catch (e) { }
                count.hidden = true;
            }, 1500);
        });
    });
    button.addEventListener('hidden.bs.dropdown', render);

    load();
    setInterval(function () { if (!document.hidden) load(); }, 60000);
})();

// "/" jumps to the top bar search (unless you're already typing somewhere).
(function () {
    'use strict';

    var search = document.getElementById('global-search');
    if (!search) return;
    document.addEventListener('keydown', function (e) {
        if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return;
        var t = e.target;
        if (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName)) return;
        e.preventDefault();
        search.focus();
        search.select();
    });
})();

// Inline messages: every notice and confirmation appears at the top of the page (#page-messages),
// never as a browser dialog. Signed-in layouts also keep the 10 most recent messages for the
// top bar's "Recent messages" menu (in this browser, per organisation and user).
window.VmsMessages = (function () {
    'use strict';
    var MAX = 10;
    var box = document.getElementById('page-messages');
    var historyKey = box && box.dataset.history;

    function container() {
        if (box) return box;
        box = document.createElement('div');
        box.id = 'page-messages';
        box.className = 'page-messages';
        box.setAttribute('aria-live', 'polite');
        var main = document.querySelector('main') || document.body;
        main.insertBefore(box, main.firstChild);
        return box;
    }

    function load() {
        if (!historyKey) return [];
        try { return JSON.parse(localStorage.getItem(historyKey) || '[]'); } catch (e) { return []; }
    }

    function save(list) {
        try { localStorage.setItem(historyKey, JSON.stringify(list.slice(0, MAX))); } catch (e) { }
    }

    function record(text, level) {
        if (!historyKey || !text) return;
        var list = load();
        list.unshift({ text: text, level: level || 'info', at: Date.now(), page: document.title.split(' - ')[0], url: location.pathname + location.search });
        save(list);
        renderMenu();
    }

    function reveal(el) {
        var top = el.getBoundingClientRect().top;
        if (top < 70 || top > window.innerHeight - 40) window.scrollTo({ top: Math.max(0, window.scrollY + top - 90), behavior: 'smooth' });
    }

    function alertEl(level, role) {
        var el = document.createElement('div');
        el.className = 'alert alert-' + level + ' alert-dismissible fade show';
        el.setAttribute('role', role);
        return el;
    }

    /** Shows a message at the top of the page. level: success | info | warning | danger. */
    function show(text, level, options) {
        level = level || 'info';
        var el = alertEl(level, level === 'danger' ? 'alert' : 'status');
        el.appendChild(document.createTextNode(text));
        var close = document.createElement('button');
        close.type = 'button';
        close.className = 'btn-close';
        close.setAttribute('data-bs-dismiss', 'alert');
        close.setAttribute('aria-label', 'Close');
        el.appendChild(close);
        // One copy of the same message at a time.
        Array.prototype.forEach.call(container().querySelectorAll('.alert[data-text]'), function (old) {
            if (old.dataset.text === text) old.remove();
        });
        el.dataset.text = text;
        container().insertBefore(el, container().firstChild);
        reveal(el);
        if (!(options && options.record === false)) record(text, level);
        return el;
    }

    /** Asks for confirmation inline at the top of the page. Resolves true (confirmed) or false. */
    function ask(text, options) {
        options = options || {};
        var old = container().querySelector('.confirm-bar');
        if (old) old.querySelector('.confirm-no').click();
        return new Promise(function (resolve) {
            var el = alertEl(options.level || 'warning', 'alertdialog');
            el.classList.add('confirm-bar');
            el.classList.remove('alert-dismissible');
            var msg = document.createElement('span');
            msg.className = 'confirm-text';
            msg.textContent = text;
            var yes = document.createElement('button');
            yes.type = 'button';
            yes.className = 'btn btn-sm btn-' + (options.danger === false ? 'primary' : 'danger') + ' confirm-yes';
            yes.textContent = options.confirmLabel || 'Yes, continue';
            var no = document.createElement('button');
            no.type = 'button';
            no.className = 'btn btn-sm btn-outline-secondary confirm-no';
            no.textContent = options.cancelLabel || 'Cancel';
            var actions = document.createElement('span');
            actions.className = 'confirm-actions';
            actions.appendChild(yes);
            actions.appendChild(no);
            el.appendChild(msg);
            el.appendChild(actions);
            container().insertBefore(el, container().firstChild);

            function done(result) {
                document.removeEventListener('keydown', onKey);
                el.remove();
                if (options.returnFocus && !result) options.returnFocus.focus({ preventScroll: true });
                resolve(result);
            }
            function onKey(e) { if (e.key === 'Escape') done(false); }
            yes.addEventListener('click', function () { done(true); });
            no.addEventListener('click', function () { done(false); });
            document.addEventListener('keydown', onKey);
            reveal(el);
            yes.focus({ preventScroll: true });
        });
    }

    // Forms with data-confirm="question" ask inline before submitting.
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form.matches || !form.matches('form[data-confirm]')) return;
        if (form.dataset.confirmed === '1') { delete form.dataset.confirmed; return; }
        e.preventDefault();
        e.stopImmediatePropagation();
        var submitter = e.submitter || form.querySelector('[type=submit], button:not([type])');
        var label = form.dataset.confirmLabel
            || (submitter && (submitter.getAttribute('title') || submitter.getAttribute('aria-label') || submitter.textContent.trim()))
            || 'Yes, continue';
        ask(form.dataset.confirm, { confirmLabel: label, returnFocus: submitter }).then(function (ok) {
            if (!ok) return;
            form.dataset.confirmed = '1';
            if (form.requestSubmit) form.requestSubmit(submitter && submitter.form === form ? submitter : undefined);
            else form.submit();
        });
    }, true);

    // ---- Recent messages menu ----
    function ago(t) {
        var s = Math.round((Date.now() - t) / 1000);
        if (s < 60) return 'just now';
        if (s < 3600) return Math.floor(s / 60) + ' min ago';
        if (s < 86400) return Math.floor(s / 3600) + ' h ago';
        return new Date(t).toLocaleDateString();
    }

    function renderMenu() {
        var menu = document.querySelector('.recent-menu');
        if (!menu) return;
        var list = load();
        var target = menu.querySelector('.recent-list');
        target.textContent = '';
        if (!list.length) {
            var empty = document.createElement('div');
            empty.className = 'notif-empty';
            empty.textContent = 'No messages yet.';
            target.appendChild(empty);
        }
        list.forEach(function (m) {
            var a = document.createElement('a');
            a.className = 'notif-item recent-item level-' + m.level;
            a.href = m.url || '#';
            var dot = document.createElement('span');
            dot.className = 'recent-dot';
            var body = document.createElement('span');
            body.className = 'min-w-0';
            var title = document.createElement('span');
            title.className = 'd-block recent-text';
            title.textContent = m.text;
            var meta = document.createElement('span');
            meta.className = 'd-block small text-muted';
            meta.textContent = (m.page ? m.page + ' · ' : '') + ago(m.at);
            body.appendChild(title);
            body.appendChild(meta);
            a.appendChild(dot);
            a.appendChild(body);
            target.appendChild(a);
        });
        var count = document.querySelector('.recent-count');
        if (count) { count.textContent = list.length; count.hidden = !list.length; }
    }

    // Messages the server rendered into the page (TempData) go into the history once.
    if (box) Array.prototype.forEach.call(box.querySelectorAll('[data-vms-message]'), function (el) {
        record(el.dataset.vmsMessage, el.dataset.level);
    });
    var clear = document.querySelector('.recent-clear');
    if (clear) clear.addEventListener('click', function () {
        try { localStorage.removeItem(historyKey); } catch (e) { }
        renderMenu();
    });
    renderMenu();

    return { show: show, ask: ask };
})();
