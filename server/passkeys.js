'use strict';
(() => {
  const ticket = location.hash.slice(1);
  history.replaceState(null, '', '/passkeys');
  const button = document.getElementById('verify'), status = document.getElementById('status');
  if (!/^[A-Za-z0-9_-]{43}$/.test(ticket) || !window.isSecureContext || !navigator.credentials) {
    status.textContent = 'Open a fresh verification link from FishTank in a browser that supports passkeys.'; return;
  }
  const decode = value => Uint8Array.from(atob(value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - value.length % 4) % 4)), c => c.charCodeAt(0));
  const encode = value => btoa(String.fromCharCode(...new Uint8Array(value))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  const post = async (path, body) => {
    const reply = await fetch(path, {method: 'POST', credentials: 'omit', cache: 'no-store', redirect: 'error', headers: {'Content-Type': 'application/json'}, body: JSON.stringify({...body, ticket})});
    if (!reply.ok) throw new Error('Verification unavailable, expired or already used. Start again in FishTank.');
    return reply.json();
  };
  document.getElementById('context').textContent = 'Continue only if you just requested this verification in FishTank.';
  button.disabled = false;
  button.addEventListener('click', async () => {
    button.disabled = true; status.textContent = 'Waiting for your authenticator…';
    try {
      const flow = await post('/passkeys/options', {}), options = flow.publicKey;
      options.challenge = decode(options.challenge);
      if (options.user) options.user.id = decode(options.user.id);
      for (const key of ['allowCredentials', 'excludeCredentials']) if (options[key]) options[key] = options[key].map(c => ({...c, id: decode(c.id)}));
      const credential = flow.mode === 'register' ? await navigator.credentials.create({publicKey: options}) : await navigator.credentials.get({publicKey: options});
      const response = {clientDataJSON: encode(credential.response.clientDataJSON)};
      for (const key of ['attestationObject', 'authenticatorData', 'signature', 'userHandle']) if (credential.response[key]) response[key] = encode(credential.response[key]);
      if (credential.response.getTransports) response.transports = credential.response.getTransports();
      const result = await post('/passkeys/finish', {credential: {id: credential.id, rawId: encode(credential.rawId), type: credential.type, response, clientExtensionResults: credential.getClientExtensionResults()}});
      status.textContent = result.approved ? 'Verified. Return to FishTank and finish there. You may close this page.' : 'Verification failed. Start a fresh request in FishTank.';
    } catch (error) { status.textContent = error instanceof Error ? error.message : 'Verification failed. Start again in FishTank.'; }
  });
})();
