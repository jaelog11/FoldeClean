/* FoldeClean UI (문구는 i18n.js 의 t() 사용) */
'use strict';
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmtSize = b => { if (b < 1024) return b + ' B'; const u = ['KB','MB','GB','TB']; let i = -1; do { b /= 1024; i++; } while (b >= 1024 && i < u.length - 1); return b.toFixed(b < 10 ? 1 : 0) + ' ' + u[i]; };
const fmtNum = n => Number(n).toLocaleString();
const COLORS = ['--c1','--c2','--c3','--c4','--c5','--c6','--c7','--c8','--c9','--c10'].map(v => `var(${v})`);

// ---------------------------------------------------------------- API (WebView2 창 / pywebview / --web 모드)
const wv2 = window.chrome && window.chrome.webview ? window.chrome.webview : null;
const rpc = { n: 0, pending: new Map() };
if (wv2) wv2.addEventListener('message', e => { const d = e.data; const p = rpc.pending.get(d.id); if (p) { rpc.pending.delete(d.id); p(d.result); } });
const api = new Proxy({}, { get: (_, name) => (...args) => {
  if (wv2) return new Promise(res => { const id = ++rpc.n; rpc.pending.set(id, res); wv2.postMessage({ id, name, args }); });
  if (window.pywebview && window.pywebview.api) return window.pywebview.api[name](...args);
  return fetch('/api/' + name, { method: 'POST', body: JSON.stringify(args) }).then(r => r.json());
}});

const state = { step: 1, root: '', scan: null, strategies: [], strategy: 'type', includeWarn: false, plan: null, selected: new Set(), lastJournal: null, execRoot: '' };

// ---------------------------------------------------------------- 공통 UI
function toast(msg, ms = 2600) { const el = $('#toast'); el.textContent = msg; el.hidden = false; clearTimeout(el._t); el._t = setTimeout(() => el.hidden = true, ms); }
function busy(text) { const b = $('#busy'); if (text) { $('#busyText').textContent = text; $('#busyBar').style.width = '0%'; $('#busyBar').classList.add('indet'); $('#busyPct').textContent = ''; $('#busyElapsed').textContent = ''; b.hidden = false; } else b.hidden = true; }
const PHASES = {
  scan: p => t('busy.scan', { n: fmtNum(p.count) }), prepare: p => t('busy.prepare', { n: fmtNum(p.total) }),
  analyze: p => t('busy.analyze', { n: fmtNum(p.count), t: fmtNum(p.total) }), locks: p => t('busy.locks', { n: fmtNum(p.count), t: fmtNum(p.total) }),
  hash: p => t('busy.hash', { n: fmtSize(p.count), t: fmtSize(p.total) }), plan: p => t('busy.plan', { n: fmtNum(p.count), t: fmtNum(p.total) }),
};
function watchProgress(fallback) {
  const t0 = Date.now();
  return setInterval(async () => {
    const p = await api.get_progress(); const f = PHASES[p.phase];
    $('#busyText').textContent = f ? f(p) : fallback;
    $('#busyElapsed').textContent = t('busy.elapsed', { s: Math.round((Date.now() - t0) / 1000) });
    const bar = $('#busyBar');
    if (p.total > 0) { const pct = Math.min(100, p.count / p.total * 100); bar.classList.remove('indet'); bar.style.width = pct + '%'; $('#busyPct').textContent = Math.round(pct) + '%'; }
    else { bar.classList.add('indet'); $('#busyPct').textContent = ''; }
  }, 250);
}
function go(step) {
  state.step = step;
  $$('.panel').forEach(p => p.classList.toggle('active', p.id === 'p' + step));
  $$('.step').forEach(b => { const n = +b.dataset.step; b.classList.toggle('active', n === step); b.classList.toggle('done', n < step); if (n <= step) b.disabled = false; });
  $('.main').scrollTop = 0;
  if (step === 3) requestAnimationFrame(() => runDemo(state.strategy));
  if (step === 4 && state.plan) requestAnimationFrame(() => renderFlow(state.plan.flows, state.plan.src_groups, state.plan.dest_tree));
}
document.addEventListener('click', e => { const g = e.target.closest('[data-go]'); if (g) go(+g.dataset.go); const s = e.target.closest('.step'); if (s && !s.disabled) go(+s.dataset.step); });

// ---------------------------------------------------------------- 언어
function initLang() {
  for (const id of ['#langSel', '#langSelW']) {
    const sel = $(id);
    sel.innerHTML = Object.entries(LANG_NAMES).map(([k, v]) => `<option value="${k}" ${k === LANG ? 'selected' : ''}>${v}</option>`).join('');
    sel.onchange = async () => { LANG = sel.value; try { localStorage.setItem('lang', LANG); } catch (e) { } $('#langSel').value = LANG; $('#langSelW').value = LANG; await api.set_lang(LANG); rerender(); };
  }
  api.set_lang(LANG);
  applyI18n();
}
// 첫 실행 안내 (한 번 확인하면 다시 보이지 않음)
function initWelcome() {
  let seen = false; try { seen = localStorage.getItem('welcomeSeen') === '1'; } catch (e) { }
  if (seen) return;
  $('#welcome').hidden = false;
  $('#btnWelcomeOk').onclick = () => { $('#welcome').hidden = true; try { localStorage.setItem('welcomeSeen', '1'); } catch (e) { } };
}
// 오류 보고서 저장 (바탕화면에 txt, 파일 이름 같은 개인 정보는 담지 않음)
$('#btnReport').onclick = async () => {
  const note = prompt(t('report.prompt'), '') ?? '';
  busy(t('report.saving')); const r = await api.save_report(note); busy(null);
  if (r.error) return toast(r.error);
  toast(t('report.saved', { path: r.path }), 6000);
};
function rerender() {   // 언어가 바뀌면 현재 화면을 새 문구로 다시 그린다
  applyI18n(); initDepth(); initVersion(); initFolders(); initEdition();
  if (state.scan) renderScan(state.scan);
  if (state.strategies.length) { renderStrategyCards(); selectStrategy(state.strategy); }
  if (state.plan) renderPlan(state.plan);
}

// ---------------------------------------------------------------- 1. 폴더
async function initVersion() {
  try { const i = await api.app_info(); const v = $('#version'); v.textContent = `${i.name} v${i.version}`; v.title = `${i.engine} · ${i.journals}`; $('#owner').textContent = i.owner || t('app.owner'); document.title = `${i.name} ${i.version}`; } catch (e) { $('#owner').textContent = t('app.owner'); }
}
async function initFolders() {
  const list = await api.default_folders();
  $('#quickFolders').innerHTML = list.map(f => `<span class="chip" data-path="${esc(f.path)}">${esc(t('folder.' + f.key) === 'folder.' + f.key ? f.label : t('folder.' + f.key))}</span>`).join('');
  $$('#quickFolders .chip').forEach(c => c.onclick = () => $('#rootInput').value = c.dataset.path);
}
$('#btnPick').onclick = async () => { const p = await api.pick_folder(); if (p) $('#rootInput').value = p; else if (!window.pywebview && !wv2) toast(t('toast.browser')); };
// 검사 범위: 0 = 이 폴더의 파일만(기본), 2 = 하위 2단계, 50 = 전부
const DEPTHS = [[0, 'p1.depth0'], [2, 'p1.depth2'], [50, 'p1.depthAll']];
function initDepth() {
  const sel = $('#optDepth'); const cur = sel.value || '0';
  sel.innerHTML = DEPTHS.map(([v, k]) => `<option value="${v}" ${String(v) === cur ? 'selected' : ''}>${esc(t(k))}</option>`).join('');
}
async function runScan(depth) {
  const root = $('#rootInput').value.trim();
  if (!root) return toast(t('toast.needpath'));
  if (depth !== undefined) $('#optDepth').value = String(depth);
  state.root = root; busy(t('busy.folder'));
  const poll = watchProgress(t('busy.folder'));
  const res = await api.scan_folder(root, $('#optLocks').checked, +$('#optDepth').value);
  clearInterval(poll); busy(null);
  if (res.error) return toast(res.error);
  state.scan = res; renderScan(res); go(2);
}
$('#btnScan').onclick = () => runScan();

// ---------------------------------------------------------------- 2. 검사 결과
function renderScan(res) {
  const s = res.summary, r = res.risk.stats;
  $('#scanRoot').textContent = s.root;
  renderScanNote(res);
  state.dupeExact = null;                               // 새 검사이므로 중복 확정값은 버린다
  if (state.strategies.length) renderStrategyCards();   // 중복 예상 배지를 카드에 반영
  $('#tiles').innerHTML = [
    [t('p2.files'), fmtNum(s.file_count) + t('unit.files'), ''], [t('p2.size'), fmtSize(s.total_size), ''],
    [t('p2.safe'), fmtNum(r.safe) + t('unit.files'), 'ok'], [t('p2.wb'), `${fmtNum(r.warn)} / ${fmtNum(r.block)}`, r.block ? 'bad' : 'warn'],
  ].map(([l, v, c]) => `<div class="tile ${c}"><div class="v">${v}</div><div class="l">${l}</div></div>`).join('');
  const max = Math.max(...res.categories.map(c => c.size), 1);
  const catLabel = c => c.key ? t('cat.' + c.key) : c.label;
  $('#catBars').innerHTML = res.categories.slice(0, 9).map((c, i) => `<div class="bar-row"><span>${esc(catLabel(c))}</span><div class="bar-track"><div class="bar-fill" style="width:0;background:${COLORS[i % COLORS.length]}" data-w="${(c.size / max * 100).toFixed(1)}"></div></div><span class="muted small">${fmtNum(c.count)}${t('unit.files')} · ${fmtSize(c.size)}</span></div>`).join('');
  requestAnimationFrame(() => $$('#catBars .bar-fill').forEach(b => b.style.width = b.dataset.w + '%'));
  donut([[t('risk.safe'), r.safe, 'var(--ok)'], [t('risk.warn'), r.warn, 'var(--warn)'], [t('risk.block'), r.block, 'var(--bad)']]);
  $('#flaggedCount').textContent = fmtNum(res.flagged_total);
  if (res.risk.lock_timed_out) toast(t('toast.locktimeout', { n: fmtNum(res.risk.lock_checked) }), 6000);
  renderFlagged(res.flagged, s.root);
  $('#flagFilter').oninput = e => { const q = e.target.value.toLowerCase(); renderFlagged(res.flagged.filter(f => f.name.toLowerCase().includes(q) || f.path.toLowerCase().includes(q) || f.reasons.map(tCode).join(' ').toLowerCase().includes(q)), s.root); };
}
// 같은 프로그램·프로젝트·폴더에서 나온 파일은 묶어서 보여준다
function renderFlagged(items, root) {
  if (!items.length) { $('#flaggedList').innerHTML = `<div class="muted">${t('p2.none')}</div>`; return; }
  const groups = new Map();
  for (const f of items) { const k = f.group || f.path.slice(0, f.path.lastIndexOf('\\')); if (!groups.has(k)) groups.set(k, []); groups.get(k).push(f); }
  const rank = { block: 0, warn: 1, safe: 2 };
  const arr = [...groups.entries()].map(([k, fs]) => ({ key: k, files: fs, risk: fs.reduce((a, f) => rank[f.risk] < rank[a] ? f.risk : a, 'safe'), size: fs.reduce((a, f) => a + f.size, 0),
    reasons: [...new Set(fs.flatMap(f => f.reasons.map(c => c.split('|')[0])))] }));
  arr.sort((a, b) => rank[a.risk] - rank[b.risk] || b.files.length - a.files.length);
  const rel = p => { const r = p.startsWith(root) ? p.slice(root.length).replace(/^\\/, '') : p; return r || t('p4.from').split(' ')[0]; };
  $('#flaggedList').innerHTML = arr.slice(0, 300).map((g, gi) => {
    const single = g.files.length === 1;
    const head = `<div class="ghead" data-g="${gi}"><span class="caret">${single ? '' : '▸'}</span><span class="pill ${g.risk}">${t('risk.' + g.risk)}</span><div><div class="name" title="${esc(g.key)}">📁 ${esc(rel(g.key))} <span class="badge">${fmtNum(g.files.length)}${t('unit.files')}</span></div><div class="reasons">${esc(g.reasons.map(k => t(k)).join(' · '))}</div></div><span class="muted small">${fmtSize(g.size)}</span></div>`;
    const body = `<div class="gbody" ${single ? '' : 'hidden'}>${g.files.slice(0, 200).map(f => `<div class="item"><span class="pill ${f.risk}">${t('risk.' + f.risk)}</span><div><div class="name" title="${esc(f.path)}">${esc(f.name)}</div><div class="reasons">${esc(f.reasons.map(tCode).join(' · '))}</div></div><span class="muted small">${fmtSize(f.size)}</span></div>`).join('')}${g.files.length > 200 ? `<div class="muted small">+${fmtNum(g.files.length - 200)}</div>` : ''}</div>`;
    return `<div class="group ${single ? 'single' : ''}">${head}${body}</div>`;
  }).join('');
  $$('#flaggedList .ghead').forEach(h => h.onclick = () => { const g = h.parentElement; if (g.classList.contains('single')) return; const b = $('.gbody', g); b.hidden = !b.hidden; $('.caret', h).textContent = b.hidden ? '▸' : '▾'; });
}
// 검사 범위 안내: 하위 폴더를 건너뛰었거나, 반대로 아주 많은 파일을 끌어왔을 때 알려준다
function renderScanNote(res) {
  const el = $('#scanNote');
  const n = res.summary.file_count, skipped = res.skipped_dirs || 0, depth = res.depth ?? 0;
  if (depth === 0 && skipped > 0) {
    el.innerHTML = t('p2.note.top', { n: fmtNum(skipped) }) + ` <button id="btnRescanAll">${esc(t('p2.note.all'))}</button>`;
    el.hidden = false;
    $('#btnRescanAll').onclick = () => runScan(50);
  } else if (depth > 0 && n > 20000) {
    el.innerHTML = t('p2.note.many', { n: fmtNum(n) }) + ` <button id="btnRescanTop">${esc(t('p2.note.toponly'))}</button>`;
    el.hidden = false;
    $('#btnRescanTop').onclick = () => runScan(0);
  } else el.hidden = true;
}
function donut(parts) {
  const total = parts.reduce((a, p) => a + p[1], 0) || 1; let acc = 0; const R = 44, C = 2 * Math.PI * R;
  const segs = parts.map(([l, v, c]) => { const len = v / total * C; const s = `<circle cx="60" cy="60" r="${R}" fill="none" stroke="${c}" stroke-width="16" stroke-dasharray="${len} ${C - len}" stroke-dashoffset="${-acc}" transform="rotate(-90 60 60)"><animate attributeName="stroke-dasharray" from="0 ${C}" to="${len} ${C - len}" dur=".8s" fill="freeze"/></circle>`; acc += len; return s; }).join('');
  $('#donut').innerHTML = segs + `<text x="60" y="64" text-anchor="middle" font-size="14" font-weight="700" fill="currentColor">${Math.round(parts[0][1] / total * 100)}%</text>`;
  $('#riskLegend').innerHTML = parts.map(([l, v, c]) => `<div><i style="background:${c}"></i>${l} <b>${fmtNum(v)}</b></div>`).join('');
}

// ---------------------------------------------------------------- 3. 정리 방식
const OPTS = {
  type: [['keep_subdirs', 'checkbox', 'opt.keep', 'opt.keep.s']],
  date: [['granularity', 'select', 'opt.gran', '', [['month', 'opt.month'], ['year', 'opt.year']]], ['keep_subdirs', 'checkbox', 'opt.keep', '']],
  type_date: [['keep_subdirs', 'checkbox', 'opt.keep', '']],
  para: [['active_days', 'number', 'opt.active', 'opt.active.s', 30], ['resource_days', 'number', 'opt.resource', 'opt.resource.s', 180]],
  johnny: [['keep_subdirs', 'checkbox', 'opt.keep', '']],
  archive_old: [['days', 'number', 'opt.days', 'opt.days.s', 180], ['keep_subdirs', 'checkbox', 'opt.keep', '']],
  dedupe: [['min_size', 'number', 'opt.minsize', 'opt.minsize.s', 1024]],
};
const DEMO = () => ({
  type: { bins: [t('cat.documents'), t('cat.images'), t('cat.videos'), t('cat.other')], files: [['report.pdf', 0], ['photo.jpg', 1], ['clip.mp4', 2], ['memo.txt', 0], ['scan.png', 1], ['data.bin', 3]] },
  date: { bins: ['2024/03', '2025/01', '2025/07', '2026/09'], files: [['minutes.docx', 0], ['photo.jpg', 2], ['draft.hwp', 1], ['receipt.pdf', 3], ['capture.png', 2], ['backup.zip', 0]] },
  type_date: { bins: [t('cat.documents') + '/2024', t('cat.documents') + '/2025', t('cat.images') + '/2025', t('cat.images') + '/2026'], files: [['report.pdf', 0], ['photo.jpg', 2], ['contract.docx', 1], ['capture.png', 3], ['memo.txt', 1], ['scan.jpg', 2]] },
  para: { bins: ['1_Projects', '3_Resources', '4_Archives'], files: [['plan.docx', 0], ['ref.pdf', 1], ['2022.xlsx', 2], ['week.pptx', 0], ['old.jpg', 2], ['manual.pdf', 1]] },
  johnny: { bins: ['10-19 ' + t('cat.documents'), '20-29 ' + t('cat.images'), '30-39 ' + t('cat.videos'), '50-59 ' + t('cat.archives')], files: [['report.pdf', 0], ['photo.jpg', 1], ['clip.mp4', 2], ['backup.zip', 3], ['memo.txt', 0], ['capture.png', 1]] },
  archive_old: { bins: [t('demo.keep'), 'Archive/2023', 'Archive/2024'], files: [['week.docx', 0], ['old.pdf', 1], ['2024.jpg', 2], ['wip.xlsx', 0], ['2023.zip', 1]] },
  dedupe: { bins: [t('demo.orig'), t('cat.other') === 'cat.other' ? '_dup' : t('strat.dedupe.ex').split('/')[0]], files: [['photo.jpg', 0], ['photo (1).jpg', 1], ['report.pdf', 0], ['report copy.pdf', 1], ['photo (2).jpg', 1]] },
});
async function initStrategies() { state.strategies = await api.strategies(); renderStrategyCards(); selectStrategy(state.strategy); }
// 중복 예상: 검사할 때 크기만으로 미리 계산해 둔 값. 파일을 읽지 않아 즉시 알 수 있다.
// 내용까지 확인하면 state.dupeExact 에 담기고, 배지와 안내가 확정값으로 바뀐다.
const DUPE_AUTO_BYTES = 100 * 1024 * 1024;   // 읽을 양이 이보다 적으면 고르는 즉시 자동 확인
function dupeBadge(id) {
  if (id !== 'dedupe') return '';
  const h = state.scan && state.scan.dupe_hint;
  if (!h) return '';
  const ex = state.dupeExact;
  if (ex) return ex.duplicates
    ? `<span class="dbadge maybe">${esc(t('dup.found', { n: fmtNum(ex.duplicates) }))}</span>`
    : `<span class="dbadge none">${esc(t('dup.none'))}</span>`;
  return h.candidates ? `<span class="dbadge maybe">${esc(t('dup.maybe', { n: fmtNum(h.candidates) }))}</span>`
                      : `<span class="dbadge none">${esc(t('dup.none'))}</span>`;
}
function renderStrategyCards() {
  $('#stratGrid').innerHTML = state.strategies.map(s => `<div class="scard ${s.id === state.strategy ? 'sel' : ''}" data-id="${s.id}"><span class="tag">${esc(t(`strat.${s.id}.tag`))}</span><b>${esc(t(`strat.${s.id}.name`))}</b><p>${esc(t(`strat.${s.id}.desc`))}</p>${dupeBadge(s.id)}</div>`).join('');
  $$('.scard').forEach(c => c.onclick = () => selectStrategy(c.dataset.id));
}
function renderDupeBlock() {
  const h = state.scan && state.scan.dupe_hint;
  if (!h) return `<div class="dupebox"><span class="muted small">${esc(t('dup.needscan'))}</span></div>`;
  if (!h.candidates) return `<div class="dupebox none">${esc(t('dup.hint.none'))}</div>`;
  const ex = state.dupeExact;
  if (ex) return ex.duplicates
    ? `<div class="dupebox"><b class="ok">${esc(t('dup.confirmed', { groups: fmtNum(ex.groups), n: fmtNum(ex.duplicates), size: fmtSize(ex.reclaim) }))}</b></div>`
    : `<div class="dupebox none">${esc(t('dup.confirmed0'))}</div>`;
  const auto = (h.candidate_bytes || 0) <= DUPE_AUTO_BYTES;
  return `<div class="dupebox"><div>${t('dup.hint.some', { n: fmtNum(h.candidates), size: fmtSize(h.max_reclaim) })}</div>
    <div class="row">${auto ? `<span class="small muted" id="dupeResult">${esc(t('dup.checking'))}</span>`
                            : `<button class="ghost small" id="btnDupeCheck">${esc(t('dup.check'))}</button><span class="small" id="dupeResult"></span>`}</div></div>`;
}
/// 내용을 읽어 확정한다. silent 면 전체 화면 진행 창을 띄우지 않는다(자동 확인).
async function runDupeCheck(silent) {
  let poll;
  if (!silent) { busy(t('dup.checking')); poll = watchProgress(t('dup.checking')); }
  const r = await api.dedupe_check(1024);
  if (poll) clearInterval(poll);
  if (!silent) busy(null);
  if (r.error) { const el = $('#dupeResult'); if (el) el.textContent = r.error; return; }
  state.dupeExact = r;
  renderStrategyCards();                       // 배지를 확정값으로
  if (state.strategy === 'dedupe') selectStrategy('dedupe');   // 안내도 확정값으로
}
function selectStrategy(id) {
  const prev = collectOpts();
  state.strategy = id;
  $$('.scard').forEach(c => c.classList.toggle('sel', c.dataset.id === id));
  const opts = (OPTS[id] || []).map(([k, ty, label, help, def]) => {
    const h = help ? t(help) : '';
    if (ty === 'checkbox') return `<label class="opt"><span>${t(label)}<small>${h}</small></span><input type="checkbox" data-k="${k}" ${prev[k] ? 'checked' : ''}></label>`;
    if (ty === 'select') return `<label class="opt"><span>${t(label)}<small>${h}</small></span><select class="input" data-k="${k}" style="width:130px">${def.map(([v, l]) => `<option value="${v}" ${prev[k] === v ? 'selected' : ''}>${t(l)}</option>`).join('')}</select></label>`;
    return `<label class="opt"><span>${t(label)}<small>${h}</small></span><input class="input" type="number" data-k="${k}" value="${prev[k] ?? def}"></label>`;
  }).join('');
  $('#stratDetail').innerHTML = `<h3>${esc(t(`strat.${id}.name`))} <span class="badge">${esc(t(`strat.${id}.tag`))}</span></h3><p class="muted" style="margin:0">${esc(t(`strat.${id}.desc`))}</p>${id === 'dedupe' ? renderDupeBlock() : ''}<div class="demo" id="demo"></div><div class="kv"><b>${t('p3.best')}</b><span>${esc(t(`strat.${id}.best`))}</span><b>${t('p3.example')}</b><span>${esc(t(`strat.${id}.ex`))}</span></div><div class="opts">${opts}<label class="opt"><span>${t('p3.warn')}<small>${t('p3.warn.s')}</small></span><input type="checkbox" id="optWarn"></label></div>`;
  $('#optWarn').checked = state.includeWarn; $('#optWarn').onchange = e => state.includeWarn = e.target.checked;
  const dc = $('#btnDupeCheck'); if (dc) dc.onclick = () => runDupeCheck(false);
  // 읽을 양이 적으면 고르는 즉시 자동으로 확정한다 (한 번만)
  if (id === 'dedupe' && !state.dupeExact && state.scan && state.scan.dupe_hint
      && state.scan.dupe_hint.candidates && (state.scan.dupe_hint.candidate_bytes || 0) <= DUPE_AUTO_BYTES) {
    setTimeout(() => runDupeCheck(true), 30);
  }
  runDemo(id);
}
function collectOpts() { const o = {}; $$('#stratDetail [data-k]').forEach(el => o[el.dataset.k] = el.type === 'checkbox' ? el.checked : (el.type === 'number' ? +el.value : el.value)); return o; }
let demoTimer;
function runDemo(id) {
  clearInterval(demoTimer); const d = DEMO()[id]; const box = $('#demo'); if (!box || !d) return;
  const W = box.clientWidth || 480; const n = d.bins.length; const gap = W / n;
  box.innerHTML = d.bins.map((b, i) => `<div class="bin" style="left:${gap * i + gap / 2 - 36}px"><div class="fold" style="background:linear-gradient(180deg,${COLORS[i]},color-mix(in srgb,${COLORS[i]} 65%,#000))"></div>${esc(b)}</div>`).join('');
  let k = 0;
  const shoot = () => { const [name, bi] = d.files[k % d.files.length]; k++; const el = document.createElement('div'); el.className = 'fchip'; el.textContent = name; const dx = gap * bi + gap / 2 - 16 - 30; const dy = 200 - 60 - 18; el.style.setProperty('--dx', dx + 'px'); el.style.setProperty('--dy', dy + 'px'); el.style.top = (18 + (k % 3) * 22) + 'px'; box.appendChild(el); setTimeout(() => el.remove(), 1700); };
  shoot(); demoTimer = setInterval(shoot, 650);
}
$('#btnPlan').onclick = async () => {
  busy(t('busy.planning'));
  const poll = watchProgress(t('busy.planning'));
  const res = await api.build_plan(state.strategy, collectOpts(), state.includeWarn, null, [], 0);
  clearInterval(poll); busy(null); if (res.error) return toast(res.error);
  // 옮길 파일이 없으면 빈 미리보기로 넘기지 않고, 왜 없는지 그 자리에서 알려 준다
  if (!res.summary.move_count) { showEmptyPlan(res); return; }
  $('#planEmpty').hidden = true;
  state.plan = res; state.selected.clear(); renderPlan(res); go(4);
};
function showEmptyPlan(res) {
  const s = res.summary, id = state.strategy;
  let why;
  if (id === 'dedupe') why = t('plan.empty.dedupe');
  else if (id === 'archive_old') why = t('plan.empty.archive');
  else if (s.skipped_same > 0) why = t('plan.empty.same', { n: fmtNum(s.skipped_same) });
  else if (s.rule_skips > 0) why = t('plan.empty.rules', { n: fmtNum(s.rule_skips) });
  else why = t('plan.empty.general');
  const el = $('#planEmpty');
  el.innerHTML = `<b>${esc(t('plan.empty.title'))}</b> ${why}`;
  el.hidden = false;
  el.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
}

// ---------------------------------------------------------------- 4. 미리보기
function renderPlan(p) {
  const s = p.summary;
  $('#planSummary').innerHTML = t('p4.summary', { n: fmtNum(s.move_count), size: fmtSize(s.total_size), renamed: fmtNum(s.renamed), warn: fmtNum(s.warn_count) }) + (s.move_count === 0 ? ` · <span style="color:var(--warn)">${t('p4.empty')}</span>` : '');
  renderFlow(p.flows, p.src_groups, p.dest_tree);
  $('#tree').innerHTML = p.dest_tree.length ? p.dest_tree.map(n => treeNode(n)).join('') : `<div class="muted">${t('p4.none')}</div>`;
  $$('#tree .th').forEach(h => h.onclick = () => h.parentElement.classList.toggle('closed'));
  loadMoves('');
  $('#moveFilter').oninput = e => loadMoves(e.target.value);
}
function treeNode(n, depth = 0) {
  const has = n.children.length; return `<div class="tnode ${depth > 1 ? 'closed' : ''}"><div class="th"><span class="caret">${has ? '▾' : ''}</span><span class="fname">📁 ${esc(n.name)}</span><span class="meta">${fmtNum(n.count)}${t('unit.files')} · ${fmtSize(n.size)}</span></div>${has ? `<div class="tc">${n.children.map(c => treeNode(c, depth + 1)).join('')}</div>` : ''}</div>`;
}
function renderFlow(flows, srcGroups, destTree) {
  const svg = $('#flow'); const W = Math.max(svg.clientWidth || 900, 700);
  const L = srcGroups.slice(0, 14), R = destTree.slice(0, 14);
  const rowH = 30, H = Math.max(L.length, R.length, 5) * rowH + 40; svg.setAttribute('viewBox', `0 0 ${W} ${H}`); svg.style.height = H + 'px';
  const lx = 180, rx = W - 180, nw = 160;
  const totalL = L.reduce((a, g) => a + g.size, 0) || 1;
  const yL = {}, yR = {}; L.forEach((g, i) => yL[g.name] = 20 + i * rowH + rowH / 2); R.forEach((g, i) => yR[g.name] = 20 + i * rowH + rowH / 2);
  const rIdx = {}; R.forEach((g, i) => rIdx[g.name] = i);
  let out = '';
  flows.filter(f => yL[f.src] != null && yR[f.dst] != null).forEach(f => {
    const y1 = yL[f.src], y2 = yR[f.dst]; const w = Math.max(1.5, Math.min(14, f.size / totalL * 60));
    const d = `M${lx + nw / 2},${y1} C${(lx + rx) / 2},${y1} ${(lx + rx) / 2},${y2} ${rx - nw / 2},${y2}`;
    const c = COLORS[rIdx[f.dst] % COLORS.length];
    out += `<path class="flow-path" d="${d}" stroke="${c}" stroke-width="${w}"><title>${esc(f.src)} → ${esc(f.dst)}\n${fmtNum(f.count)}${t('unit.files')} · ${fmtSize(f.size)}</title></path><path class="flow-dash" d="${d}" stroke-width="${Math.max(1.5, w * .45)}"/>`;
  });
  const node = (x, y, name, meta, color) => `<g><rect class="flow-node" x="${x - nw / 2}" y="${y - 12}" width="${nw}" height="24" rx="7"${color ? ` stroke="${color}"` : ''}/><text class="flow-label" x="${x - nw / 2 + 8}" y="${y + 4}">${esc(name.length > 14 ? name.slice(0, 13) + '…' : name)}</text><text class="flow-sub" x="${x + nw / 2 - 8}" y="${y + 4}" text-anchor="end">${meta}</text></g>`;
  L.forEach(g => out += node(lx, yL[g.name], g.name, fmtNum(g.count), null));
  R.forEach((g, i) => out += node(rx, yR[g.name], g.name, fmtNum(g.count), COLORS[i % COLORS.length]));
  out += `<text class="flow-sub" x="${lx - nw / 2}" y="12">${t('p4.from')}</text><text class="flow-sub" x="${rx - nw / 2}" y="12">${t('p4.to')}</text>`;
  svg.innerHTML = out;
}
$('#animToggle').onchange = e => $('#flow').classList.toggle('noanim', !e.target.checked);

const ROW = 44; let mvTotal = 0, mvQuery = '', mvCache = new Map();
async function loadMoves(q) { mvQuery = q; mvCache.clear(); const r = await api.plan_moves(0, 200, q); mvTotal = r.total; r.items.forEach((m, i) => mvCache.set(i, m)); $('#moveCount').textContent = fmtNum(mvTotal); $('#vspacer').style.height = mvTotal * ROW + 'px'; $('#moveList').scrollTop = 0; drawRows(); }
async function drawRows() {
  const box = $('#moveList'); const start = Math.floor(box.scrollTop / ROW); const end = Math.min(mvTotal, start + Math.ceil(box.clientHeight / ROW) + 4);
  if ([...Array(Math.max(0, end - start)).keys()].some(i => !mvCache.has(start + i))) { const r = await api.plan_moves(Math.max(0, start - 50), 300, mvQuery); r.items.forEach((m, i) => mvCache.set(Math.max(0, start - 50) + i, m)); }
  let html = '';
  for (let i = start; i < end; i++) {
    const m = mvCache.get(i); if (!m) continue;
    const noteText = m.note ? tCode(m.note) : '';
    const origPath = m.note && m.note.startsWith('dup|') ? m.note.slice(4) : '';
    const subText = m.rule ? t('p4.byrule', { a: m.rule }) : noteText;
    const sub = subText ? `<small class="note" title="${esc(subText)}">${esc(subText)}</small>` : `<small class="note muted">${esc(m.src)}</small>`;
    html += `<div class="vrow ${m.risk}" style="transform:translateY(${i * ROW}px);position:absolute;left:0;right:0"><input type="checkbox" data-src="${esc(m.src)}" ${state.selected.has(m.src) ? 'checked' : ''}><span class="n"><a href="#" class="open" data-open="${esc(m.src)}" title="${t('p4.open')}">${esc(m.name)}</a>${sub}</span><span class="arrow">→</span><span class="d" title="${esc(m.dst)}">${esc(m.dst_rel)}${m.renamed ? ` <i style="color:var(--warn)">${t('p4.renamed')}</i>` : ''}</span><span class="sz">${fmtSize(m.size)}</span><button class="ico" data-reveal="${esc(m.src)}" title="${t('p4.reveal')}">📂</button>${origPath ? `<button class="ico" data-reveal="${esc(origPath)}" title="${t('p4.orig')}">🔍</button>` : '<span></span>'}</div>`;
  }
  $('#vrows').innerHTML = html;
}
$('#moveList').addEventListener('scroll', () => requestAnimationFrame(drawRows));
$('#moveList').addEventListener('click', e => {
  const o = e.target.closest('[data-open]'); if (o) { e.preventDefault(); api.open_path(o.dataset.open); return; }
  const r = e.target.closest('[data-reveal]'); if (r) api.reveal_path(r.dataset.reveal);
});
$('#moveList').addEventListener('change', e => { const cb = e.target; if (cb.dataset.src) { cb.checked ? state.selected.add(cb.dataset.src) : state.selected.delete(cb.dataset.src); $('#selInfo').textContent = state.selected.size ? t('p4.selected', { n: state.selected.size }) : ''; } });
$('#btnExclude').onclick = async () => { if (!state.selected.size) return toast(t('p4.needsel')); busy(t('busy.exclude')); const n = await api.exclude_moves([...state.selected]); busy(null); state.selected.clear(); $('#selInfo').textContent = ''; toast(t('p4.excluded', { n })); loadMoves(mvQuery); };

// ---------------------------------------------------------------- 5. 실행
$('#btnExec').onclick = async () => {
  if (!state.plan || !mvTotal) return toast(t('p4.nomoves'));
  if (!confirm(t('p4.confirm', { n: fmtNum(mvTotal) }))) return;
  const r = await api.execute(true); if (r.error) return toast(r.error);
  state.execRoot = state.plan.root; go(5); $('#execTitle').textContent = t('p5.running'); $('#execSub').textContent = ''; $('#doneAnim').hidden = true; $('#btnCancel').disabled = false;
  watchExec(t('p5.exec'));
};
function watchExec(label) {
  const timer = setInterval(async () => {
    const p = await api.get_progress(); const e = p.exec;
    const pct = e.total ? e.done / e.total * 100 : 0; $('#execBar').style.width = pct + '%';
    $('#execText').textContent = `${fmtNum(e.done)} / ${fmtNum(e.total)} · ${e.current || ''}${e.failed ? ` · ${t('p5.failed')} ${e.failed}` : ''}`;
    if (e.finished && !e.running) { clearInterval(timer); $('#execTitle').textContent = t('p5.done', { label }); $('#execSub').textContent = e.failed ? t('p5.fail', { n: e.failed }) : t('p5.ok'); $('#doneAnim').hidden = false; $('#btnCancel').disabled = true; state.lastJournal = e.journal; }
  }, 250);
}
$('#btnCancel').onclick = () => api.cancel();
$('#btnOpenRoot').onclick = () => api.open_path(state.execRoot || state.root);
$('#btnUndoLast').onclick = async () => { if (!state.lastJournal) return toast(t('p5.noundo')); if (!confirm(t('p5.confirmundo'))) return; const r = await api.undo(state.lastJournal); if (r.error) return toast(r.error); $('#execTitle').textContent = t('p5.undoing'); $('#doneAnim').hidden = true; watchExec(t('p5.undo')); };

$('#btnJournals').onclick = async () => {
  busy(t('j.loading')); const js = await api.journals(); busy(null);
  $('#journalList').innerHTML = js.length ? js.map(j => `<div class="item"><span class="pill ${j.undone ? 'safe' : 'warn'}">${j.undone ? t('j.undone') : t('j.applied')}</span><div><div class="name">${esc(j.created)} · ${t('j.moves', { n: fmtNum(j.count) })}${j.failed ? ` · ${t('j.failed', { n: j.failed })}` : ''}</div><div class="path">${esc(j.root || '')}</div></div>${j.undone ? '' : `<button class="ghost small" data-j="${esc(j.path)}">${t('j.undo')}</button>`}</div>`).join('') : `<div class="muted">${t('j.none')}</div>`;
  $('#journalModal').hidden = false;
  $$('#journalList [data-j]').forEach(b => b.onclick = async () => { if (!confirm(t('j.confirm'))) return; const r = await api.undo(b.dataset.j); if (r.error) return toast(r.error); $('#journalModal').hidden = true; go(5); $('#execTitle').textContent = t('p5.undoing'); $('#execSub').textContent = ''; $('#doneAnim').hidden = true; watchExec(t('p5.undo')); });
};
$('#btnCloseJ').onclick = () => $('#journalModal').hidden = true;

// ---------------------------------------------------------------- 판 구분(Standard/체험/Pro)
let edition = { edition: 'standard', pro_active: false };
async function initEdition() {
  try { edition = await api.edition_info(); } catch (e) { return; }
  const b = $('#btnEdition');
  b.className = 'edition ' + (edition.edition || 'standard');
  b.textContent = edition.edition === 'pro' ? t('edition.pro')
    : edition.edition === 'trial' ? t('edition.trial', { n: edition.trial_days_left })
    : t('edition.standard');
  $('#proRow').classList.toggle('locked', !edition.pro_active);
  await refreshRulesSummary();
}
async function refreshRulesSummary() {
  try {
    const r = await api.rules_get();
    rulesState.list = (r.rules || []).map(normRule);
    const n = rulesState.list.filter(x => x.enabled).length;
    $('#rulesSummary').textContent = n ? t('rules.summary', { n }) : t('rules.summary0');
  } catch (e) { /* 구버전 백엔드 */ }
}
function openPro() {
  const e = edition;
  $('#proStatus').textContent = e.edition === 'pro' ? t('pro.status.pro')
    : e.edition === 'trial' ? t('pro.status.trial', { n: e.trial_days_left })
    : e.trial_used ? t('pro.status.used') : t('pro.status.none');
  const canTrial = !e.pro_active && !e.trial_used;
  const btn = $('#btnTrial');
  btn.textContent = canTrial ? t('pro.trial') : t('pro.soon');
  btn.disabled = !canTrial;
  $('#proModal').hidden = false;
}
$('#btnEdition').onclick = openPro;
$('#btnProClose').onclick = () => $('#proModal').hidden = true;
$('#btnTrial').onclick = async () => {
  const r = await api.start_trial();
  if (r.error) return toast(r.error);
  $('#proModal').hidden = true;
  await initEdition();
  toast(t('pro.status.trial', { n: r.trial_days_left }), 5000);
};

// ---------------------------------------------------------------- 내 규칙 (Pro)
const FIELDS = ['name', 'ext', 'size', 'age', 'dir', 'path'];
const OPS_TEXT = ['contains', 'not_contains', 'equals', 'starts', 'ends', 'regex'];
const OPS_NUM = ['gt', 'lt', 'equals'];
const ACTIONS = ['folder', 'skip', 'prefix', 'suffix'];
const opsFor = f => (f === 'size' || f === 'age') ? OPS_NUM : OPS_TEXT;
const rulesState = { list: [], sel: -1 };
const normRule = r => ({ id: r.id || '', enabled: r.enabled !== false, name: r.name || '', conditions: (r.conditions || []).map(c => ({ field: c.field || 'name', op: c.op || 'contains', value: c.value || '' })), action: r.action || 'folder', value: r.value || '' });

$('#btnRules').onclick = openRules;
$('#proRow').addEventListener('click', e => { if (!edition.pro_active && !e.target.closest('#btnRules')) openPro(); });
$('#btnRulesClose').onclick = () => $('#rulesModal').hidden = true;

async function openRules() {
  if (!edition.pro_active) return openPro();
  await refreshRulesSummary();
  rulesState.sel = rulesState.list.length ? 0 : -1;
  renderRules();
  $('#rulesModal').hidden = false;
}
function renderRules() {
  const L = $('#rulesList');
  L.innerHTML = rulesState.list.length
    ? rulesState.list.map((r, i) => `<div class="rule-item ${i === rulesState.sel ? 'sel' : ''} ${r.enabled ? '' : 'off'}" data-i="${i}"><input type="checkbox" data-en="${i}" ${r.enabled ? 'checked' : ''}><span class="rn">${esc(r.name || t('rules.newname'))}</span></div>`).join('')
    : `<div class="muted small">${t('rules.none')}</div>`;
  $$('#rulesList .rule-item').forEach(el => el.onclick = ev => {
    if (ev.target.dataset.en !== undefined) return;
    rulesState.sel = +el.dataset.i; renderRules();
  });
  $$('#rulesList [data-en]').forEach(cb => cb.onchange = () => { rulesState.list[+cb.dataset.en].enabled = cb.checked; renderRules(); });
  renderRuleEdit();
}
function renderRuleEdit() {
  const box = $('#ruleEdit');
  const r = rulesState.list[rulesState.sel];
  if (!r) { box.innerHTML = `<div class="muted small">${t('rules.pick')}</div>`; return; }
  const sel = (opts, cur, attr, tag) => `<select class="input" ${attr}>${opts.map(o => `<option value="${o}" ${o === cur ? 'selected' : ''}>${esc(t(tag + '.' + o))}</option>`).join('')}</select>`;
  const conds = r.conditions.map((c, i) => `<div class="cond">
      ${sel(FIELDS, c.field, `data-cf="${i}"`, 'field')}
      ${sel(opsFor(c.field), c.op, `data-co="${i}"`, 'op')}
      <input class="input" data-cv="${i}" value="${esc(c.value)}">
      <button class="icobtn" data-cd="${i}">✕</button>
    </div>`).join('');
  box.innerHTML = `
    <div class="fr"><span>${t('rules.name')}</span><input class="input" id="rName" value="${esc(r.name)}"></div>
    <div class="fr"><span>${t('rules.conditions')}</span><div>${conds}<button class="ghost small" id="rAddCond">${t('rules.addcond')}</button></div></div>
    <div class="fr"><span>${t('rules.action')}</span>${sel(ACTIONS, r.action, 'id="rAction"', 'act')}</div>
    ${r.action === 'skip' ? '' : `<div class="fr"><span>${t('rules.value')}</span><div><input class="input" id="rValue" value="${esc(r.value)}"><div class="tokens">${r.action === 'folder' ? esc(t('rules.tokens')) : ''}</div></div></div>`}
    <div class="row"><button class="ghost small" id="rTest">${t('rules.test')}</button><button class="icobtn" id="rDel">${t('rules.delete')}</button></div>
    <div class="testbox" id="rTestBox" hidden></div>`;
  $('#rName').oninput = e => { r.name = e.target.value; const it = $(`.rule-item[data-i="${rulesState.sel}"] .rn`); if (it) it.textContent = r.name || t('rules.newname'); };
  $$('#ruleEdit [data-cf]').forEach(s2 => s2.onchange = () => { const i = +s2.dataset.cf; r.conditions[i].field = s2.value; r.conditions[i].op = opsFor(s2.value)[0]; renderRuleEdit(); });
  $$('#ruleEdit [data-co]').forEach(s2 => s2.onchange = () => r.conditions[+s2.dataset.co].op = s2.value);
  $$('#ruleEdit [data-cv]').forEach(inp => inp.oninput = () => r.conditions[+inp.dataset.cv].value = inp.value);
  $$('#ruleEdit [data-cd]').forEach(b => b.onclick = () => { r.conditions.splice(+b.dataset.cd, 1); renderRuleEdit(); });
  $('#rAddCond').onclick = () => { r.conditions.push({ field: 'name', op: 'contains', value: '' }); renderRuleEdit(); };
  $('#rAction').onchange = e => { r.action = e.target.value; renderRuleEdit(); };
  const rv = $('#rValue'); if (rv) rv.oninput = e => r.value = e.target.value;
  $('#rDel').onclick = () => { rulesState.list.splice(rulesState.sel, 1); rulesState.sel = Math.min(rulesState.sel, rulesState.list.length - 1); renderRules(); };
  $('#rTest').onclick = async () => {
    const res = await api.rules_test(r);
    const box2 = $('#rTestBox'); box2.hidden = false;
    if (res.error) { box2.innerHTML = `<span class="muted">${esc(t('rules.testneed'))}</span>`; return; }
    const list = (res.samples || []).map(s2 => `<li>${esc(s2.name)}${s2.dest ? ' → ' + esc(s2.dest) : ''}</li>`).join('');
    box2.innerHTML = `<span class="hit">${esc(t('rules.testhit', { n: fmtNum(res.count) }))}</span>${list ? `<ul>${list}</ul>` : ''}`;
  };
}
$('#btnRuleAdd').onclick = () => {
  rulesState.list.push(normRule({ name: t('rules.newname'), conditions: [{ field: 'name', op: 'contains', value: '' }], action: 'folder', value: '' }));
  rulesState.sel = rulesState.list.length - 1;
  renderRules();
};
$('#btnRulesSave').onclick = async () => {
  const res = await api.rules_save(rulesState.list);
  if (res.error) return toast(res.error);
  await refreshRulesSummary();
  $('#rulesSaveInfo').textContent = t('rules.saved', { n: res.saved });
  toast(t('rules.saved', { n: res.saved }));
};

// ---------------------------------------------------------------- 시작
function start() { initLang(); initDepth(); initVersion(); initFolders(); initStrategies(); initEdition(); initWelcome(); }
if (wv2 || (window.pywebview && window.pywebview.api)) start(); else { window.addEventListener('pywebviewready', start, { once: true }); setTimeout(() => { if (!window.pywebview) start(); }, 300); }
window.addEventListener('resize', () => { if (state.plan && state.step === 4) renderFlow(state.plan.flows, state.plan.src_groups, state.plan.dest_tree); if (state.step === 3) runDemo(state.strategy); });
