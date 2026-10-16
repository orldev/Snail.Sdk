/* ==========================================================================
   Theme behaviour of every Snail site. Three small things and nothing else:
   search over the index MkDocs builds, the mobile Menu link, and a scroll
   wrapper for the parameter tables the generator writes.
   ========================================================================== */

(function () {
  'use strict';

  // --- Parameter tables ----------------------------------------------------
  // A tool page carries a table per tool, and some are wider than the column.
  // The table scrolls inside its own box so the page never does.
  function wrapTables() {
    var tables = document.querySelectorAll('.prose > table');
    for (var i = 0; i < tables.length; i++) {
      var box = document.createElement('div');
      box.className = 'scroll';
      tables[i].parentNode.insertBefore(box, tables[i]);
      box.appendChild(tables[i]);
      labelCells(tables[i]);

      // On a phone the last column is simply cut by the edge and nothing says
      // so. A note is the system's own answer — a fading gradient would be the
      // first filled surface on a site that has none.
      var note = document.createElement('p');
      note.className = 'scroll-note';
      note.textContent = 'more columns \u2192';
      note.hidden = true;
      box.parentNode.insertBefore(note, box.nextSibling);
      watchOverflow(box, note);
    }
  }

  // On a phone a table of two or three columns stacks its rows, and a cell
  // below the name says which column it was: the label is its header's text.
  function labelCells(table) {
    var headers = table.querySelectorAll('thead th');
    var rows = table.querySelectorAll('tbody tr');
    for (var r = 0; r < rows.length; r++) {
      for (var c = 0; c < rows[r].cells.length && c < headers.length; c++) {
        rows[r].cells[c].setAttribute('data-label', headers[c].textContent.trim());
      }
    }
  }

  function watchOverflow(box, note) {
    var check = function () { note.hidden = box.scrollWidth <= box.clientWidth + 1; };
    check();
    box.addEventListener('scroll', check);
    window.addEventListener('resize', check);
  }

  // --- Copying a command ---------------------------------------------------
  // A word, not a glyph: the system has no icons, and an action here is text.
  function addCopyLinks() {
    var blocks = document.querySelectorAll('.prose .highlight');
    for (var i = 0; i < blocks.length; i++) {
      (function (block) {
        var link = document.createElement('button');
        link.type = 'button';
        link.className = 'code-copy';
        link.textContent = 'copy';
        link.addEventListener('click', function () {
          var code = block.querySelector('code');
          if (!code || !navigator.clipboard) return;
          navigator.clipboard.writeText(code.textContent).then(function () {
            link.textContent = 'copied';
            setTimeout(function () { link.textContent = 'copy'; }, 1400);
          });
        });
        block.appendChild(link);
      })(blocks[i]);
    }
  }

  // --- Search --------------------------------------------------------------
  // MkDocs writes search_index.json whatever the theme is: one document per
  // heading, a few hundred for the largest site. At that size a substring pass is
  // instant and needs no query engine, so there is none.
  var index = null;
  var loading = null;

  function load() {
    if (index) return Promise.resolve(index);
    if (loading) return loading;
    loading = fetch(window.SEARCH_INDEX)
      .then(function (r) { return r.json(); })
      .then(function (data) {
        index = (data.docs || []).filter(function (d) { return d.title && d.location; });
        return index;
      })
      .catch(function () { index = []; return index; });
    return loading;
  }

  function score(doc, needle) {
    var title = doc.title.toLowerCase();
    if (title === needle) return 0;
    if (title.indexOf(needle) === 0) return 1;
    if (title.indexOf(needle) >= 0) return 2;
    return (doc.text || '').toLowerCase().indexOf(needle) >= 0 ? 3 : -1;
  }

  function where(location) {
    var path = location.split('#')[0].replace(/\/$/, '');
    return path === '' ? 'Home' : path;
  }

  function render(box, docs, needle) {
    box.textContent = '';
    if (!docs.length) {
      var none = document.createElement('p');
      none.className = 'result-none';
      none.textContent = 'Nothing matches “' + needle + '”.';
      box.appendChild(none);
      box.hidden = false;
      return;
    }
    var base = window.SEARCH_INDEX.replace(/search\/search_index\.json$/, '');
    for (var i = 0; i < docs.length; i++) {
      var a = document.createElement('a');
      a.className = 'result';
      a.href = base + docs[i].location;
      var title = document.createElement('span');
      title.className = 'result-title';
      title.textContent = docs[i].title;
      var loc = document.createElement('span');
      loc.className = 'result-where';
      loc.textContent = where(docs[i].location);
      a.appendChild(title);
      a.appendChild(loc);
      box.appendChild(a);
    }
    box.hidden = false;
  }

  function search(input, box) {
    var needle = input.value.trim().toLowerCase();
    if (needle.length < 2) {
      box.hidden = true;
      box.textContent = '';
      return;
    }
    load().then(function (docs) {
      var hits = [];
      for (var i = 0; i < docs.length; i++) {
        var rank = score(docs[i], needle);
        if (rank >= 0) hits.push({ doc: docs[i], rank: rank });
      }
      hits.sort(function (a, b) { return a.rank - b.rank; });
      render(box, hits.slice(0, 12).map(function (h) { return h.doc; }), input.value.trim());
    });
  }

  function ready() {
    wrapTables();
    addCopyLinks();

    var head = document.getElementById('head');
    var input = document.getElementById('q');
    var box = document.getElementById('results');
    var menu = document.getElementById('act-menu');
    var find = document.getElementById('act-search');
    if (!head || !input || !box) return;

    input.addEventListener('input', function () { search(input, box); });
    input.addEventListener('focus', load);

    input.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') {
        input.value = '';
        box.hidden = true;
        input.blur();
      }
    });

    // A slash reaches the field from anywhere, which is why the hint says "/".
    document.addEventListener('keydown', function (e) {
      if (e.key !== '/' || e.metaKey || e.ctrlKey || e.altKey) return;
      var tag = (e.target.tagName || '').toLowerCase();
      if (tag === 'input' || tag === 'textarea') return;
      e.preventDefault();
      head.classList.add('is-searching');
      input.focus();
    });

    if (menu) {
      menu.addEventListener('click', function () {
        var open = head.classList.toggle('is-open');
        menu.classList.toggle('is-on', open);
        menu.setAttribute('aria-expanded', open ? 'true' : 'false');
      });
    }

    if (find) {
      find.addEventListener('click', function () {
        var on = head.classList.toggle('is-searching');
        find.classList.toggle('is-on', on);
        if (on) input.focus();
      });
    }

    document.addEventListener('click', function (e) {
      if (box.hidden) return;
      if (head.contains(e.target)) return;
      box.hidden = true;
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', ready);
  } else {
    ready();
  }
})();
