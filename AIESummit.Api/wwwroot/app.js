const $ = id => document.getElementById(id);
const config = await fetch('/firebase-config').then(response => response.json());
const emulator = 'http://' + location.hostname + ':' + config.emulatorPort + '/';
const identity = config.emulator ? emulator + 'identitytoolkit.googleapis.com/' : 'https://identitytoolkit.googleapis.com/';
const secureToken = config.emulator ? emulator + 'securetoken.googleapis.com/' : 'https://securetoken.googleapis.com/';
const apiKey = encodeURIComponent(config.apiKey);
let session = null;
let selected = null;
let recent = [];
let refreshInFlight = null;
let requestNumber = 0;
const imageUrls = [];
const statusLabels = {queued: 'Waiting for OCR', splitting: 'Identifying documents', ready: 'Ready', failed: 'Failed'};

async function jsonRequest(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.detail || error.error?.message || 'Request failed (' + response.status + ').');
  }
  return response.json();
}

async function token() {
  if (!session) throw new Error('Sign in to continue.');
  if (Date.now() < session.expires) return session.idToken;
  const active = session;
  refreshInFlight ??= jsonRequest(secureToken + 'v1/token?key=' + apiKey, {
    method: 'POST', body: new URLSearchParams({grant_type: 'refresh_token', refresh_token: active.refreshToken})
  }).finally(() => { refreshInFlight = null; });
  const result = await refreshInFlight;
  if (session !== active) throw new Error('Your session changed.');
  active.idToken = result.id_token;
  active.refreshToken = result.refresh_token;
  active.expires = Date.now() + (Number(result.expires_in) - 60) * 1000;
  return active.idToken;
}

async function api(path, options = {}) {
  return jsonRequest('/api/v1' + path, {...options, headers: {Authorization: 'Bearer ' + await token()}});
}

function showError(error) { $('notice').textContent = error.message; }

function clearImages() { while (imageUrls.length) URL.revokeObjectURL(imageUrls.pop()); }

$('auth-form').addEventListener('submit', async event => {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  const submitter = event.submitter;
  submitter.disabled = true;
  $('notice').textContent = '';
  try {
    const result = await jsonRequest(identity + 'v1/accounts:' + submitter.value + '?key=' + apiKey, {
      method: 'POST', headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({email: form.get('email'), password: form.get('password'), returnSecureToken: true})
    });
    session = {...result, expires: Date.now() + (Number(result.expiresIn) - 60) * 1000};
    $('auth-form').reset();
    $('auth').hidden = true;
    $('workspace').hidden = $('signout').hidden = false;
    await list();
  } catch (error) { showError(error); }
  finally { submitter.disabled = false; }
});

$('signout').addEventListener('click', () => {
  session = selected = null;
  recent = [];
  requestNumber++;
  clearImages();
  $('uploads').replaceChildren();
  $('documents').replaceChildren();
  $('upload').reset();
  $('workspace').hidden = $('detail').hidden = $('signout').hidden = true;
  $('auth').hidden = false;
  $('notice').textContent = '';
});

$('upload').addEventListener('submit', async event => {
  event.preventDefault();
  const active = session;
  const form = event.currentTarget;
  const button = event.submitter;
  button.disabled = true;
  $('notice').textContent = '';
  try {
    const result = await api('/uploads', {method: 'POST', body: new FormData(form)});
    if (session !== active) return;
    form.reset();
    await Promise.all([list(), detail(result.id)]);
  } catch (error) { if (session === active) showError(error); }
  finally { button.disabled = false; }
});

async function list(before) {
  const active = session;
  const rows = await api('/uploads' + (before ? '?before=' + before : ''));
  if (session !== active) return;
  const byId = new Map(recent.map(row => [row.id, row]));
  for (const row of rows) byId.set(row.id, row);
  recent = [...byId.values()].sort((a, b) => b.id - a.id);
  if (before || recent.length <= 50) $('more').hidden = rows.length < 50;
  const items = recent.map(upload => {
    const li = document.createElement('li');
    const button = document.createElement('button');
    button.textContent = upload.fileName;
    button.className = 'secondary';
    button.addEventListener('click', () => detail(upload.id).catch(showError));
    const state = document.createElement('span');
    state.textContent = statusLabels[upload.status];
    li.append(button, state);
    return li;
  });
  if (!items.length) {
    const empty = document.createElement('li');
    empty.textContent = 'Your uploads will appear here.';
    items.push(empty);
  }
  $('uploads').replaceChildren(...items);
}

async function detail(id) {
  selected = id;
  const seq = ++requestNumber;
  const result = await api('/uploads/' + id);
  if (seq !== requestNumber) return;
  $('detail').hidden = false;
  $('detail-title').textContent = result.upload.fileName;
  $('detail-status').textContent = result.upload.error || statusLabels[result.upload.status];
  $('documents').replaceChildren();
  clearImages();
  for (const doc of result.documents) {
    const article = document.createElement('article');
    const title = document.createElement('h3');
    title.textContent = doc.title;
    const pages = document.createElement('p');
    pages.textContent = doc.firstPage === doc.lastPage ? 'Page ' + doc.firstPage : 'Pages ' + doc.firstPage + '–' + doc.lastPage;
    const markdown = document.createElement('pre');
    markdown.textContent = doc.markdown;
    article.append(title, pages, markdown);
    for (const image of doc.images) {
      const button = document.createElement('button');
      button.className = 'secondary';
      button.textContent = 'View image from page ' + image.page;
      button.addEventListener('click', async () => {
        button.disabled = true;
        try {
          const response = await fetch('/api/v1/uploads/' + id + '/images/' + image.id, {headers: {Authorization: 'Bearer ' + await token()}});
          if (!response.ok) throw new Error('Could not load image.');
          const blob = await response.blob();
          if (seq !== requestNumber) return;
          const picture = document.createElement('img');
          picture.alt = image.reference;
          picture.src = URL.createObjectURL(blob);
          imageUrls.push(picture.src);
          const figure = document.createElement('figure');
          figure.append(picture);
          button.replaceWith(figure);
        } catch (error) { showError(error); button.disabled = false; }
      });
      article.append(button);
    }
    $('documents').append(article);
  }
}

$('more').addEventListener('click', () => list(recent.at(-1)?.id).catch(showError));
let polling = false;
setInterval(async () => {
  if (!session || polling || !recent.some(u => u.status === 'queued' || u.status === 'splitting')) return;
  polling = true;
  const previous = recent.find(u => u.id === selected)?.status;
  try {
    await list();
    if (selected && previous !== recent.find(u => u.id === selected)?.status) await detail(selected);
  } catch (error) { if (session) showError(error); }
  finally { polling = false; }
}, 4000);
