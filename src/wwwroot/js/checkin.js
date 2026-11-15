// Check-in screen: webcam photos, OTP phone verification, signature pad and business card scanning.
(function () {
    'use strict';

    document.querySelectorAll('.camera-widget').forEach(initCamera);
    document.querySelectorAll('.otp-widget').forEach(initOtp);
    document.querySelectorAll('.signature-pad').forEach(initSignature);
    document.querySelectorAll('.bcard-scan').forEach(initBusinessCard);

    // Check-in forms need a photo, a verified number and a signature. The server re-checks;
    // this just gives instant feedback.
    document.querySelectorAll('form.checkin-form').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            var problems = [];
            var photo = form.querySelector('input[name="PhotoData"]');
            var signature = form.querySelector('input[name="SignatureData"]');
            if (photo && form.dataset.requirePhoto === 'true' && !photo.value) problems.push('take your photo');
            if (!form.querySelector('.phone-token').value) problems.push('verify your mobile number');
            if (signature && form.dataset.requireSignature === 'true' && !signature.value) problems.push('sign the visitor agreement');
            if (problems.length) {
                e.preventDefault();
                // Shown inline at the top; the kiosk keeps no message history.
                window.VmsMessages.show('Please ' + problems.join(', ').replace(/, ([^,]*)$/, ' and $1') + ' before checking in.', 'danger', { record: false });
            }
        });
    });

    function show(el, visible) { el.classList.toggle('d-none', !visible); }

    function initCamera(root) {
        var video = root.querySelector('.camera-video');
        var preview = root.querySelector('.camera-preview');
        var message = root.querySelector('.camera-message');
        var startBtn = root.querySelector('.camera-start');
        var captureBtn = root.querySelector('.camera-capture');
        var retakeBtn = root.querySelector('.camera-retake');
        var photoInput = root.querySelector('.photo-data');
        var isDocument = root.classList.contains('doc-camera');
        var stream = null;

        function setMessage(text, isError) {
            message.textContent = text || '';
            message.classList.toggle('text-danger', !!isError);
        }

        function stop() {
            if (stream) stream.getTracks().forEach(function (t) { t.stop(); });
            stream = null;
        }

        function showPhoto(dataUrl) {
            preview.src = dataUrl;
            show(preview, true);
            show(video, false);
            show(startBtn, false);
            show(captureBtn, false);
            show(retakeBtn, true);
            setMessage('');
        }

        function start() {
            if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
                setMessage('This browser cannot use the camera. Open the app over https or on localhost.', true);
                return;
            }
            setMessage('Starting camera…');
            // Documents need more pixels to stay legible; a rear camera suits them where there is one.
            var constraints = isDocument
                ? { width: { ideal: 1280 }, height: { ideal: 720 }, facingMode: 'environment' }
                : { width: { ideal: 640 }, height: { ideal: 480 }, facingMode: 'user' };
            navigator.mediaDevices.getUserMedia({ video: constraints, audio: false })
                .then(function (s) {
                    stream = s;
                    video.srcObject = s;
                    show(video, true);
                    show(preview, false);
                    show(startBtn, false);
                    show(captureBtn, true);
                    show(retakeBtn, false);
                    setMessage(isDocument ? 'Hold the card flat and close, filling the frame.' : '');
                })
                .catch(function (err) {
                    show(startBtn, true);
                    setMessage(err && err.name === 'NotAllowedError'
                        ? 'Camera access was blocked. Allow camera access in the browser and try again.'
                        : 'No camera found. Connect a webcam and try again.', true);
                });
        }

        function capture() {
            if (!video.videoWidth) return;
            var maxWidth = isDocument ? 1280 : 640;
            var scale = Math.min(1, maxWidth / video.videoWidth);
            var canvas = document.createElement('canvas');
            canvas.width = Math.round(video.videoWidth * scale);
            canvas.height = Math.round(video.videoHeight * scale);
            // A face preview is mirrored with CSS like a selfie camera; the saved photo is not.
            canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
            var dataUrl = canvas.toDataURL('image/jpeg', isDocument ? 0.9 : 0.85);
            photoInput.value = dataUrl;
            stop();
            showPhoto(dataUrl);
            root.dispatchEvent(new CustomEvent('camera:captured', { detail: dataUrl }));
        }

        startBtn.addEventListener('click', start);
        captureBtn.addEventListener('click', capture);
        retakeBtn.addEventListener('click', function () { photoInput.value = ''; start(); });
        window.addEventListener('pagehide', stop);

        // Let other widgets (the business card scanner) drive this camera.
        root.camera = {
            start: start,
            stop: stop,
            hasPhoto: function () { return !!photoInput.value; },
            setPhoto: function (dataUrl) { stop(); photoInput.value = dataUrl; showPhoto(dataUrl); }
        };

        // After a failed submit the server sends the photo back, so don't make them retake it.
        if (photoInput.value) showPhoto(photoInput.value);
        else if (root.dataset.autostart !== 'false') start();
        else show(startBtn, true);
    }

    function initSignature(root) {
        var canvas = root.querySelector('.signature-canvas');
        var placeholder = root.querySelector('.signature-placeholder');
        var input = root.querySelector('.signature-data');
        var ctx = canvas.getContext('2d');
        var drawing = false;
        var last = null;

        // Match the canvas's pixels to its on-screen size (sharp on high-DPI screens).
        function resize() {
            var rect = canvas.getBoundingClientRect();
            var ratio = window.devicePixelRatio || 1;
            var saved = input.value;
            canvas.width = Math.round(rect.width * ratio);
            canvas.height = Math.round(rect.height * ratio);
            ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
            ctx.lineWidth = 2.2;
            ctx.lineCap = 'round';
            ctx.lineJoin = 'round';
            ctx.strokeStyle = '#111827';
            if (saved) restore(saved);
        }

        function restore(dataUrl) {
            var img = new Image();
            img.onload = function () {
                var rect = canvas.getBoundingClientRect();
                ctx.drawImage(img, 0, 0, rect.width, rect.height);
            };
            img.src = dataUrl;
            placeholder.classList.add('d-none');
        }

        function point(e) {
            var rect = canvas.getBoundingClientRect();
            return { x: e.clientX - rect.left, y: e.clientY - rect.top };
        }

        canvas.addEventListener('pointerdown', function (e) {
            drawing = true;
            last = point(e);
            canvas.setPointerCapture(e.pointerId);
            placeholder.classList.add('d-none');
            // A tap leaves a dot.
            ctx.beginPath();
            ctx.arc(last.x, last.y, ctx.lineWidth / 2, 0, Math.PI * 2);
            ctx.fillStyle = ctx.strokeStyle;
            ctx.fill();
            e.preventDefault();
        });

        canvas.addEventListener('pointermove', function (e) {
            if (!drawing) return;
            var p = point(e);
            ctx.beginPath();
            ctx.moveTo(last.x, last.y);
            ctx.lineTo(p.x, p.y);
            ctx.stroke();
            last = p;
            e.preventDefault();
        });

        function end() {
            if (!drawing) return;
            drawing = false;
            input.value = canvas.toDataURL('image/png');
        }

        canvas.addEventListener('pointerup', end);
        canvas.addEventListener('pointercancel', end);

        root.querySelector('.signature-clear').addEventListener('click', function () {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            input.value = '';
            placeholder.classList.remove('d-none');
        });

        window.addEventListener('resize', resize);
        resize();
    }

    // ---------- Business card scanning ----------
    // The card is photographed (webcam) or uploaded, read with Tesseract.js in the browser (loaded from
    // the CDN only when first used), and the text parsed into name / company / email / mobile.
    var TESSERACT_URL = 'https://cdn.jsdelivr.net/npm/tesseract.js@5.1.1/dist/tesseract.min.js';
    var tesseractLoading = null;

    function loadTesseract() {
        if (window.Tesseract) return Promise.resolve(window.Tesseract);
        if (!tesseractLoading) {
            tesseractLoading = new Promise(function (resolve, reject) {
                var s = document.createElement('script');
                s.src = TESSERACT_URL;
                s.crossOrigin = 'anonymous';
                s.onload = function () { resolve(window.Tesseract); };
                s.onerror = function () { tesseractLoading = null; reject(new Error('load')); };
                document.head.appendChild(s);
            });
        }
        return tesseractLoading;
    }

    // One OCR worker, created on first use. Page segmentation mode 11 ("sparse text") suits business cards:
    // the default mode tends to skip large, isolated lines such as the person's name.
    var ocrWorker = null, ocrLogger = null;
    function getWorker(logger) {
        ocrLogger = logger;
        if (!ocrWorker) {
            ocrWorker = loadTesseract()
                .then(function (T) { return T.createWorker('eng', 1, { logger: function (m) { if (ocrLogger) ocrLogger(m); } }); })
                .then(function (w) { return w.setParameters({ tessedit_pageseg_mode: '11' }).then(function () { return w; }); })
                .catch(function (err) { ocrWorker = null; throw err; });
        }
        return ocrWorker;
    }

    function initBusinessCard(root) {
        var form = root.closest('form');
        var cameraBtn = root.querySelector('.bcard-camera-start');
        var fileInput = root.querySelector('.bcard-file');
        var cameraBox = root.querySelector('.bcard-camera');
        var widget = cameraBox.querySelector('.camera-widget');
        var imageInput = widget.querySelector('.photo-data');
        var progress = root.querySelector('.bcard-progress');
        var progressBar = progress.querySelector('.progress-bar');
        var progressLabel = progress.querySelector('.bcard-progress-label');
        var progressPct = progress.querySelector('.bcard-progress-pct');
        var result = root.querySelector('.bcard-result');
        var busy = false;

        var fields = {
            name: { input: form.querySelector('input[name="Visit.VisitorName"]'), label: 'Name' },
            company: { input: form.querySelector('input[name="Visit.Company"]'), label: 'Company' },
            email: { input: form.querySelector('input[name="Visit.Email"]'), label: 'Email' },
            phone: { input: form.querySelector('.otp-widget input[name="Visit.Phone"]'), label: 'Mobile' }
        };

        function setProgress(label, pct) {
            show(progress, true);
            progressLabel.textContent = label;
            progressPct.textContent = pct == null ? '' : Math.round(pct * 100) + '%';
            progressBar.style.width = Math.max(5, Math.round((pct || 0) * 100)) + '%';
        }

        function setResult(html, kind) {
            result.className = 'bcard-result small mt-2 ' + (kind === 'error' ? 'text-danger' : kind === 'warn' ? 'text-warning-emphasis' : '');
            result.innerHTML = html;
        }

        cameraBtn.addEventListener('click', function () {
            show(cameraBox, true);
            if (widget.camera) widget.camera.start();
            cameraBox.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        });

        widget.addEventListener('camera:captured', function (e) { read(e.detail); });

        fileInput.addEventListener('change', function () {
            var file = fileInput.files && fileInput.files[0];
            fileInput.value = '';
            if (!file) return;
            if (!/^image\//.test(file.type)) { setResult('Please choose an image file (JPEG or PNG).', 'error'); return; }
            var img = new Image();
            var url = URL.createObjectURL(file);
            img.onload = function () {
                // Same size and format as a camera capture, so the server stores it the same way.
                var scale = Math.min(1, 1600 / Math.max(img.naturalWidth, img.naturalHeight));
                var canvas = document.createElement('canvas');
                canvas.width = Math.round(img.naturalWidth * scale);
                canvas.height = Math.round(img.naturalHeight * scale);
                canvas.getContext('2d').drawImage(img, 0, 0, canvas.width, canvas.height);
                URL.revokeObjectURL(url);
                var dataUrl = canvas.toDataURL('image/jpeg', 0.88);
                show(cameraBox, true);
                if (widget.camera) widget.camera.setPhoto(dataUrl);
                read(dataUrl);
            };
            img.onerror = function () { URL.revokeObjectURL(url); setResult("That image couldn't be opened.", 'error'); };
            img.src = url;
        });

        function read(dataUrl) {
            if (busy || !dataUrl) return;
            busy = true;
            setResult('');
            setProgress('Loading the card reader…', 0.02);
            getWorker(function (m) {
                if (m.status === 'recognizing text') setProgress('Reading card…', m.progress);
                else if (/load|initializ/.test(m.status)) setProgress('Loading the card reader…', (m.progress || 0) * 0.3);
            })
                .then(function (worker) { return worker.recognize(dataUrl); })
                .then(function (r) { apply(parseBusinessCard(r.data.text || '')); })
                .catch(function (err) {
                    setResult(err && err.message === 'load'
                        ? "The card reader couldn't be loaded (it needs an internet connection). Please type your details instead."
                        : "The card couldn't be read. Try again with the card flat and well lit, or type your details.", 'error');
                })
                .finally(function () { busy = false; show(progress, false); });
        }

        function setValue(input, value) {
            input.value = value;
            // The OTP widget listens for input to reset verification when the number changes.
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.classList.add('bcard-filled');
            setTimeout(function () { input.classList.remove('bcard-filled'); }, 2500);
        }

        function apply(card) {
            var filled = [], offers = [];
            Object.keys(fields).forEach(function (key) {
                var f = fields[key], value = card[key];
                if (!f.input || !value) return;
                if (!f.input.value.trim()) { setValue(f.input, value); filled.push(f.label); }
                else if (f.input.value.trim() !== value) offers.push(key);
            });

            if (!filled.length && !offers.length) {
                setResult("We couldn't find your details on the card. Please type them in.", 'warn');
                return;
            }
            var html = filled.length
                ? '✓ Filled in from your card: <strong>' + filled.join(', ') + '</strong>. Please check them.'
                : 'Your card was read.';
            if (fields.phone.input && card.phone && filled.indexOf('Mobile') >= 0) html += ' Then verify your mobile number.';
            offers.forEach(function (key) {
                html += '<div class="mt-1">' + fields[key].label + ' on card: <strong>' + escapeHtml(card[key]) + '</strong> ' +
                    '<button type="button" class="btn btn-link btn-sm p-0 align-baseline bcard-use" data-key="' + key + '">Use this</button></div>';
            });
            setResult(html, '');
            result.querySelectorAll('.bcard-use').forEach(function (b) {
                b.addEventListener('click', function () {
                    setValue(fields[b.dataset.key].input, card[b.dataset.key]);
                    b.parentElement.remove();
                });
            });
        }
    }

    // Business card text -> { name, company, email, phone }. Heuristics, so the visitor checks the result.
    var TITLE_WORDS = /\b(manager|director|engineer|officer|ceo|cto|cfo|coo|founder|co-founder|president|vice|vp|head|lead|executive|consultant|analyst|developer|designer|architect|specialist|associate|partner|owner|proprietor|sales|marketing|operations|chairman|md|gm|agm|dgm|advocate|doctor|dr\.?|ca|cs|intern|administrator|coordinator|representative|supervisor)\b/i;
    var COMPANY_WORDS = /\b(pvt|private|ltd|limited|llp|llc|inc|corp|corporation|co\.|company|group|technologies|technology|tech|solutions|systems|services|industries|enterprises|consulting|consultants|labs|software|global|international|associates|traders|exports|infotech|ventures|holdings|bank|university|college|hospital|foundation|studio|agency)\b/i;
    var ADDRESS_WORDS = /\b(road|rd\.?|street|st\.|nagar|lane|floor|building|bldg|tower|plot|sector|block|phase|avenue|ave|main|cross|near|opp|layout|colony|marg|chowk|park|estate|suite|po box|p\.o\.|district|city|state|india|pin|zip)\b|\b\d{6}\b/i;

    function parseBusinessCard(text) {
        var lines = text.split(/\r?\n/)
            .map(function (l) { return l.replace(/[|~`^"“”«»]+/g, ' ').replace(/\s+/g, ' ').trim(); })
            .filter(function (l) { return l.length > 1; });
        var card = {};

        // Email: allow OCR's habit of putting spaces around @ and dots.
        var joined = lines.join('\n').replace(/\s*@\s*/g, '@').replace(/(@[\w-]+)\s*\.\s*/g, '$1.');
        var email = joined.match(/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/);
        if (email) card.email = email[0].replace(/\.+$/, '').toLowerCase();

        // Phones: prefer a line marked mobile/cell/M, else the first number that looks like a mobile.
        var phones = [];
        lines.forEach(function (l) {
            var re = /(\+?\d[\d\s().-]{8,}\d)/g, m;
            while ((m = re.exec(l))) {
                var digits = m[1].replace(/\D/g, '');
                if (digits.length < 10 || digits.length > 13) continue;
                var label = l.slice(0, m.index).toLowerCase();
                phones.push({
                    value: m[1].replace(/[^\d+]/g, ''),
                    mobile: /\b(m|mob|mobile|cell|cel|whatsapp|wa)\b/.test(label) || /^(\+?91)?[6-9]\d{9}$/.test(digits),
                    fax: /\bfax\b/.test(label)
                });
            }
        });
        var phone = phones.filter(function (p) { return p.mobile && !p.fax; })[0] || phones.filter(function (p) { return !p.fax; })[0];
        if (phone) card.phone = phone.value;

        function isContact(l) {
            return /@|www\.|https?:|\.com\b|\.in\b/i.test(l) || (l.replace(/\D/g, '').length >= 6);
        }

        // Company: a line with a company word, else the email's domain.
        var companyLine = lines.filter(function (l) { return COMPANY_WORDS.test(l) && !isContact(l) && !ADDRESS_WORDS.test(l); })[0];
        if (companyLine) card.company = tidy(companyLine);
        else if (card.email) {
            var domain = card.email.split('@')[1].split('.')[0];
            if (!/^(gmail|yahoo|outlook|hotmail|live|icloud|rediffmail|proton|protonmail|aol|zoho)$/.test(domain))
                card.company = domain.charAt(0).toUpperCase() + domain.slice(1);
        }

        // Name: 2-4 words of letters, not a title, company, address or contact line. Prefer one that
        // matches the email's local part, then the most prominent (usually first) such line.
        var candidates = lines.filter(function (l) {
            if (isContact(l) || TITLE_WORDS.test(l) || COMPANY_WORDS.test(l) || ADDRESS_WORDS.test(l)) return false;
            var words = l.split(' ');
            return words.length >= 2 && words.length <= 4 && /^[A-Za-z.' -]+$/.test(l) && l.replace(/[^A-Za-z]/g, '').length >= 4;
        });
        var local = card.email ? card.email.split('@')[0].replace(/[^a-z]/g, '') : '';
        var byEmail = local && candidates.filter(function (l) {
            return l.toLowerCase().split(/[ .]+/).some(function (w) { return w.length > 2 && local.indexOf(w) >= 0; });
        })[0];
        var name = byEmail || candidates[0];
        if (name) card.name = titleCase(name.replace(/\.(?=\S)/g, '. '));

        return card;
    }

    function tidy(s) {
        s = s.replace(/^[^A-Za-z0-9]+|[^A-Za-z0-9.)]+$/g, '');
        return s === s.toUpperCase() && s.length > 4 ? titleCase(s).replace(/\b(Pvt|Ltd|Llp|Llc|Inc)\b/g, function (w) { return w === 'Llp' || w === 'Llc' ? w.toUpperCase() : w; }) : s;
    }

    function titleCase(s) {
        return s.toLowerCase().replace(/\s+/g, ' ').trim().replace(/(^|[\s'-])([a-z])/g, function (_, p, c) { return p + c.toUpperCase(); });
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; });
    }

    function initOtp(root) {
        var form = root.closest('form');
        var phone = root.querySelector('.otp-phone');
        var sendBtn = root.querySelector('.otp-send');
        var entry = root.querySelector('.otp-entry');
        var code = root.querySelector('.otp-code');
        var verifyBtn = root.querySelector('.otp-verify');
        var status = root.querySelector('.otp-status');
        var tokenInput = root.querySelector('.phone-token');
        var autoSubmit = root.dataset.autoSubmit === 'true';
        var antiforgery = form.querySelector('input[name="__RequestVerificationToken"]').value;
        var verifiedPhone = tokenInput.value ? phone.value : null;

        function setStatus(text, kind) {
            status.textContent = text || '';
            status.className = 'otp-status small mt-2 ' + (kind === 'ok' ? 'text-success fw-semibold' : kind === 'error' ? 'text-danger' : 'text-muted');
        }

        function setVerified(isVerified) {
            phone.classList.toggle('is-valid', isVerified);
            sendBtn.textContent = isVerified ? 'Verified' : 'Send code';
            sendBtn.disabled = isVerified;
            if (isVerified) {
                show(entry, false);
                setStatus('✓ Mobile number verified', 'ok');
            }
        }

        function post(url, data) {
            var body = new FormData();
            body.append('__RequestVerificationToken', antiforgery);
            Object.keys(data).forEach(function (k) { body.append(k, data[k]); });
            return fetch(url, { method: 'POST', body: body, credentials: 'same-origin' })
                .then(function (r) {
                    if (!r.ok) throw new Error('HTTP ' + r.status);
                    return r.json();
                });
        }

        function send() {
            if (!phone.value.trim()) { setStatus('Please enter your mobile number.', 'error'); phone.focus(); return; }
            sendBtn.disabled = true;
            setStatus('Sending code…');
            post(root.dataset.sendUrl, { phone: phone.value })
                .then(function (res) {
                    if (!res.ok) { setStatus(res.error, 'error'); return; }
                    show(entry, true);
                    sendBtn.textContent = 'Resend';
                    setStatus('Code sent to ' + phone.value.trim() + '.');
                    code.value = '';
                    code.focus();
                })
                .catch(function () { setStatus('Could not send the code. Please try again.', 'error'); })
                .finally(function () { if (!tokenInput.value) sendBtn.disabled = false; });
        }

        function verify() {
            if (!code.value.trim()) { code.focus(); return; }
            verifyBtn.disabled = true;
            post(root.dataset.verifyUrl, { phone: phone.value, code: code.value })
                .then(function (res) {
                    if (!res.ok) { setStatus(res.error, 'error'); code.select(); return; }
                    tokenInput.value = res.token;
                    verifiedPhone = phone.value;
                    setVerified(true);
                    if (autoSubmit) {
                        setStatus('✓ Verified. Finding your visit…', 'ok');
                        form.submit();
                    }
                })
                .catch(function () { setStatus('Could not verify the code. Please try again.', 'error'); })
                .finally(function () { verifyBtn.disabled = false; });
        }

        sendBtn.addEventListener('click', send);
        verifyBtn.addEventListener('click', verify);

        // Enter in either box does the obvious next step instead of submitting the form.
        phone.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); if (!tokenInput.value) send(); }
        });
        code.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); verify(); }
        });

        // Changing the number after verifying means it has to be verified again.
        phone.addEventListener('input', function () {
            if (verifiedPhone !== null && phone.value !== verifiedPhone) {
                tokenInput.value = '';
                verifiedPhone = null;
                setVerified(false);
                setStatus('');
            }
        });

        if (tokenInput.value) setVerified(true);
    }
})();
