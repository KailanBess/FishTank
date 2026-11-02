'use strict';
(() => {
  const $ = id => document.getElementById(id);
  let session = '', csrf = '', entries = [], editing = null, busy = false, vaultOpen = false;
  let createVault = false, lastAction = Date.now(), secretTimer, revealTimer, pendingHideLock = false, generation = 0;
  const titles = { home: 'Device overview', alerts: 'Live alerts', vault: 'Passwords & connections' };
  function message(text, error = false) { $('message').textContent = text; $('message').classList.toggle('error', error); }
  function node(tag, text, className) { const element = document.createElement(tag); if (text !== undefined) element.textContent = text; if (className) element.className = className; return element; }
  function hideSecret() { clearTimeout(revealTimer); $('entry-password').type = 'password'; $('reveal-password').textContent = 'Show password'; }
  function closeEditor() { hideSecret(); clearTimeout(secretTimer); $('entry-form').reset(); $('entry-password').value = ''; $('entry-notes').value = ''; editing = null; if ($('editor').open) $('editor').close(); }
  function clearVault() { closeEditor(); entries = []; vaultOpen = false; $('account-list').replaceChildren(); $('search').value = ''; $('master').value = ''; $('confirm-master').value = ''; $('vault-open').hidden = true; $('vault-locked').hidden = false; }
  function disconnected(text) { generation++; session = csrf = '';  clearVault(); $('workspace').hidden = true; $('pair-panel').hidden = false; $('lock-session').disabled = true; $('findings').replaceChildren(); $('event-list').replaceChildren(); message(text, true); }
  async function api(path, body = {}) {
    const requestGeneration = generation;
    const response = await fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + session, 'X-FishTank-CSRF': csrf }, body: JSON.stringify(body), cache: 'no-store', credentials: 'omit', redirect: 'error' });
    const result = await response.json();
    if (requestGeneration !== generation) throw new Error('Browser is locked. Pair again to continue.');
    if (!response.ok) {
      if (response.status === 401) disconnected(result.error);
      if (response.status === 423) clearVault();
      throw new Error(result.error || 'Request failed.');
    }
    if (path !== '/api/status') lastAction = Date.now();
    return result;
  }
  async function run(action) {
    if (busy) return;
    busy = true; $('workspace').setAttribute('aria-busy', 'true');
    const controls = [...document.querySelectorAll('main button, dialog button')].filter(b => !b.disabled && b.id !== 'lock-session' && b.id !== 'close-editor');
    controls.forEach(b => { b.disabled = true; });
    try { await action(); } catch (error) { message(error.message || 'The local connection was interrupted.', true); }
    finally {
      busy = false; controls.forEach(b => { b.disabled = false; }); $('workspace').removeAttribute('aria-busy'); if ($('editor').open) configureEditor();
      if (pendingHideLock && session) { pendingHideLock = false; clearVault(); run(async () => { await api('/api/vault/lock'); }); }
    }
  }
  function showPage(page) {
    Object.keys(titles).forEach(id => { $(id).hidden = id !== page; });
    document.querySelectorAll('[data-page]').forEach(b => { b.classList.toggle('active', b.dataset.page === page); if (b.dataset.page === page) b.setAttribute('aria-current', 'page'); else b.removeAttribute('aria-current'); });
    $('page-title').textContent = titles[page];
    if (page === 'vault' && session) run(vaultState);
  }
  document.querySelectorAll('[data-page]').forEach(b => b.addEventListener('click', () => showPage(b.dataset.page)));
  document.querySelectorAll('[data-go]').forEach(b => b.addEventListener('click', () => showPage(b.dataset.go)));
  document.querySelector('.brand').addEventListener('click', event => { event.preventDefault(); showPage('home'); });
  async function refresh() {
    const status = await api('/api/status');
    $('live-heading').textContent = status.liveEnabled ? 'Listener enabled' : 'Optional · off';
    $('live-status').textContent = $('alerts-coverage').textContent = status.liveStatus;
    $('scan-status').textContent = status.lastScan;
    $('event-list').replaceChildren();
    if (!status.events.length) $('event-list').append(node('p', 'No retained alerts. This does not establish that the device is clean.', 'muted'));
    status.events.slice().reverse().forEach(event => { const item = node('article', undefined, 'event'); item.append(node('span', event.Kind, 'state'), node('p', event.Summary), node('time', event.recorded)); $('event-list').append(item); });
  }
  $('refresh-status').addEventListener('click', () => run(refresh));
  function renderFindings(result) {
    $('findings').replaceChildren();
    if (result.capturedUtc) $('findings').append(node('p', 'Recorded: ' + result.capturedUtc, 'small'));
    if (result.note || result.scope) $('findings').append(node('p', result.note || result.scope, 'muted'));
    (result.findings || []).forEach(finding => { const item = node('article', undefined, 'finding'); item.append(node('span', finding.State, 'state' + (finding.State === 'Review' ? ' review' : '')), node('h3', finding.Name), node('p', finding.Value, 'muted')); $('findings').append(item); });
  }
  [['check-security', '/api/check/security'], ['check-disk', '/api/check/disk'], ['check-backups', '/api/backups']].forEach(([id, path]) => $(id).addEventListener('click', () => run(async () => { message('Reading local evidence…'); renderFindings(await api(path)); message('Check complete. Review unknown results and recorded dates.'); })));
  $('quick-scan').addEventListener('click', () => run(async () => {
    if (!confirm('Request a Defender quick scan? Confirm it in the desktop app. Defender policy may quarantine or remove detected threats.')) return;
    message('Confirm this request in the FishTank desktop window.');
    const result = await api('/api/scan/quick'); message(result.accepted ? 'Scan requested. Follow progress and final results in the desktop app.' : 'Scan was not started. Check the desktop app.');
  }));
  $('lock-session').addEventListener('click', async () => {
    const token = session, nonce = csrf;
    disconnected('Browser locked. Open a new pairing link from FishTank to reconnect.');
    try { await fetch('/api/lock', { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + token, 'X-FishTank-CSRF': nonce }, body: '{}', credentials: 'omit', cache: 'no-store' }); } catch { /* Server also expires the session. */ }
  });
  async function vaultState() {
    const state = await api('/api/vault/state');
    createVault = !state.exists;
    $('confirm-master-row').hidden = !createVault; $('confirm-master').required = createVault;
    $('unlock-vault').textContent = createVault ? 'Create encrypted vault' : 'Unlock vault';
    $('master').minLength = createVault ? 12 : 1;
    $('vault-state').textContent = createVault ? 'Choose a unique master passphrase of at least 12 characters. There is no reset or recovery key.' : 'Vault found on this PC. Enter its master passphrase. Auto-lock: five minutes without a vault action.';
    if (state.unlocked) { vaultOpen = true; await loadEntries(); } else clearVault();
  }
  $('unlock-form').addEventListener('submit', event => { event.preventDefault(); run(async () => {
    const master = $('master').value, confirmMaster = $('confirm-master').value;
    $('master').value = $('confirm-master').value = '';
    message('Deriving vault encryption keys…');
    await api(createVault ? '/api/vault/create' : '/api/vault/unlock', { Master: master, Confirm: confirmMaster });
    vaultOpen = true; await loadEntries(); message('Vault unlocked. Keep the master passphrase private and make an encrypted backup from the desktop app.');
  }); });
  async function loadEntries() { entries = await api('/api/vault/list'); vaultOpen = true; $('vault-open').hidden = false; $('vault-locked').hidden = true; renderEntries(); }
  $('lock-vault').addEventListener('click', () => { clearVault(); run(async () => { await api('/api/vault/lock'); await vaultState(); message('Password vault locked.'); }); });
  function actionButton(text, action, quiet = true) { const button = node('button', text, quiet ? 'quiet' : ''); button.type = 'button'; button.addEventListener('click', action); return button; }
  function matches(entry, query) { return [entry.Title, entry.Username, entry.Website, entry.ConnectedEmail, entry.Email].join(' ').toLowerCase().includes(query); }
  function serviceCard(entry) {
    const item = node('article', undefined, 'service'); const heading = node('div', undefined, 'section-head'); const text = node('div');
    text.append(node('h3', entry.Title), node('p', entry.Username ? 'Username: ' + entry.Username : 'No separate username recorded', 'small'));
    heading.append(text, actionButton('View / edit', () => run(() => edit(entry.Id))));
    item.append(heading, node('p', (entry.Website ? entry.Website + ' · ' : '') + entry.Review, 'small')); return item;
  }
  function renderEntries() {
    const query = $('search').value.toLowerCase().trim(), container = $('account-list'); container.replaceChildren(); let visible = 0;
    entries.filter(e => e.Kind === 'Email').forEach(email => {
      const children = entries.filter(e => e.LinkedEmailId === email.Id), all = matches(email, query);
      const services = children.filter(e => all || matches(e, query));
      if (!all && !services.length) return;
      const group = node('article', undefined, 'account-group'), heading = node('div', undefined, 'account-heading'), details = node('div');
      details.append(node('span', 'EMAIL ACCOUNT', 'eyebrow'), node('h3', email.Email), node('p', email.Title + (email.Username ? ' · ' + email.Username : ''), 'small'));
      const actions = node('div', undefined, 'actions'); actions.append(actionButton('View / edit', () => run(() => edit(email.Id))), actionButton('Connect service', () => newEntry('Service', email.Id)));
      heading.append(details, actions); group.append(heading, node('p', children.length + ' connected service' + (children.length === 1 ? '' : 's') + ' · ' + email.Review, 'small'));
      if (services.length) { const links = node('div', undefined, 'connections'); services.forEach(e => links.append(serviceCard(e))); group.append(links); }
      container.append(group); visible++;
    });
    const standalone = entries.filter(e => e.Kind === 'Service' && !e.LinkedEmailId && matches(e, query));
    if (standalone.length) { const group = node('article', undefined, 'account-group'); group.append(node('p', 'SERVICES WITHOUT AN EMAIL CONNECTION', 'eyebrow')); standalone.forEach(e => group.append(serviceCard(e))); container.append(group); visible++; }
    if (!visible) container.append(node('p', entries.length ? 'No accounts match this search.' : 'Start with Add email, then connect your services. Existing standalone entries can be linked by editing them.', 'empty'));
  }
  $('search').addEventListener('input', renderEntries);
  function configureEditor() {
    const emailAccount = $('entry-kind').value === 'Email';
    $('connection-row').hidden = emailAccount; $('entry-email').required = emailAccount;
    $('entry-email').disabled = !emailAccount && !!$('entry-link').value;
    $('linked-signin').disabled = emailAccount || !$('entry-link').value;
    if ($('linked-signin').disabled) $('linked-signin').checked = false;
    $('entry-password').disabled = $('linked-signin').checked;
    $('entry-password').required = !$('linked-signin').checked;
    $('generate-password').disabled = $('linked-signin').checked;
  }
  function fillEditor(entry) {
    closeEditor(); editing = entry;
    $('entry-kind').value = entry.Kind; $('entry-title').value = entry.Title; $('entry-email').value = entry.Email;
    $('entry-link').replaceChildren(new Option('No linked email', ''));
    entries.filter(e => e.Kind === 'Email' && e.Id !== entry.Id).forEach(e => $('entry-link').add(new Option(e.Email + ' · ' + e.Title, e.Id)));
    $('entry-link').value = entry.LinkedEmailId; $('entry-username').value = entry.Username; $('entry-site').value = entry.Website;
    $('linked-signin').checked = entry.UsesLinkedSignIn; $('entry-password').value = entry.Password; $('entry-notes').value = entry.Notes;
    $('editor-title').textContent = entry.Id ? 'Account details' : 'Add account'; $('delete-entry').hidden = !entry.Id;
    configureEditor(); $('editor').showModal();
    secretTimer = setTimeout(() => { closeEditor(); message('Account details cleared after five minutes. Reopen them when needed.'); }, 300000);
  }
  function newEntry(kind, link = '') { if (busy || !vaultOpen) return; fillEditor({ Id: '', Kind: kind, Title: '', Email: '', LinkedEmailId: link, UsesLinkedSignIn: false, Username: '', Website: '', Password: '', Notes: '' }); }
  async function edit(id) { const entry = await api('/api/vault/get', { Id: id }); fillEditor(entry); }
  $('add-email').addEventListener('click', () => newEntry('Email'));
  $('add-service').addEventListener('click', () => newEntry('Service'));
  ['entry-kind', 'entry-link', 'linked-signin'].forEach(id => $(id).addEventListener('change', configureEditor));
  $('close-editor').addEventListener('click', closeEditor); $('editor').addEventListener('cancel', closeEditor);
  $('reveal-password').addEventListener('click', () => { if ($('entry-password').type === 'text') hideSecret(); else { $('entry-password').type = 'text'; $('reveal-password').textContent = 'Hide password'; revealTimer = setTimeout(hideSecret, 30000); } });
  $('generate-password').addEventListener('click', () => run(async () => { const result = await api('/api/vault/generate'); $('entry-password').value = result.Password; message('Generated locally. Save the account to store it.'); }));
  $('entry-form').addEventListener('submit', event => { event.preventDefault(); run(async () => {
    if (!editing) return;
    const linked = $('entry-kind').value === 'Service' ? $('entry-link').value : '';
    const value = { Id: editing.Id || crypto.randomUUID().replaceAll('-', ''), Kind: $('entry-kind').value, Title: $('entry-title').value, Email: linked ? '' : $('entry-email').value.trim(), LinkedEmailId: linked, UsesLinkedSignIn: $('linked-signin').checked, Username: $('entry-username').value, Website: $('entry-site').value, Password: $('linked-signin').checked ? '' : $('entry-password').value, Notes: $('entry-notes').value };
    await api('/api/vault/save', value); closeEditor(); await loadEntries(); message('Saved in the local encrypted vault. No service password was changed online.');
  }); });
  $('delete-entry').addEventListener('click', () => run(async () => { if (!editing || !confirm('Delete this account from the active vault? Encrypted backups may retain older copies.')) return; await api('/api/vault/delete', { Id: editing.Id }); closeEditor(); await loadEntries(); message('Account deleted from the active vault.'); }));
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) { hideSecret(); if (vaultOpen || busy) { clearVault(); if (busy) pendingHideLock = true; else run(async () => { await api('/api/vault/lock'); }); } }
  });
  setInterval(() => {
    if (!session || busy) return;
    if (Date.now() < lastAction || Date.now() - lastAction >= 300000) { $('lock-session').click(); return; }
    if (!document.hidden) run(refresh);
  }, 10000);
  const fragment = new URLSearchParams(location.hash.slice(1)); const pair = fragment.get('pair');
  history.replaceState(null, '', '/');
  if (pair) run(async () => {
    const result = await api('/api/pair', pair); session = result.session; csrf = result.csrf; lastAction = Date.now();
    $('workspace').hidden = false; $('pair-panel').hidden = true; $('lock-session').disabled = false;
    message('Paired to FishTank on this PC. Close or lock this session when finished.'); await refresh();
  });
})();
