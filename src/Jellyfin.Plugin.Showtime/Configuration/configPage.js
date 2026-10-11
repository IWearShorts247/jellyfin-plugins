const pluginId = '7c3e5b2a-41d6-4f0e-9b8a-5d2c6e1f9a37';

const STEP_META = {
    Bumper: { label: 'Video bumper', color: '#7e57c2' },
    Trivia: { label: 'Trivia reel', color: '#26a69a' },
    Trailers: { label: 'Trailers', color: '#ef6c00' },
    Rating: { label: 'Rating card', color: '#c62828' },
    AudioFormat: { label: 'Audio format', color: '#1e88e5' },
    Feature: { label: 'Feature', color: '#fdd835' }
};

const CLASSIC = [
    ['Bumper', 'Trivia Intro'], ['Trivia'], ['Bumper', 'Trivia Outro'], ['Bumper', 'Trailers Intro'],
    ['Trailers', '', 2], ['Bumper', 'Trailers Outro'], ['Bumper', 'Feature Intro'], ['Bumper', 'Countdown'],
    ['Rating'], ['AudioFormat'], ['Feature']
];

function esc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

function newStep(type, category, count) {
    return { Type: type, Enabled: true, Category: category || '', Count: count || 1, TrailerSource: 'Library', PreferUnwatched: true, LimitToFeatureRating: true, Action: '' };
}

function classicSequence(name) {
    return { Name: name || 'Classic', Enabled: true, Genres: '', Users: '', IncludeEpisodes: false, FeatureEndAction: '', Steps: CLASSIC.map(s => newStep(...s)) };
}

export default function (view) {
    let config = null;
    let content = { Categories: [], Folders: [], Sections: {} };
    let current = 0;
    let dragIndex = null;
    const $ = sel => view.querySelector(sel);

    const markDirty = () => { $('#stDirty').textContent = 'Unsaved changes'; };
    const seq = () => config.Sequences[current];

    function actionOptions(selected) {
        const names = config.Actions.map(a => a.Name).filter(Boolean);
        return ['<option value="">— none —</option>']
            .concat(names.map(n => `<option value="${esc(n)}" ${n === selected ? 'selected' : ''}>${esc(n)}</option>`))
            .join('');
    }

    // ---------- Sequences ----------
    function renderSequenceList() {
        $('#stSequence').innerHTML = config.Sequences
            .map((s, i) => `<option value="${i}" ${i === current ? 'selected' : ''}>${esc(s.Name || 'Untitled')}${s.Enabled ? '' : ' (off)'}</option>`)
            .join('');
    }

    function renderSequence() {
        renderSequenceList();
        const s = seq();
        $('#stSeqName').value = s.Name;
        $('#stSeqGenres').value = s.Genres;
        $('#stSeqUsers').value = s.Users;
        $('#stSeqEnabled').checked = s.Enabled;
        $('#stSeqEpisodes').checked = s.IncludeEpisodes;
        $('#stSeqEnd').innerHTML = actionOptions(s.FeatureEndAction);
        renderSteps();
    }

    function stepFields(step, i) {
        const action = `<div class="st-field"><label>Fire action when it starts</label><select data-k="Action">${actionOptions(step.Action)}</select></div>`;
        switch (step.Type) {
            case 'Bumper': {
                const cats = content.Categories.length ? content.Categories : ['Countdown'];
                const opts = cats.concat(step.Category && !cats.includes(step.Category) ? [step.Category] : [])
                    .map(c => `<option ${c === step.Category ? 'selected' : ''}>${esc(c)}</option>`).join('');
                return `<div class="st-row">
                    <div class="st-field"><label>Category (Video Bumpers/…)</label><select data-k="Category"><option value="">— pick —</option>${opts}</select></div>
                    <div class="st-field" style="flex:0 0 6em"><label>How many</label><input type="number" min="1" max="10" data-k="Count" value="${step.Count}"></div>
                    ${action}</div>`;
            }
            case 'Trivia':
                return `<div class="st-row">
                    <div class="st-field" style="flex:0 0 6em"><label>Reels</label><input type="number" min="1" max="5" data-k="Count" value="${step.Count}"></div>
                    ${action}</div>`;
            case 'Trailers':
                return `<div class="st-row">
                    <div class="st-field" style="flex:0 0 6em"><label>How many</label><input type="number" min="1" max="10" data-k="Count" value="${step.Count}"></div>
                    <div class="st-field"><label>From</label><select data-k="TrailerSource">
                        <option value="Library" ${step.TrailerSource === 'Library' ? 'selected' : ''}>Other movies' local trailers</option>
                        <option value="TrailersFolder" ${step.TrailerSource === 'TrailersFolder' ? 'selected' : ''}>Content library "Trailers" folder</option>
                        <option value="Both" ${step.TrailerSource === 'Both' ? 'selected' : ''}>Both</option></select></div>
                    ${action}</div>
                    <div class="st-row">
                    <label class="st-check"><input type="checkbox" data-k="PreferUnwatched" ${step.PreferUnwatched ? 'checked' : ''}> Prefer movies the viewer hasn't seen</label>
                    <label class="st-check"><input type="checkbox" data-k="LimitToFeatureRating" ${step.LimitToFeatureRating ? 'checked' : ''}> No higher rating than the feature</label></div>`;
            case 'Rating':
                return `<div class="st-muted">Plays <code>Ratings Bumpers/&lt;system&gt;/&lt;rating&gt;</code> matching the feature (MPAA, BBFC, FSK, DEJUS).</div><div class="st-row">${action}</div>`;
            case 'AudioFormat':
                return `<div class="st-muted">Plays <code>Audio Format Bumpers/&lt;format&gt;</code> for the main audio track (Atmos → TrueHD → Dolby Digital…).</div><div class="st-row">${action}</div>`;
            case 'Feature':
                return `<div class="st-muted">The movie itself. Steps after this are ignored.</div><div class="st-row">${action}</div>`;
            default:
                return action;
        }
    }

    function renderSteps() {
        const steps = seq().Steps;
        const featureAt = steps.findIndex(s => s.Type === 'Feature' && s.Enabled);
        $('#stSteps').innerHTML = steps.map((step, i) => {
            const meta = STEP_META[step.Type] || { label: step.Type, color: '#888' };
            const cls = ['st-step', step.Enabled ? '' : 'disabled', featureAt >= 0 && i > featureAt ? 'after-feature' : ''].join(' ');
            return `<li class="${cls}" data-i="${i}" style="--st-color:${meta.color}">
                <div class="st-num" draggable="true" title="Drag to reorder"></div>
                <div class="st-step-body">
                    <div class="st-step-title">${esc(meta.label)}${step.Type === 'Bumper' && step.Category ? ': ' + esc(step.Category) : ''}</div>
                    ${stepFields(step, i)}
                </div>
                <div class="st-step-tools">
                    <button type="button" class="st-icon" data-op="toggle" title="${step.Enabled ? 'Disable' : 'Enable'}">${step.Enabled ? '◉' : '○'}</button>
                    <button type="button" class="st-icon" data-op="up" title="Move up">↑</button>
                    <button type="button" class="st-icon" data-op="down" title="Move down">↓</button>
                    <button type="button" class="st-icon" data-op="remove" title="Remove">✕</button>
                </div></li>`;
        }).join('');
    }

    function moveStep(from, to) {
        const steps = seq().Steps;
        if (to < 0 || to >= steps.length || from === to) return;
        const [s] = steps.splice(from, 1);
        steps.splice(to, 0, s);
        markDirty();
        renderSteps();
    }

    // ---------- Content ----------
    async function loadContent() {
        try {
            content = await ApiClient.getJSON(ApiClient.getUrl('Showtime/Content'));
        } catch (e) {
            content = { Categories: [], Folders: [], Sections: {} };
        }

        const sections = content.Sections || {};
        $('#stStats').innerHTML = content.Library
            ? Object.keys(sections).map(k => `<div class="st-stat"><b>${sections[k]}</b>${esc(k)}</div>`).join('')
                + `<div class="st-stat"><b>${content.Total}</b>videos in ${esc(content.Library)}</div>`
            : '<div class="st-muted">Pick a content library and save.</div>';
    }

    async function renderContent() {
        const folders = await ApiClient.getVirtualFolders();
        $('#stLibrary').innerHTML = '<option value="">— none —</option>' + folders
            .map(f => `<option value="${f.ItemId}" ${f.ItemId === config.ContentLibraryId ? 'selected' : ''}>${esc(f.Name)} (${esc(f.CollectionType || 'mixed')})</option>`)
            .join('');
        $('#stTriviaSource').value = config.TriviaSourcePath || '';
        $('#stMusic').value = config.MusicPath || '';
        $('#stReelPath').value = config.TriviaReelPath || '';
        $('#stReelCount').value = config.TriviaReelCount;
        $('#stReelMinutes').value = config.TriviaReelMinutes;
        $('#stQSec').value = config.QuestionSeconds;
        $('#stCSec').value = config.ClueSeconds;
        $('#stASec').value = config.AnswerSeconds;
        $('#stGenerate').checked = config.GenerateLibraryTrivia;
    }

    function readContent() {
        config.ContentLibraryId = $('#stLibrary').value;
        config.TriviaSourcePath = $('#stTriviaSource').value.trim();
        config.MusicPath = $('#stMusic').value.trim();
        config.TriviaReelPath = $('#stReelPath').value.trim();
        config.TriviaReelCount = parseInt($('#stReelCount').value, 10) || 6;
        config.TriviaReelMinutes = parseInt($('#stReelMinutes').value, 10) || 8;
        config.QuestionSeconds = parseInt($('#stQSec').value, 10) || 10;
        config.ClueSeconds = parseInt($('#stCSec').value, 10) || 5;
        config.AnswerSeconds = parseInt($('#stASec').value, 10) || 6;
        config.GenerateLibraryTrivia = $('#stGenerate').checked;
    }

    // ---------- Actions ----------
    function renderActions() {
        $('#stActions').innerHTML = config.Actions.map((a, i) => `<div class="st-card" data-a="${i}">
            <div class="st-row">
                <div class="st-field"><label>Name</label><input data-k="Name" value="${esc(a.Name)}" placeholder="Dim lights"></div>
                <div class="st-field" style="flex:0 0 7em"><label>Method</label><select data-k="Method">
                    ${['POST', 'GET', 'PUT'].map(m => `<option ${m === a.Method ? 'selected' : ''}>${m}</option>`).join('')}</select></div>
                <div class="st-field" style="flex:3 1 20em"><label>URL</label><input data-k="Url" value="${esc(a.Url)}" placeholder="https://homeassistant.local:8123/api/webhook/showtime-dim"></div>
                <div class="st-field" style="flex:0 0 7em"><label>Delay (s)</label><input type="number" min="0" data-k="DelaySeconds" value="${a.DelaySeconds || 0}"></div>
            </div>
            <div class="st-row">
                <div class="st-field"><label>Headers (one "Name: value" per line)</label><textarea data-k="Headers" placeholder="Authorization: Bearer …">${esc(a.Headers)}</textarea></div>
                <div class="st-field"><label>Body</label><textarea data-k="Body" placeholder='{"feature": "{feature}"}'>${esc(a.Body)}</textarea></div>
                <div class="st-field" style="flex:0 0 12em"><label>Content type</label><input data-k="ContentType" value="${esc(a.ContentType)}"></div>
            </div>
            <button is="emby-button" type="button" class="raised" data-op="test">Test</button>
            <button is="emby-button" type="button" class="raised" data-op="delete">Delete</button>
            <span class="st-muted" data-result></span>
        </div>`).join('') || '<p class="st-muted">No actions yet.</p>';
    }

    // ---------- Preview ----------
    let searchTimer = null;
    async function search(term) {
        if (!term || term.length < 2) { $('#stResults').innerHTML = ''; return; }
        const res = await ApiClient.getItems(ApiClient.getCurrentUserId(), {
            SearchTerm: term, IncludeItemTypes: 'Movie,Episode', Recursive: true, Limit: 8, Fields: 'ProductionYear'
        });
        $('#stResults').innerHTML = res.Items.map(it => `<button type="button" data-id="${it.Id}">${esc(it.Name)} ${it.ProductionYear ? '(' + it.ProductionYear + ')' : ''} <span class="st-muted">${esc(it.Type)}</span></button>`).join('');
    }

    async function preview(itemId) {
        $('#stResults').innerHTML = '';
        $('#stPreview').innerHTML = '<p class="st-muted">Building…</p>';
        const url = ApiClient.getUrl('Showtime/Preview', { itemId, userId: ApiClient.getCurrentUserId() });
        try {
            const p = await ApiClient.getJSON(url);
            const total = (p.Clips || []).reduce((t, c) => t + (c.Seconds || 0), 0);
            const rows = (p.Clips || []).map(c => `<tr><td>${c.Step}</td><td>${esc(c.Label)}</td><td>${esc(c.Name)}<div class="st-muted">${esc(c.Path)}</div></td><td>${c.Seconds ? Math.round(c.Seconds) + 's' : ''}</td><td>${esc(c.Action)}</td></tr>`).join('');
            $('#stPreview').innerHTML = `<div class="st-card">
                <h3 style="margin-top:0">${esc(p.Feature)}</h3>
                <div class="st-muted">Sequence <b>${esc(p.Sequence || '—')}</b> · rating ${esc(p.Rating)} · audio ${esc(p.Audio || 'unknown')} · pre-show ≈ ${Math.round(total / 60)} min</div>
                ${rows ? `<table class="st-table"><tr><th>Step</th><th>What</th><th>Clip</th><th>Length</th><th>Action</th></tr>${rows}
                    <tr><td></td><td>Feature</td><td>${esc(p.Feature)}</td><td></td><td>${esc(p.FeatureAction || '')}${p.FeatureEndAction ? ' · on stop: ' + esc(p.FeatureEndAction) : ''}</td></tr></table>` : ''}
                ${(p.Notes || []).map(n => `<div class="st-muted">• ${esc(n)}</div>`).join('')}
            </div>`;
        } catch (e) {
            $('#stPreview').innerHTML = '<p>Preview failed. Save first, and check the server log.</p>';
        }
    }

    // ---------- Load / save ----------
    async function save(silent) {
        readContent();
        Dashboard.showLoadingMsg();
        try {
            const result = await ApiClient.updatePluginConfiguration(pluginId, config);
            if (!silent) Dashboard.processPluginConfigurationUpdateResult(result);
            $('#stDirty').textContent = '';
            await loadContent();
        } finally {
            Dashboard.hideLoadingMsg();
        }
    }

    async function load() {
        Dashboard.showLoadingMsg();
        config = await ApiClient.getPluginConfiguration(pluginId);
        config.Sequences = config.Sequences || [];
        config.Actions = config.Actions || [];
        if (config.Sequences.length === 0) {
            config.Sequences.push(classicSequence());
        }
        current = Math.min(current, config.Sequences.length - 1);
        await loadContent();
        await renderContent();
        renderSequence();
        renderActions();
        $('#stDirty').textContent = '';
        Dashboard.hideLoadingMsg();
    }

    // ---------- Wiring ----------
    view.querySelectorAll('.st-tab').forEach(tab => tab.addEventListener('click', () => {
        view.querySelectorAll('.st-tab').forEach(t => t.classList.toggle('active', t === tab));
        view.querySelectorAll('.st-panel').forEach(p => p.classList.toggle('active', p.dataset.panel === tab.dataset.tab));
        if (tab.dataset.tab === 'sequences') renderSequence(); // pick up renamed actions
    }));

    $('#stSequence').addEventListener('change', e => { current = parseInt(e.target.value, 10); renderSequence(); });
    $('#stSeqNew').addEventListener('click', () => {
        config.Sequences.push({ Name: 'New sequence', Enabled: true, Genres: '', Users: '', IncludeEpisodes: false, FeatureEndAction: '', Steps: [newStep('Feature')] });
        current = config.Sequences.length - 1; markDirty(); renderSequence();
    });
    $('#stSeqCopy').addEventListener('click', () => {
        const copy = JSON.parse(JSON.stringify(seq())); copy.Name += ' copy';
        config.Sequences.push(copy); current = config.Sequences.length - 1; markDirty(); renderSequence();
    });
    $('#stSeqDelete').addEventListener('click', () => {
        if (!confirm(`Delete sequence "${seq().Name}"?`)) return;
        config.Sequences.splice(current, 1);
        if (config.Sequences.length === 0) config.Sequences.push(classicSequence());
        current = 0; markDirty(); renderSequence();
    });

    const bindSeq = (id, key, prop) => $(id).addEventListener('input', e => {
        seq()[key] = e.target[prop || 'value']; markDirty();
        if (key === 'Name' || key === 'Enabled') renderSequenceList();
    });
    bindSeq('#stSeqName', 'Name');
    bindSeq('#stSeqGenres', 'Genres');
    bindSeq('#stSeqUsers', 'Users');
    bindSeq('#stSeqEnabled', 'Enabled', 'checked');
    bindSeq('#stSeqEpisodes', 'IncludeEpisodes', 'checked');
    $('#stSeqEnd').addEventListener('change', e => { seq().FeatureEndAction = e.target.value; markDirty(); });

    $('#stAddStep').addEventListener('click', () => {
        const type = $('#stAddType').value;
        const steps = seq().Steps;
        const featureAt = steps.findIndex(s => s.Type === 'Feature');
        const step = newStep(type, type === 'Bumper' ? (content.Categories[0] || 'Countdown') : '');
        if (type !== 'Feature' && featureAt >= 0) steps.splice(featureAt, 0, step); else steps.push(step);
        markDirty(); renderSteps();
    });

    const stepsEl = $('#stSteps');
    stepsEl.addEventListener('click', e => {
        const btn = e.target.closest('[data-op]');
        if (!btn) return;
        const i = parseInt(btn.closest('.st-step').dataset.i, 10);
        const steps = seq().Steps;
        if (btn.dataset.op === 'up') moveStep(i, i - 1);
        else if (btn.dataset.op === 'down') moveStep(i, i + 1);
        else if (btn.dataset.op === 'remove') { steps.splice(i, 1); markDirty(); renderSteps(); }
        else if (btn.dataset.op === 'toggle') { steps[i].Enabled = !steps[i].Enabled; markDirty(); renderSteps(); }
    });
    stepsEl.addEventListener('change', e => {
        const k = e.target.dataset.k;
        if (!k) return;
        const step = seq().Steps[parseInt(e.target.closest('.st-step').dataset.i, 10)];
        step[k] = e.target.type === 'checkbox' ? e.target.checked : e.target.type === 'number' ? (parseInt(e.target.value, 10) || 1) : e.target.value;
        markDirty();
        if (k === 'Category') renderSteps();
    });
    stepsEl.addEventListener('dragstart', e => {
        const li = e.target.closest('.st-step');
        dragIndex = parseInt(li.dataset.i, 10);
        li.classList.add('dragging');
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', String(dragIndex));
        e.dataTransfer.setDragImage(li, 20, 20);
    });
    stepsEl.addEventListener('dragover', e => { if (dragIndex !== null) e.preventDefault(); });
    stepsEl.addEventListener('drop', e => {
        e.preventDefault();
        const li = e.target.closest('.st-step');
        if (li && dragIndex !== null) moveStep(dragIndex, parseInt(li.dataset.i, 10));
        dragIndex = null;
    });
    stepsEl.addEventListener('dragend', () => { dragIndex = null; view.querySelectorAll('.dragging').forEach(x => x.classList.remove('dragging')); });

    $('#stAddAction').addEventListener('click', () => {
        config.Actions.push({ Name: 'Action ' + (config.Actions.length + 1), Method: 'POST', Url: '', Headers: '', ContentType: 'application/json', Body: '', DelaySeconds: 0 });
        markDirty(); renderActions();
    });
    const actionsEl = $('#stActions');
    actionsEl.addEventListener('input', e => {
        const k = e.target.dataset.k;
        if (!k) return;
        const a = config.Actions[parseInt(e.target.closest('[data-a]').dataset.a, 10)];
        const old = a.Name;
        a[k] = e.target.type === 'number' ? (parseInt(e.target.value, 10) || 0) : e.target.value;
        if (k === 'Name') {
            // Keep step references pointing at the renamed action.
            config.Sequences.forEach(s => {
                if (s.FeatureEndAction === old) s.FeatureEndAction = a.Name;
                s.Steps.forEach(st => { if (st.Action === old) st.Action = a.Name; });
            });
        }
        markDirty();
    });
    actionsEl.addEventListener('click', async e => {
        const btn = e.target.closest('[data-op]');
        if (!btn) return;
        const card = btn.closest('[data-a]');
        const i = parseInt(card.dataset.a, 10);
        if (btn.dataset.op === 'delete') {
            config.Actions.splice(i, 1); markDirty(); renderActions();
        } else if (btn.dataset.op === 'test') {
            const out = card.querySelector('[data-result]');
            out.textContent = 'Saving and firing…';
            await save(true);
            const r = await ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('Showtime/Actions/Test', { name: config.Actions[i].Name }), dataType: 'json' });
            out.textContent = r.Result;
        }
    });

    $('#stLibrary').addEventListener('change', markDirty);
    view.querySelector('[data-panel="content"]').addEventListener('input', markDirty);
    $('#stRender').addEventListener('click', async () => {
        await save(true);
        const tasks = await ApiClient.getScheduledTasks({ isHidden: false });
        const task = tasks.find(t => t.Key === 'ShowtimeRenderTrivia');
        if (!task) { $('#stRenderStatus').textContent = 'Task not found.'; return; }
        await ApiClient.startScheduledTask(task.Id);
        $('#stRenderStatus').textContent = 'Rendering started. Progress is under Dashboard → Scheduled Tasks.';
    });

    $('#stSearch').addEventListener('input', e => { clearTimeout(searchTimer); searchTimer = setTimeout(() => search(e.target.value.trim()), 250); });
    $('#stResults').addEventListener('click', e => { const b = e.target.closest('[data-id]'); if (b) preview(b.dataset.id); });

    $('#stSave').addEventListener('click', () => save(false));
    view.addEventListener('viewshow', load);
}
