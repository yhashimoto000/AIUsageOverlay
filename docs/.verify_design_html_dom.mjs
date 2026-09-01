
import { JSDOM, VirtualConsole } from 'jsdom';
import fs from 'fs';

const file = process.argv[2];
const errors = [];
const vc = new VirtualConsole()
  .on('jsdomError', e => errors.push('jsdomError: ' + e.message))
  .on('error', (...a) => errors.push('console.error: ' + a.join(' ')));

const dom = new JSDOM(fs.readFileSync(file, 'utf8'), {
  runScripts: 'dangerously',
  pretendToBeVisual: true,
  url: 'http://localhost/',
  virtualConsole: vc,
});
const { window } = dom, d = window.document;
await new Promise(r => setTimeout(r, 400));

const out = [];
const ok = (c, label, detail) => out.push({ ok: !!c, label, detail: detail || '' });

/* jsdom は要素の scrollTo を実装しない。スタブしないと本文検索・目次クリックの
   ハンドラが例外で落ち、その先を検査できなくなる（ブラウザでは正常に動く経路）。
   ここを諦めると、目玉機能である検索と目次遷移が一度も動かされないまま
   「実行時エラー 0 件」が出てしまう。 */
const docEl = d.getElementById('doc');
if (docEl && !docEl.scrollTo) docEl.scrollTo = () => {};

/* 実行時エラーの判定は最後にまとめて行う（→ ファイル末尾）。
   イベントハンドラの中で投げた例外は操作してみないと出てこないため、
   読み込み直後だけ見ても「エラー 0 件」になってしまう。実際にそれで
   目次の絞り込みが死んでいるのを見逃した。 */

/* ---- 目次の開閉と絞り込み ----
   たたまれた章の中の一致を見落とさないよう、絞り込み中は全章が開く決まりになっている。 */
const groups = [...d.querySelectorAll('.chgrp')];
// h2 を持たない文書の先頭には章行なしの器ができる。開閉の検査対象からは外す。
const withSecs = groups.filter(g => g.querySelector('.secs') && g.querySelector('.ca'));
if (withSecs.length) {
  const g = withSecs[0];
  const secs = g.querySelector('.secs');
  const caret = g.querySelector('.ca');
  const before = secs.style.display;
  caret.dispatchEvent(new window.MouseEvent('click', { bubbles: true }));
  ok(secs.style.display !== before, '目次のキャレットで章を開閉できる',
     '"' + before + '" -> "' + secs.style.display + '"');

  const navq = d.getElementById('navq');
  const visibleSecs = () => [...d.querySelectorAll('nav a.sec')]
    .filter(a => !a.classList.contains('hidden')).length;
  const total = d.querySelectorAll('nav a.sec').length;
  const probe = (withSecs[0].querySelector('a.sec') || {}).dataset;
  // 節が 1 つしかない文書では「絞り込んで減る」を確かめようがないので飛ばす
  if (navq && probe && probe.title && total >= 2) {
    navq.value = probe.title.slice(0, 4);
    navq.dispatchEvent(new window.Event('input'));
    ok(visibleSecs() < total && visibleSecs() > 0, '目次を絞り込める',
       '"' + navq.value + '" -> ' + visibleSecs() + ' / ' + total + ' 節');
    const openedAll = groups.filter(x => x.querySelector('.secs'))
      .every(x => x.querySelector('.secs').style.display !== 'none');
    ok(openedAll, '絞り込み中は全章が展開される');
    navq.value = ''; navq.dispatchEvent(new window.Event('input'));
    ok(visibleSecs() === total, '目次の絞り込み解除で全節に戻る');
  }
}

/* ---- 目次クリックで本文へ飛べる ---- */
const firstSec = d.querySelector('nav a.sec');
if (firstSec) {
  firstSec.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true }));
  ok(firstSec.classList.contains('cur'), '目次クリックで現在位置が移る',
     firstSec.dataset.target);
}

/* ---- パンくず ---- */
const crumb = d.getElementById('crumb');
ok(crumb && crumb.textContent.trim().length > 0, 'パンくずに現在位置が出る',
   crumb ? crumb.textContent.trim().slice(0, 40) : '(要素なし)');

/* ---- 本文の全文検索 ----
   デバウンス 200ms を挟んでいるので、入力後に少し待ってから数える。 */
const qbox = d.getElementById('q');
const titleEl = d.querySelector('#doc h2 .t') || d.querySelector('#doc p');
if (qbox && titleEl && titleEl.textContent.trim().length >= 2) {
  const word = titleEl.textContent.trim().slice(0, 2);
  qbox.value = word;
  qbox.dispatchEvent(new window.Event('input'));
  await new Promise(r => setTimeout(r, 350));
  const n = d.querySelectorAll('#doc mark').length;
  ok(n > 0, '本文の全文検索がハイライトする', '"' + word + '" -> ' + n + ' 件');
  d.getElementById('next').dispatchEvent(new window.MouseEvent('click', { bubbles: true }));
  ok(d.querySelectorAll('#doc mark.cur').length === 1, '▼ で次の一致へ移動する');

  // トリアージの操作パネルは文書の中身ではないので、検索対象に入れてはいけない。
  // 入れると UI 文字列が大量に一致し、絞り込み時に <mark> が孤児化して件数が狂う。
  qbox.value = '判定済';
  qbox.dispatchEvent(new window.Event('input'));
  await new Promise(r => setTimeout(r, 350));
  ok(d.querySelectorAll('.triage-bar mark, .tri-status mark, .tri-memo mark').length === 0,
     '検索がトリアージの操作パネルを拾わない');

  d.getElementById('clear').dispatchEvent(new window.MouseEvent('click', { bubbles: true }));
  await new Promise(r => setTimeout(r, 350));
  ok(d.querySelectorAll('#doc mark').length === 0, '検索クリアでハイライトが消える');
}

const tables = [...d.querySelectorAll('table[data-triage]')];
if (tables.length === 0) {
  ok(errors.length === 0, 'スクリプト実行時エラー 0 件', errors.slice(0, 2).join(' | '));
  console.log(JSON.stringify({ skipped: 'トリアージ表なし', results: out }));
  process.exit(0);
}

/* 行キー（1 列目）が入っている表の中から、いちばん行数の多いものを検査対象にする。
   1 列目が空の表を選ぶと保存キーが作れず、保存の検査が偽 NG になる。 */
const usable = tables.filter(x => x.tBodies[0].rows.length
  && [...x.tBodies[0].rows].some(r => r.cells[0] && r.cells[0].textContent.trim()));
if (usable.length === 0) {
  ok(errors.length === 0, 'スクリプト実行時エラー 0 件', errors.slice(0, 2).join(' | '));
  console.log(JSON.stringify({ skipped: '行キーを持つトリアージ表なし', results: out }));
  process.exit(0);
}
const t = usable.slice().sort((a, b) => b.tBodies[0].rows.length - a.tBodies[0].rows.length)[0];
const rows = [...t.tBodies[0].rows];
const head = [...t.tHead.rows[0].cells].map(c => c.textContent.trim());
ok(head.slice(-2).join(',') === '対応,メモ', '「対応 / メモ」列が注入される', head.slice(-2).join(','));
// メモは 1 行 input。textarea に戻すと行高が揃わなくなるので、型まで見る。
ok(rows[0].querySelector('select') && rows[0].querySelector('td.tri-memo input'),
   '各行に対応 select とメモ input（1 行）がある');

const bars = [...d.querySelectorAll('.triage-bar')];
ok(bars.length === tables.length, '絞り込みバーが全表に付く',
   'バー ' + bars.length + ' / 表 ' + tables.length);

const bar = t.previousElementSibling;
const visible = () => rows.filter(r => !r.classList.contains('hidden-by-filter')).length;

const q = bar.querySelector('input[type=search]');
/* 「スコープ / 非スコープ」のように片方が他方の部分文字列だと、その語で絞っても
   行が減らず偽 NG になる。**全行に一致しない語**を選び直す。
   検査そのものが信用されなくなる方が、見逃しより害が大きい。 */
let probe = '';
for (const r of rows) {
  for (const c of [r.cells[1], r.cells[0], r.cells[2]]) {
    const cand = c ? c.textContent.trim().slice(0, 6) : '';
    if (!cand) continue;
    const hit = rows.filter(x => x.textContent.toLowerCase().includes(cand.toLowerCase())).length;
    if (hit >= 1 && hit < rows.length) { probe = cand; break; }
  }
  if (probe) break;
}
if (q && probe) {
  q.value = probe; q.dispatchEvent(new window.Event('input'));
  ok(visible() < rows.length && visible() > 0, '全文検索で絞り込める',
     '"' + probe + '" -> ' + visible() + ' / ' + rows.length + ' 行');
  q.value = ''; q.dispatchEvent(new window.Event('input'));
  ok(visible() === rows.length, '検索クリアで全行に戻る');
}

const counts = bar.querySelector('.tri-count');
ok(counts && /判定済/.test(counts.textContent), '「判定済 n / 総数」の表示がある',
   counts ? counts.textContent.trim().slice(0, 36) : '');

/* 状態での絞り込みはチップで行う（ドロップダウンより 1 クリック速い） */
const chips = [...bar.querySelectorAll('.fchip')];
ok(chips.length >= 6, '状態の絞り込みチップがある', chips.map(c => c.textContent).join('/'));

const key = rows[0].cells[0].textContent.trim();
const sel = rows[0].querySelector('select');
const target = [...sel.options].map(o => o.value).find(v => v === 'do') || sel.options[1].value;
sel.value = target; sel.dispatchEvent(new window.Event('change'));
ok(rows[0].className.indexOf('tri-') >= 0, '選択が行のクラスに反映される', rows[0].className);

/* 進捗バーは状態別の件数を幅で表す。1 件でも判定すればセグメントが出る。 */
const segs = bar.querySelectorAll('.tri-progress i');
ok(segs.length > 0, '進捗バーに状態別のセグメントが出る', segs.length + ' 本');

/* ツールバー右端の全体集計。表をまたいだ重複は 1 件として数える。 */
const tally = d.getElementById('tri-tally');
ok(tally && /判定済/.test(tally.textContent), 'ツールバーに全体の判定済件数が出る',
   tally ? tally.textContent.trim() : '');

const lsKeys = Object.keys(window.localStorage);
const raw = lsKeys.length ? window.localStorage.getItem(lsKeys[0]) : null;
let saved = null;
try { saved = raw ? JSON.parse(raw) : null; } catch (e) { }
const flat = saved ? Object.values(saved).reduce((a, o) => Object.assign(a, o), {}) : {};
ok(flat[key] && flat[key].s === target, '判定が localStorage に保存される',
   'key=' + (lsKeys[0] || '(なし)') + ' / ' + key + '=' + JSON.stringify(flat[key] || null));

const tid = t.getAttribute('data-triage');
const sibling = tables.find(x => x !== t && x.getAttribute('data-triage') === tid
  && [...x.tBodies[0].rows].some(r => r.cells[0].textContent.trim() === key));
if (sibling) {
  const sr = [...sibling.tBodies[0].rows].find(r => r.cells[0].textContent.trim() === key);
  ok(sr.querySelector('select').value === target, '同じ id を共有する表へ判定が同期する');
}

// すべて操作し終えてから判定する。ハンドラ内で投げた例外もここで拾える。
ok(errors.length === 0, 'スクリプト実行時エラー 0 件（操作後まで含む）',
   errors.slice(0, 2).join(' | '));

console.log(JSON.stringify({ results: out }));
