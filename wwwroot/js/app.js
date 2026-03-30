// ===== Delete Modal Handler =====
const deleteModal = document.getElementById('deleteModal');
if (deleteModal) {
    deleteModal.addEventListener('show.bs.modal', function (e) {
        const btn = e.relatedTarget;
        const id = btn.getAttribute('data-delete-id');
        const name = btn.getAttribute('data-delete-name') || '';
        document.getElementById('deleteId').value = id;
        const label = document.getElementById('deleteLabel');
        if (label) label.textContent = name;
    });
}

// ===== Auto-Refresh Countdown =====
const countdownEl = document.getElementById('refresh-countdown');
if (countdownEl) {
    let seconds = 60;
    const tick = () => {
        // Pause if modal is open
        if (deleteModal && deleteModal.classList.contains('show')) return;
        seconds--;
        countdownEl.textContent = seconds + 's';
        if (seconds <= 0) location.reload();
    };
    setInterval(tick, 1000);
}

// ===== Dark Mode Toggle =====
const btnTheme = document.getElementById('btnTheme');
if (btnTheme) {
    const html = document.documentElement;
    const icon = btnTheme.querySelector('i');
    const saved = localStorage.getItem('theme');

    if (saved) {
        html.setAttribute('data-bs-theme', saved);
    }

    const updateIcon = () => {
        const dark = html.getAttribute('data-bs-theme') === 'dark';
        icon.className = dark ? 'bi bi-sun' : 'bi bi-moon-stars';
    };
    updateIcon();

    btnTheme.addEventListener('click', () => {
        const next = html.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
        html.setAttribute('data-bs-theme', next);
        localStorage.setItem('theme', next);
        updateIcon();
    });
}

// ===== Add Modal: Desk Availability =====
const addModal = document.getElementById('addModal');
if (addModal) {
    const dateInput = addModal.querySelector('input[name="Data"]');

    const updateDesks = () => {
        const date = dateInput.value;
        if (!date) return;
        fetch('/?handler=Occupied&date=' + date)
            .then(r => r.json())
            .then(occupied => {
                for (let i = 1; i <= 16; i++) {
                    const radio = document.getElementById('add-desk-' + i);
                    const label = addModal.querySelector('label[for="add-desk-' + i + '"]');
                    if (!radio || !label) continue;
                    const code = 'ET1L' + String(i).padStart(2, '0');
                    const occupant = occupied[code];
                    // Restore original content
                    const icon = '<i class="bi bi-pc-display d-block mb-1"></i>';
                    if (occupant) {
                        radio.disabled = true;
                        if (radio.checked) radio.checked = false;
                        label.classList.remove('btn-outline-primary');
                        label.classList.add('btn-outline-danger');
                        label.title = occupant;
                        label.innerHTML = icon + '<small>' + code + '</small>' +
                            '<div class="desk-occupant">' + occupant + '</div>';
                    } else {
                        radio.disabled = false;
                        label.classList.remove('btn-outline-danger');
                        label.classList.add('btn-outline-primary');
                        label.title = '';
                        label.innerHTML = icon + '<small>' + code + '</small>';
                    }
                }
            });
    };

    dateInput.addEventListener('change', updateDesks);
    addModal.addEventListener('shown.bs.modal', updateDesks);
}

// ===== Toast Notifications =====
function showToast(message, type) {
    const container = document.getElementById('toastContainer');
    if (!container) return;

    const icons = { success: 'bi-check-circle-fill', danger: 'bi-exclamation-triangle-fill', warning: 'bi-exclamation-circle-fill' };
    const toast = document.createElement('div');
    toast.className = 'toast align-items-center text-bg-' + type + ' border-0';
    toast.setAttribute('role', 'alert');
    toast.innerHTML =
        '<div class="d-flex">' +
            '<div class="toast-body"><i class="bi ' + (icons[type] || 'bi-info-circle') + ' me-2"></i>' + message + '</div>' +
            '<button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>' +
        '</div>';
    container.appendChild(toast);
    new bootstrap.Toast(toast, { delay: 4000 }).show();
    toast.addEventListener('hidden.bs.toast', () => toast.remove());
}
