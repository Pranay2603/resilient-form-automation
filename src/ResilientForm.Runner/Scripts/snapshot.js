// Describes every form control in the current document (including open shadow roots) as plain data.
// Runs inside the page via IJavaScriptExecutor. Registers the live elements in window.__rf so the
// C# side can fetch the exact element again by index. It never reads data-* attributes.
return (function () {
  var reg = (window.__rf = []);
  var NS = function (s) { return (s || '').replace(/\s+/g, ' ').trim(); };
  var SEL = 'input,select,textarea,[role="radio"],[role="textbox"],[role="combobox"],[role="listbox"],[contenteditable=""],[contenteditable="true"]';

  var controls = [];
  (function collect(root) {
    root.querySelectorAll(SEL).forEach(function (e) { controls.push(e); });
    root.querySelectorAll('*').forEach(function (e) { if (e.shadowRoot) collect(e.shadowRoot); });
  })(document);

  function isVisible(e) {
    if (!e || !e.isConnected) return false;
    var r = e.getBoundingClientRect();
    if (r.width < 1 || r.height < 1) return false;
    if (e.checkVisibility && !e.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true })) return false;
    return true;
  }

  // Visible pieces of text with their position, built once per document / shadow root.
  var textCache = new Map();
  var INLINE = { B: 1, I: 1, EM: 1, STRONG: 1, SPAN: 1, SMALL: 1, SUP: 1, ABBR: 1, U: 1, FONT: 1, MARK: 1 };
  var SKIP = { SCRIPT: 1, STYLE: 1, NOSCRIPT: 1, OPTION: 1, SELECT: 1, TEXTAREA: 1, TEMPLATE: 1, OPTGROUP: 1 };
  function textIndex(root) {
    if (textCache.has(root)) return textCache.get(root);
    var items = [];
    var base = root.nodeType === 9 ? root.body : root;
    if (base) {
      var els = base.querySelectorAll('*');
      for (var i = 0; i < els.length && i < 6000; i++) {
        var el = els[i];
        if (SKIP[el.tagName]) continue;
        if (!isVisible(el)) continue;
        var pieces = 0;
        for (var n = el.firstChild; n; n = n.nextSibling) {
          if (n.nodeType !== 3 || !NS(n.nodeValue)) continue;
          pieces++;
          var rg = document.createRange();
          rg.selectNodeContents(n);
          var rc = rg.getBoundingClientRect();
          if (rc.width >= 1 && rc.height >= 1) items.push({ el: el, text: NS(n.nodeValue), l: rc.left, t: rc.top, r: rc.right, b: rc.bottom });
        }
        // "First <b>Name</b>": text split by inline formatting is also offered as one phrase
        if (pieces > 0 && el.children.length > 0 && Array.prototype.every.call(el.children, function (ch) { return INLINE[ch.tagName]; }) && !el.querySelector(SEL)) {
          var full = NS(el.textContent);
          if (full && full.length <= 60) {
            var r2 = document.createRange(); r2.selectNodeContents(el);
            var c2 = r2.getBoundingClientRect();
            if (c2.width >= 1 && c2.height >= 1) items.push({ el: el, text: full, l: c2.left, t: c2.top, r: c2.right, b: c2.bottom });
          }
        }
      }
    }
    textCache.set(root, items);
    return items;
  }

  function overlapY(a, b) { return Math.min(a.b, b.b) - Math.max(a.t, b.t); }
  function overlapX(a, b) { return Math.min(a.r, b.r) - Math.max(a.l, b.l); }
  function box(r) { return { l: r.left, t: r.top, r: r.right, b: r.bottom, h: r.height }; }

  // Text sitting to the left / above / right of a control, ignoring text that belongs to another control between.
  function nearby(c, rect, wantRight, others) {
    var me = box(rect), out = [];
    var leftWall = -1e9, topWall = -1e9;
    others.forEach(function (o) {
      if (o === c) return;
      var ob = box(o.getBoundingClientRect());
      if (ob.r - ob.l < 1) return;
      if (ob.r <= me.l + 4 && overlapY(ob, me) > 0.4 * Math.min(ob.h, me.h)) leftWall = Math.max(leftWall, ob.r);
      if (ob.b <= me.t + 4 && overlapX(ob, me) > 0) topWall = Math.max(topWall, ob.b);
    });
    textIndex(c.getRootNode()).forEach(function (t) {
      if (c.contains(t.el)) return;
      var vo = overlapY(t, me);
      if (t.r <= me.l + 4 && vo > 0.4 * Math.min(t.b - t.t, me.h) && t.l >= leftWall - 2) {
        var d = me.l - t.r; if (d <= 320) out.push({ source: 'near-left', text: t.text, distance: Math.max(0, d) });
      }
      if (t.b <= me.t + 4 && t.b >= topWall - 2 && (overlapX(t, me) > 0 || (t.l >= me.l - 24 && t.l <= me.r))) {
        var d2 = me.t - t.b; if (d2 <= 70) out.push({ source: 'near-above', text: t.text, distance: Math.max(0, d2) });
      }
      if (wantRight && t.l >= me.r - 4 && vo > 0.4 * Math.min(t.b - t.t, me.h)) {
        var d3 = t.l - me.r; if (d3 <= 60) out.push({ source: 'near-right', text: t.text, distance: Math.max(0, d3) });
      }
    });
    var keep = [], seen = {};
    out.sort(function (a, b) { return a.distance - b.distance; }).forEach(function (e) {
      seen[e.source] = (seen[e.source] || 0) + 1;
      if (seen[e.source] <= 3) keep.push(e);
    });
    return keep;
  }

  function labelText(lab) {
    var c = lab.cloneNode(true);
    c.querySelectorAll('input,select,textarea,button,option,script,style').forEach(function (n) { n.remove(); });
    return NS(c.textContent);
  }

  function kindOf(c) {
    var tag = c.tagName.toLowerCase(), role = (c.getAttribute('role') || '').toLowerCase();
    if (tag === 'select') return 'select';
    if (tag === 'textarea') return 'textarea';
    if (tag === 'input') {
      var t = (c.type || 'text').toLowerCase();
      if (t === 'radio') return 'radio';
      if (t === 'checkbox') return 'checkbox';
      if (t === 'text' || t === 'search') return 'text';
      return 'input-' + t;
    }
    if (role === 'radio') return 'radio';
    if (role === 'combobox' || role === 'listbox') return 'combobox';
    if (role === 'textbox' || c.isContentEditable) return 'text';
    return 'other';
  }

  // radio groups: by shared name, otherwise by the smallest container holding several radios
  var radios = controls.filter(function (c) { return kindOf(c) === 'radio'; });
  var nameCount = {};
  radios.forEach(function (r) { var k = r.getAttribute('name'); if (k) nameCount[k] = (nameCount[k] || 0) + 1; });
  var groupIds = new Map(), nextGroup = 0;
  function gid(key) { if (!groupIds.has(key)) groupIds.set(key, nextGroup++); return groupIds.get(key); }
  function groupOf(r) {
    var nm = r.getAttribute('name');
    if (nm && nameCount[nm] >= 2) return gid('n:' + nm + ':' + (r.form ? Array.prototype.indexOf.call(document.forms, r.form) : -1));
    for (var a = r.parentElement; a; a = a.parentElement) {
      if (a.querySelectorAll('input[type=radio],[role=radio]').length >= 2) return gid(a);
    }
    return gid(r);
  }

  var result = [];
  controls.forEach(function (c) {
    var kind = kindOf(c);
    if (kind === 'other') return;
    var rect = c.getBoundingClientRect();
    var root = c.getRootNode();
    var labels = [], labelEls = [];

    if (c.labels) {
      Array.prototype.forEach.call(c.labels, function (lab) {
        labelEls.push(lab);
        labels.push({ source: lab.contains(c) ? 'wrap' : 'for', text: labelText(lab), distance: 0 });
      });
    }
    var lb = c.getAttribute('aria-labelledby');
    if (lb) lb.split(/\s+/).forEach(function (id) {
      var t = root.getElementById ? root.getElementById(id) : document.getElementById(id);
      if (t) { labelEls.push(t); labels.push({ source: 'aria-labelledby', text: NS(t.textContent), distance: 0 }); }
    });
    var fs = c.closest('fieldset');
    var lg = fs && fs.querySelector(':scope > legend');
    if (lg) labels.push({ source: 'legend', text: NS(lg.textContent), distance: 0 });
    var grp = c.closest('[role=radiogroup],[role=group]');
    if (grp) {
      var ga = grp.getAttribute('aria-label');
      var gl = grp.getAttribute('aria-labelledby');
      if (ga) labels.push({ source: 'group-aria', text: NS(ga), distance: 0 });
      if (gl) gl.split(/\s+/).forEach(function (id) {
        var t = document.getElementById(id);
        if (t) labels.push({ source: 'group-aria', text: NS(t.textContent), distance: 0 });
      });
    }

    var isChoice = kind === 'radio' || kind === 'checkbox';
    var vis = isVisible(c);
    // custom styled radios: the real input is hidden, its label is what people see and click
    var effVis = vis || (isChoice && labelEls.some(isVisible));
    var anchor = vis ? c : (isChoice && labelEls.filter(isVisible)[0]) || c;
    var arect = anchor.getBoundingClientRect();
    if (effVis) nearby(c, arect, isChoice && labels.length === 0, controls).forEach(function (e) { labels.push(e); });

    var options = [];
    if (kind === 'select') {
      Array.prototype.slice.call(c.options, 0, 400).forEach(function (o) {
        options.push({ text: NS(o.textContent), value: o.value, disabled: o.disabled });
      });
    }

    var idx = reg.push(c) - 1;
    result.push({
      index: idx,
      tag: c.tagName.toLowerCase(),
      kind: kind,
      id: c.id || '',
      name: c.getAttribute('name') || '',
      cls: typeof c.className === 'string' ? c.className : '',
      placeholder: c.getAttribute('placeholder') || '',
      ariaLabel: c.getAttribute('aria-label') || '',
      title: c.getAttribute('title') || '',
      autocomplete: c.getAttribute('autocomplete') || '',
      value: kind === 'select' ? '' : (c.getAttribute('value') || ''),
      visible: vis,
      effVisible: effVis,
      disabled: !!c.disabled || c.getAttribute('aria-disabled') === 'true',
      readOnly: !!c.readOnly,
      checked: !!c.checked || c.getAttribute('aria-checked') === 'true',
      groupId: kind === 'radio' ? groupOf(c) : -1,
      rect: { x: arect.left, y: arect.top, w: arect.width, h: arect.height },
      labels: labels,
      options: options
    });
  });
  return JSON.stringify(result);
})();
