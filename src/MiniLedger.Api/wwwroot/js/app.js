function formatCurrency(value) {
    return parseFloat(value || 0).toLocaleString('pt-BR', {
        style: 'currency',
        currency: 'BRL'
    });
}

function formatDate(dateString) {
    const date = new Date(dateString);
    return date.toLocaleString('pt-BR');
}

function getAuthHeaders() {
    const headers = { 'Content-Type': 'application/json' };
    const token = currentAccount?.token;
    if (token) {
        headers['Authorization'] = 'Bearer ' + token;
    }
    headers['Idempotency-Key'] = 'dep-' + Date.now() + '-' + Math.random().toString(36).slice(2, 12);
    return headers;
}

function showToast(message, type = 'success') {
    const container = document.getElementById('toastContainer');
    if (!container) return;
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.textContent = message;
    container.appendChild(toast);
    requestAnimationFrame(() => {
        toast.classList.add('show');
    });
    setTimeout(() => {
        toast.classList.remove('show');
        setTimeout(() => {
            toast.remove();
        }, 250);
    }, 2200);
}

// Máscara de dinheiro: o usuário digita só números e o campo preenche
// da direita para a esquerda (1 -> 0,01 | 10 -> 0,10 | 1000 -> 10,00)
function attachMoneyMask(input) {
    input.addEventListener('input', () => {
        const digits = input.value.replace(/\D/g, '');
        if (digits === '') {
            input.value = '';
            return;
        }
        const value = parseInt(digits, 10) / 100;
        input.value = 'R$ ' + value.toLocaleString('pt-BR', {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    });
}

// Converte o texto formatado ("1.000,50") de volta para número (1000.5)
function parseMoney(text) {
    const digits = (text || '').replace(/\D/g, '');
    return digits === '' ? 0 : parseInt(digits, 10) / 100;
}

let currentAccount = null;

function clearAllPageResults() {
    const results = [
        document.getElementById('depositPageResult'),
        document.getElementById('transferPageResult'),
        document.getElementById('statementPageResult')
    ];
    results.forEach(r => {
        if (!r) return;
        r.innerHTML = '';
        r.classList.remove('error', 'success');
    });
}

function clearAllPageInputs() {
    try {
        document.querySelectorAll('.page input, .page textarea').forEach(el => {
            const type = (el.type || '').toLowerCase();
            if (type === 'button' || type === 'submit' || type === 'reset' || type === 'checkbox' || type === 'radio' || type === 'hidden') {
                return;
            }
            el.value = '';
        });
    } catch (e) {}

    const depositInput = document.getElementById('depositAmount');
    if (depositInput) depositInput.value = '';
    const transferAmount = document.getElementById('transferAmount');
    if (transferAmount) transferAmount.value = '';
    const transferTo = document.getElementById('transferTo');
    if (transferTo) transferTo.value = '';
    const transferDesc = document.getElementById('transferDescription');
    if (transferDesc) transferDesc.value = '';
    const homeQuickDeposit = document.getElementById('homeQuickDeposit');
    if (homeQuickDeposit) homeQuickDeposit.value = '';
    const homeQuickTransferTo = document.getElementById('homeQuickTransferTo');
    if (homeQuickTransferTo) homeQuickTransferTo.value = '';
    const homeQuickTransferAmount = document.getElementById('homeQuickTransferAmount');
    if (homeQuickTransferAmount) homeQuickTransferAmount.value = '';
    const modalDeposit = document.getElementById('modalDepositAmount');
    if (modalDeposit) modalDeposit.value = '';
}

function showPage(pageId) {
    // Se não estiver logado, só a Home pode ser exibida
    if (!currentAccount && pageId !== 'pageHome') {
        pageId = 'pageHome';
    }

    ['depositPageResult', 'transferPageResult', 'statementPageResult'].forEach(id => {
        const el = document.getElementById(id);
        if (el) {
            el.innerHTML = '';
            el.classList.remove('error', 'success');
        }
    });

    document.querySelectorAll('.page').forEach(p => {
        p.classList.remove('active');
        p.style.display = 'none';
    });

    const target = document.getElementById(pageId);
    if (target) {
        target.classList.add('active');
        target.style.display = 'block';
    }

    clearAllPageInputs();
    clearAllPageResults();

    document.querySelectorAll('.header-btn[data-page]').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.page === pageId.replace('page', '').toLowerCase());
    });
}

function openModal(id) {
    document.getElementById(id).classList.add('active');
}

function closeModal(id) {
    document.getElementById(id).classList.remove('active');
}

function updateHeader() {
    const guest = document.getElementById('headerGuestActions');
    const user = document.getElementById('headerUserActions');
    if (currentAccount) {
        guest.style.display = 'none';
        user.style.display = 'flex';
    } else {
        guest.style.display = 'flex';
        user.style.display = 'none';
    }
}

function saveCurrentAccount(account) {
    currentAccount = account;
    try {
        sessionStorage.setItem('miniLedger_currentAccount', JSON.stringify(account));
    } catch(e){}
    try {
        localStorage.removeItem('miniLedger_currentAccount');
    } catch(e){}
    updateHeader();
    renderHome();
}

function loadCurrentAccount() {
    try {
        const saved = sessionStorage.getItem('miniLedger_currentAccount');
        if (saved) {
            try {
                currentAccount = JSON.parse(saved);
            } catch {
                currentAccount = null;
                try { sessionStorage.removeItem('miniLedger_currentAccount'); } catch(e){}
            }
        } else {
            currentAccount = null;
        }
    } catch(e){
        currentAccount = null;
    }
    try { localStorage.removeItem('miniLedger_current_account'); } catch(e){}
    try { localStorage.removeItem('miniLedger_currentAccount'); } catch(e){}
    updateHeader();
    renderHome();
    showPage('pageHome');
    if (currentAccount) {
        refreshBalanceHome();
    }
    clearAllPageInputs();
    clearAllPageResults();
}

function clearCurrentAccount() {
    currentAccount = null;
    try { sessionStorage.removeItem('miniLedger_currentAccount'); } catch(e){}
    try { localStorage.removeItem('miniLedger_current_account'); } catch(e){}
    try { localStorage.removeItem('miniLedger_currentAccount'); } catch(e){}
    updateHeader();
    renderHome();
    showPage('pageHome');
    document.querySelectorAll('.header-btn[data-page]').forEach(btn => btn.classList.remove('active'));
    document.querySelectorAll('.result').forEach(r => {
        r.innerHTML = '';
        r.classList.remove('error', 'success');
    });
    clearAllPageInputs();
    clearAllPageResults();
}

function renderHome() {
    const loggedOut = document.getElementById('homeLoggedOut');
    const loggedIn = document.getElementById('homeLoggedIn');
    if (currentAccount) {
        loggedOut.style.display = 'none';
        // '' deixa o CSS aplicar o display:flex do .home-account (preserva o gap)
        loggedIn.style.display = '';
        document.getElementById('homeHolderName').textContent = currentAccount.holderName;
        document.getElementById('homeAccountNumber').textContent =
            String(currentAccount.accountNumber).replace(/(\d{4})(?=\d)/g, '$1 ');
        document.getElementById('homeBalance').textContent = formatCurrency(currentAccount.balance);
    } else {
        loggedOut.style.display = '';
        loggedIn.style.display = 'none';
    }
}

attachMoneyMask(document.getElementById('modalCreateBalance'));
attachMoneyMask(document.getElementById('depositPageAmount'));
attachMoneyMask(document.getElementById('transferPageAmount'));

document.getElementById('btnOpenCreate').addEventListener('click', () => {
    document.getElementById('modalCreateResult').innerHTML = '';
    document.getElementById('modalCreateResult').classList.remove('error', 'success');
    document.getElementById('modalCreateForm').reset();
    document.getElementById('modalCreateBalance').value = '';
    openModal('modalCreate');
});

document.getElementById('btnOpenLogin').addEventListener('click', () => {
    document.getElementById('modalLoginResult').innerHTML = '';
    document.getElementById('modalLoginResult').classList.remove('error', 'success');
    document.getElementById('modalLoginForm').reset();
    openModal('modalLogin');
});

document.getElementById('heroCreate').addEventListener('click', () => {
    document.getElementById('btnOpenCreate').click();
});

document.getElementById('heroLogin').addEventListener('click', () => {
    document.getElementById('btnOpenLogin').click();
});

document.querySelectorAll('.modal-close').forEach(btn => {
    btn.addEventListener('click', () => {
        closeModal(btn.dataset.close);
    });
});

document.querySelectorAll('.modal').forEach(modal => {
    modal.addEventListener('click', (e) => {
        if (e.target === modal) {
            modal.classList.remove('active');
        }
    });
});

document.querySelectorAll('[data-page]').forEach(btn => {
    btn.addEventListener('click', () => {
        ['depositPageResult', 'transferPageResult', 'statementPageResult'].forEach(id => {
            const el = document.getElementById(id);
            if (el) {
                el.innerHTML = '';
                el.classList.remove('error', 'success');
            }
        });
        const page = 'page' + btn.dataset.page.charAt(0).toUpperCase() + btn.dataset.page.slice(1);
        showPage(page);
        if (btn.dataset.page === 'statement') {
            loadStatement();
        }
    });
});

const btnCopy = document.getElementById('btnCopyAccount');
if (btnCopy) {
    btnCopy.addEventListener('click', async () => {
        if (!currentAccount) return;
        const acc = String(currentAccount.accountNumber).replace(/\s/g, '');
        try {
            await navigator.clipboard.writeText(acc);
            showToast('Número da conta copiado!', 'success');
            const old = btnCopy.textContent;
            btnCopy.textContent = 'Copiado!';
            setTimeout(() => {
                btnCopy.textContent = old;
            }, 1200);
        } catch {
            const input = document.createElement('input');
            input.value = acc;
            document.body.appendChild(input);
            input.select();
            document.execCommand('copy');
            document.body.removeChild(input);
            showToast('Número da conta copiado!', 'success');
            const old = btnCopy.textContent;
            btnCopy.textContent = 'Copiado!';
            setTimeout(() => {
                btnCopy.textContent = old;
            }, 1200);
        }
    });
}

function onlyDigits(input) {
    if (!input) return;
    input.addEventListener('input', () => {
        const cleaned = input.value.replace(/\D/g, '');
        if (cleaned !== input.value) {
            input.value = cleaned;
        }
    });
}

onlyDigits(document.getElementById('transferPageTo'));
onlyDigits(document.getElementById('modalLoginAccount'));

document.getElementById('btnLogout').addEventListener('click', () => {
    clearCurrentAccount();
});

document.getElementById('modalCreateForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const holderName = document.getElementById('modalCreateHolder').value.trim();
    const password = document.getElementById('modalCreatePass').value;
    const confirmPassword = document.getElementById('modalCreatePass2').value;
    const initialBalance = parseMoney(document.getElementById('modalCreateBalance').value);
    const resultDiv = document.getElementById('modalCreateResult');
    const submitBtn = e.target.querySelector('button[type="submit"]');

    if (!holderName) {
        resultDiv.innerHTML = '<div>Informe o nome do titular.</div>';
        resultDiv.classList.remove('success');
        resultDiv.classList.add('error');
        return;
    }
    if (password.length < 4) {
        resultDiv.innerHTML = '<div>A senha deve ter pelo menos 4 caracteres.</div>';
        resultDiv.classList.remove('success');
        resultDiv.classList.add('error');
        return;
    }
    if (password !== confirmPassword) {
        resultDiv.innerHTML = '<div>As senhas não coincidem.</div>';
        resultDiv.classList.remove('success');
        resultDiv.classList.add('error');
        return;
    }

    submitBtn.disabled = true;
    submitBtn.textContent = 'Criando...';
    resultDiv.innerHTML = '<div class="info-message">Criando conta...</div>';
    resultDiv.classList.remove('error', 'success');

    try {
        const response = await fetch('/api/accounts', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ holderName, password, confirmPassword, initialBalance })
        });
        const data = await response.json();
        if (response.ok) {
            resultDiv.classList.add('success');
            resultDiv.innerHTML = `<div>Conta criada! Número: <strong>${data.accountNumber}</strong></div>`;
            saveCurrentAccount({
                id: data.id,
                holderName: data.holderName,
                accountNumber: data.accountNumber,
                balance: data.balance,
                createdAt: data.createdAt
            });
            setTimeout(() => {
                closeModal('modalCreate');
                showPage('pageHome');
            }, 600);
        } else {
            resultDiv.classList.add('error');
            let msg = data.error || data.detail || JSON.stringify(data);
            if (Array.isArray(data)) msg = data.map(err => err.errorMessage || err.ErrorMessage).join('<br>');
            else if (data.errors) {
                const errors = Object.values(data.errors).flat();
                if (errors.length) msg = errors.join('<br>');
            }
            resultDiv.innerHTML = `<div><strong>Erro</strong><br>${msg}</div>`;
        }
    } catch (error) {
        resultDiv.classList.add('error');
        resultDiv.innerHTML = `<div><strong>Erro de conexão</strong><br>${error.message}</div>`;
    } finally {
        submitBtn.disabled = false;
        submitBtn.textContent = 'Criar conta';
    }
});

document.getElementById('modalLoginForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const accountNumber = document.getElementById('modalLoginAccount').value.replace(/\D/g, '');
    document.getElementById('modalLoginAccount').value = accountNumber;
    const password = document.getElementById('modalLoginPass').value;
    const resultDiv = document.getElementById('modalLoginResult');
    const submitBtn = e.target.querySelector('button[type="submit"]');

    submitBtn.disabled = true;
    submitBtn.textContent = 'Entrando...';
    resultDiv.innerHTML = '<div class="info-message">Validando...</div>';
    resultDiv.classList.remove('error', 'success');

    try {
        const response = await fetch('/api/accounts/access', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ accountNumber, password })
        });
        const data = await response.json();
        if (response.ok) {
            resultDiv.classList.add('success');
            resultDiv.innerHTML = '<div>Acesso liberado</div>';
            saveCurrentAccount(data);
            setTimeout(() => {
                closeModal('modalLogin');
                showPage('pageHome');
                refreshBalanceHome();
            }, 400);
        } else {
            resultDiv.classList.add('error');
            let msg = 'Número da conta ou senha inválidos.';
            if (response.status === 429) msg = 'Muitas tentativas. Tente novamente em instantes.';
            else if (data.detail) msg = data.detail;
            else if (data.error) msg = data.error;
            else if (data.title) msg = data.title;
            else if (Array.isArray(data)) msg = data.map(err => err.errorMessage || err.ErrorMessage).join('<br>');
            else if (data.errors) {
                const errors = Object.values(data.errors).flat();
                if (errors.length) msg = errors.join('<br>');
            }
            resultDiv.innerHTML = `<div><strong>Acesso negado</strong><br>${msg}</div>`;
        }
    } catch (error) {
        resultDiv.classList.add('error');
        resultDiv.innerHTML = `<div><strong>Erro de conexão</strong><br>${error.message}</div>`;
    } finally {
        submitBtn.disabled = false;
        submitBtn.textContent = 'Entrar';
    }
});

document.getElementById('depositPageForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!currentAccount) return;
    const amount = parseMoney(document.getElementById('depositPageAmount').value);
    const resultDiv = document.getElementById('depositPageResult');
    const submitBtn = e.target.querySelector('button[type="submit"]');

    if (amount <= 0) {
        resultDiv.classList.remove('success');
        resultDiv.classList.add('error');
        resultDiv.innerHTML = '<div>Informe um valor maior que zero.</div>';
        return;
    }

    submitBtn.disabled = true;
    submitBtn.textContent = 'Adicionando...';
    resultDiv.innerHTML = '<div class="info-message">Processando...</div>';
    resultDiv.classList.remove('error', 'success');

    try {
        const response = await fetch('/api/accounts/deposit', {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify({ accountNumber: currentAccount.accountNumber, amount })
        });
        const data = await response.json();
        if (response.ok) {
            resultDiv.classList.add('success');
            resultDiv.innerHTML = `
                <div style="text-align:center; margin-bottom:0.5rem;">
                    <div style="font-size:1.4rem; color:#16a34a;">✓</div>
                    <div style="font-weight:600; color:#166534;">Saldo adicionado</div>
                </div>
                <div class="transfer-amount">${formatCurrency(data.amount)}</div>
                <div style="text-align:center;">Novo saldo: <strong>${formatCurrency(data.newBalance)}</strong></div>
            `;
            currentAccount.balance = data.newBalance;
            saveCurrentAccount(currentAccount);
            document.getElementById('depositPageForm').reset();
            showToast('Saldo adicionado com sucesso!', 'success');
        } else {
            resultDiv.classList.add('error');
            let msg = data.error || data.detail || JSON.stringify(data);
            if (Array.isArray(data)) msg = data.map(err => err.errorMessage || err.ErrorMessage).join('<br>');
            else if (data.errors) {
                const errors = Object.values(data.errors).flat();
                if (errors.length) msg = errors.join('<br>');
            }
            resultDiv.innerHTML = `<div><strong>Não foi possível adicionar saldo</strong><br>${msg}</div>`;
            showToast('Não foi possível adicionar saldo', 'error');
        }
    } catch (error) {
        resultDiv.classList.add('error');
        resultDiv.innerHTML = `<div><strong>Erro de conexão</strong><br>${error.message}</div>`;
    } finally {
        submitBtn.disabled = false;
        submitBtn.textContent = 'Adicionar';
    }
});

document.getElementById('transferPageForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!currentAccount) return;
    const toAccount = document.getElementById('transferPageTo').value;
    const amount = parseMoney(document.getElementById('transferPageAmount').value);
    const resultDiv = document.getElementById('transferPageResult');
    const submitBtn = e.target.querySelector('button[type="submit"]');

    if (amount <= 0) {
        resultDiv.classList.remove('success');
        resultDiv.classList.add('error');
        resultDiv.innerHTML = '<div>Informe um valor maior que zero.</div>';
        return;
    }

    submitBtn.disabled = true;
    submitBtn.textContent = 'Transferindo...';
    resultDiv.innerHTML = '<div class="info-message">Processando...</div>';
    resultDiv.classList.remove('error', 'success');

    try {
        const response = await fetch('/api/transfers', {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify({ fromAccountNumber: currentAccount.accountNumber, toAccountNumber: toAccount, amount })
        });
        const data = await response.json();
        if (response.ok) {
            resultDiv.classList.add('success');
            resultDiv.innerHTML = `
                <div style="text-align:center; margin-bottom:0.5rem;">
                    <div style="font-size:1.4rem; color:#16a34a;">✓</div>
                    <div style="font-weight:600; color:#166534;">Transferência realizada</div>
                </div>
                <div class="transfer-amount">${formatCurrency(data.amount)}</div>
                <div class="transfer-details">
                    <div class="transfer-box">
                        <div class="label">De</div>
                        <div class="value">${data.fromAccountNumber}</div>
                    </div>
                    <div class="transfer-box">
                        <div class="label">Para</div>
                        <div class="value">${data.toAccountNumber}</div>
                    </div>
                </div>
            `;
            await refreshBalanceHome();
            document.getElementById('transferPageForm').reset();
            showToast('Transferência realizada com sucesso!', 'success');
        } else {
            resultDiv.classList.add('error');
            let msg = 'Não foi possível realizar a transferência.';
            if (data.error) msg = data.error;
            else if (data.detail) msg = data.detail;
            else if (Array.isArray(data)) msg = data.map(err => err.errorMessage || err.ErrorMessage).join('<br>');
            else if (data.errors) {
                const errors = Object.values(data.errors).flat();
                if (errors.length) msg = errors.join('<br>');
            }
            resultDiv.innerHTML = `<div>${msg}</div>`;
            showToast('Transferência não realizada', 'error');
        }
    } catch (error) {
        resultDiv.classList.add('error');
        resultDiv.innerHTML = `<div><strong>Erro de conexão</strong><br>${error.message}</div>`;
    } finally {
        submitBtn.disabled = false;
        submitBtn.textContent = 'Transferir';
    }
});

async function loadStatement() {
    if (!currentAccount) return;
    const resultDiv = document.getElementById('statementPageResult');
    resultDiv.innerHTML = '<div class="skeleton-list"><div class="skeleton-card"></div><div class="skeleton-card"></div><div class="skeleton-card"></div></div>';
    try {
        const response = await fetch(`/api/accounts/by-number/${currentAccount.accountNumber}/transactions?page=1&pageSize=50`, {
            headers: getAuthHeaders()
        });
        if (!response.ok) {
            resultDiv.innerHTML = `
                <div class="empty-state">
                    <div class="empty-state-icon">📄</div>
                    <div class="empty-state-title">Nenhuma movimentação ainda</div>
                    <div class="empty-state-subtitle">Assim que você fizer um depósito ou transferência, aparecerá aqui.</div>
                </div>
            `;
            return;
        }
        const data = await response.json();
        if (data) {
            const transactions = Array.isArray(data) ? data : (data.items || []);
            if (transactions.length === 0) {
                resultDiv.innerHTML = `
                    <div class="empty-state">
                        <div class="empty-state-icon">📄</div>
                        <div class="empty-state-title">Nenhuma movimentação ainda</div>
                        <div class="empty-state-subtitle">Assim que você fizer um depósito ou transferência, aparecerá aqui.</div>
                    </div>
                `;
                return;
            }
            let html = '<div class="statement-list">';
            transactions.forEach(tx => {
                const isDebit = tx.type === 0 || tx.type === 'Debit';
                const sign = isDebit ? '-' : '+';
                const typeLabel = isDebit ? 'Débito' : 'Crédito';
                const icon = isDebit ? '↓' : '↑';
                html += `
                    <div class="statement-card">
                        <div class="statement-card-left">
                            <div class="statement-icon ${isDebit ? 'debit' : 'credit'}">${icon}</div>
                            <div class="statement-info">
                                <div class="statement-type">
                                    ${typeLabel}
                                    <span class="statement-pill ${isDebit ? 'debit' : 'credit'}">${isDebit ? 'Saída' : 'Entrada'}</span>
                                </div>
                                <div class="statement-counterparty">Contraparte: ${tx.counterpartyAccountNumber || '-'}</div>
                                <div class="statement-date">${formatDate(tx.createdAt)}</div>
                            </div>
                        </div>
                        <div class="statement-amount ${isDebit ? 'debit' : 'credit'}">${sign} ${formatCurrency(tx.amount)}</div>
                    </div>
                `;
            });
            html += '</div>';
            if (data.totalCount) {
                html += `<div style="text-align:center; margin-top:0.8rem; color:var(--text-light); font-size:0.9rem;">Exibindo ${transactions.length} de ${data.totalCount} movimentações</div>`;
            }
            resultDiv.innerHTML = html;
        } else {
            resultDiv.innerHTML = `
                <div class="empty-state">
                    <div class="empty-state-icon">📄</div>
                    <div class="empty-state-title">Nenhuma movimentação ainda</div>
                    <div class="empty-state-subtitle">Assim que você fizer um depósito ou transferência, aparecerá aqui.</div>
                </div>
            `;
        }
    } catch (error) {
        resultDiv.innerHTML = `
            <div class="empty-state">
                <div class="empty-state-icon">📄</div>
                <div class="empty-state-title">Nenhuma movimentação ainda</div>
                <div class="empty-state-subtitle">Assim que você fizer um depósito ou transferência, aparecerá aqui.</div>
            </div>
        `;
    }
}

async function refreshBalanceHome() {
    if (!currentAccount) return;
    try {
        const response = await fetch(`/api/accounts/by-number/${currentAccount.accountNumber}`, {
            headers: getAuthHeaders()
        });
        const data = await response.json();
        if (response.ok) {
            currentAccount.balance = data.balance;
            try {
                sessionStorage.setItem('miniLedger_currentAccount', JSON.stringify(currentAccount));
            } catch(e){}
            document.getElementById('homeBalance').textContent = formatCurrency(data.balance);
            renderHome();
        }
    } catch {}
}

document.addEventListener('DOMContentLoaded', () => {
    loadCurrentAccount();
});