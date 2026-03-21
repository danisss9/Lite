// ── Form ──────────────────────────────────────────────────────────────────
document.getElementById('submit-btn').addEventListener('click', function () {
  var name   = document.getElementById('name').value.trim();
  var email  = document.getElementById('email').value.trim();
  var agreed = document.getElementById('agree').checked;
  var result = document.getElementById('form-result');

  if (!name)   { result.textContent = 'Please enter your name.'; return; }
  if (!email)  { result.textContent = 'Please enter your email.'; return; }
  if (!agreed) { result.textContent = 'Please agree to the terms.'; return; }

  result.textContent = 'Submitted! Hello, ' + name + ' (' + email + ')';
});

// ── Counter ───────────────────────────────────────────────────────────────
var count = 0;

function updateCount() {
  document.getElementById('count-display').textContent = String(count);
}

document.getElementById('inc-btn').addEventListener('click', function () {
  count += 1;
  updateCount();
});

document.getElementById('dec-btn').addEventListener('click', function () {
  count -= 1;
  updateCount();
});

document.getElementById('reset-btn').addEventListener('click', function () {
  count = 0;
  updateCount();
});

// ── Todo list ─────────────────────────────────────────────────────────────
var todoCount = 0;

document.getElementById('todo-add').addEventListener('click', function () {
  var input = document.getElementById('todo-input');
  var text  = input.value.trim();
  if (!text) return;

  todoCount += 1;
  var list = document.getElementById('todo-list');
  var item = document.createElement('p');
  item.setAttribute('id', 'todo-' + todoCount);
  item.style.setProperty('margin-top', '6px');
  item.style.setProperty('color', '#334155');
  item.textContent = todoCount + '. ' + text;
  list.appendChild(item);

  input.value = '';
});

// ================================================================
// NEW FEATURE DEMOS (Phases 1–9)
// ================================================================

// ── DOM Events — bubbling ────────────────────────────────────────────────
(function () {
  var log   = document.getElementById('ev-log');
  var outer = document.getElementById('ev-outer');
  var inner = document.getElementById('ev-inner');

  inner.addEventListener('click', function () {
    log.textContent = 'inner clicked';
  });
  outer.addEventListener('click', function () {
    log.textContent = log.textContent + ' > outer bubbled';
  });
  document.getElementById('ev-clear').addEventListener('click', function () {
    log.textContent = '';
  });
})();

// ── Event Control — stopPropagation ──────────────────────────────────────
(function () {
  var log   = document.getElementById('ec-log');
  var outer = document.getElementById('ec-outer');
  var inner = document.getElementById('ec-inner');

  inner.addEventListener('click', function (e) {
    e.stopPropagation();
    log.textContent = 'inner clicked (propagation stopped)';
  });
  outer.addEventListener('click', function () {
    log.textContent = log.textContent + ' > outer bubbled (should NOT appear)';
  });
})();

// ── Custom Events — createEvent / dispatchEvent ─────────────────────────
(function () {
  var target = document.getElementById('ce-target');

  target.addEventListener('hello', function () {
    target.textContent = 'Custom "hello" event received!';
    target.style.backgroundColor = '#bbf7d0';
  });

  document.getElementById('ce-fire').addEventListener('click', function () {
    var evt = document.createEvent('Event');
    evt.initEvent('hello', true, true);
    target.dispatchEvent(evt);
  });
})();

// ── CSS Selectors Level 3 ────────────────────────────────────────────────
(function () {
  function clearHighlight () {
    var items = document.querySelectorAll('#sel-list li');
    for (var i = 0; i < items.length; i++) {
      items[i].style.backgroundColor = '';
    }
  }
  function highlight (sel) {
    clearHighlight();
    var matches = document.querySelectorAll(sel);
    for (var i = 0; i < matches.length; i++) {
      matches[i].style.backgroundColor = '#fef9c3';
    }
  }

  document.getElementById('sel-fruit').addEventListener('click', function () {
    highlight('#sel-list .fruit');
  });
  document.getElementById('sel-nth').addEventListener('click', function () {
    highlight('#sel-list li:nth-child(odd)');
  });
  document.getElementById('sel-attr').addEventListener('click', function () {
    highlight('#sel-list li[data-color="red"]');
  });
  document.getElementById('sel-reset').addEventListener('click', function () {
    clearHighlight();
  });
})();

// ── DOM Mutation ─────────────────────────────────────────────────────────
(function () {
  var container = document.getElementById('mut-container');
  var countEl   = document.getElementById('mut-count');
  var colors    = ['#ef4444','#3b82f6','#22c55e','#f59e0b','#8b5cf6','#14b8a6','#ec4899','#6366f1'];
  var nextId    = 0;

  function updateMutCount () {
    countEl.textContent = 'Items: ' + container.children.length;
  }

  document.getElementById('mut-add').addEventListener('click', function () {
    var el = document.createElement('div');
    el.style.width           = '30px';
    el.style.height          = '30px';
    el.style.borderRadius    = '4px';
    el.style.backgroundColor = colors[nextId % colors.length];
    nextId++;
    container.appendChild(el);
    updateMutCount();
  });

  document.getElementById('mut-clone').addEventListener('click', function () {
    if (container.children.length > 0) {
      var clone = container.children[0].cloneNode(true);
      container.appendChild(clone);
      updateMutCount();
    }
  });

  document.getElementById('mut-remove').addEventListener('click', function () {
    var kids = container.children;
    if (kids.length > 0) {
      container.removeChild(kids[kids.length - 1]);
      updateMutCount();
    }
  });

  document.getElementById('mut-clear').addEventListener('click', function () {
    while (container.children.length > 0) {
      container.removeChild(container.children[0]);
    }
    updateMutCount();
  });
})();

// ── classList API ────────────────────────────────────────────────────────
(function () {
  var target  = document.getElementById('cl-target');
  var readout = document.getElementById('cl-readout');

  function showClass () {
    readout.textContent = 'class="' + target.className + '"';
  }
  showClass();

  document.getElementById('cl-toggle').addEventListener('click', function () {
    target.classList.toggle('active');
    if (target.classList.contains('active')) {
      target.style.backgroundColor = '#bbf7d0';
      target.style.color           = '#166534';
    } else {
      target.style.backgroundColor = '#f1f5f9';
      target.style.color           = '#334155';
    }
    showClass();
  });

  document.getElementById('cl-check').addEventListener('click', function () {
    readout.textContent = 'contains("active") = ' + target.classList.contains('active');
  });
})();

// ── Timers ───────────────────────────────────────────────────────────────
(function () {
  var valEl      = document.getElementById('timer-val');
  var timerCount = 0;
  var intervalId = null;

  document.getElementById('timer-start').addEventListener('click', function () {
    if (intervalId !== null) return;
    intervalId = setInterval(function () {
      timerCount++;
      valEl.textContent = '' + timerCount;
    }, 100);
  });

  document.getElementById('timer-stop').addEventListener('click', function () {
    if (intervalId !== null) { clearInterval(intervalId); intervalId = null; }
  });

  document.getElementById('timer-reset').addEventListener('click', function () {
    if (intervalId !== null) { clearInterval(intervalId); intervalId = null; }
    timerCount = 0;
    valEl.textContent = '0';
  });
})();

// ── Canvas 2D ────────────────────────────────────────────────────────────
(function () {
  var canvas = document.getElementById('demo-canvas');
  if (!canvas) return;
  var ctx = canvas.getContext('2d');
  if (!ctx) return;

  // background
  ctx.fillStyle = '#f8fafc';
  ctx.fillRect(0, 0, 460, 130);

  // filled rectangles
  ctx.fillStyle = '#3b82f6';
  ctx.fillRect(12, 12, 44, 44);
  ctx.fillStyle = '#22c55e';
  ctx.fillRect(66, 12, 44, 44);
  ctx.strokeStyle = '#ef4444';
  ctx.lineWidth = 2;
  ctx.strokeRect(120, 12, 44, 44);

  // circle (arc)
  ctx.beginPath();
  ctx.arc(200, 34, 22, 0, 2 * 3.14159);
  ctx.fillStyle = '#f59e0b';
  ctx.fill();

  // triangle path
  ctx.beginPath();
  ctx.moveTo(240, 12);
  ctx.lineTo(280, 56);
  ctx.lineTo(240, 56);
  ctx.closePath();
  ctx.fillStyle = '#8b5cf6';
  ctx.fill();

  // bezier curve
  ctx.beginPath();
  ctx.moveTo(300, 56);
  ctx.bezierCurveTo(320, 0, 360, 0, 380, 56);
  ctx.strokeStyle = '#ec4899';
  ctx.lineWidth = 2;
  ctx.stroke();

  // text
  ctx.fillStyle = '#1e293b';
  ctx.font = '13px sans-serif';
  ctx.fillText('Canvas 2D rendered inside Lite Browser', 12, 90);

  // divider line
  ctx.beginPath();
  ctx.moveTo(12, 108);
  ctx.lineTo(448, 108);
  ctx.strokeStyle = '#cbd5e1';
  ctx.lineWidth = 1;
  ctx.stroke();

  // gradient bar via filled rects
  var barY = 115;
  for (var i = 0; i < 20; i++) {
    var hue = Math.round(i * 18);
    ctx.fillStyle = 'hsl(' + hue + ', 70%, 55%)';
    ctx.fillRect(12 + i * 22, barY, 20, 8);
  }
})();

// ── Geometry APIs ────────────────────────────────────────────────────────
(function () {
  document.getElementById('geo-btn').addEventListener('click', function () {
    var el   = document.getElementById('geo-target');
    var rect = el.getBoundingClientRect();
    var out  = document.getElementById('geo-readout');
    out.textContent =
      'top=' + Math.round(rect.top) +
      '  left=' + Math.round(rect.left) +
      '  w=' + Math.round(rect.width) +
      '  h=' + Math.round(rect.height) +
      '  |  offset: ' + Math.round(el.offsetWidth) + 'x' + Math.round(el.offsetHeight);
  });
})();

// ── getComputedStyle ─────────────────────────────────────────────────────
(function () {
  document.getElementById('cs-btn').addEventListener('click', function () {
    var el = document.getElementById('cs-target');
    var cs = getComputedStyle(el);
    var out = document.getElementById('cs-readout');
    out.textContent =
      'bg=' + cs.backgroundColor +
      '  color=' + cs.color +
      '  fontSize=' + cs.fontSize +
      '  w=' + cs.width;
  });
})();

// ── Dynamic Styling ──────────────────────────────────────────────────────
(function () {
  var box    = document.getElementById('ds-box');
  var colors = ['#3b82f6','#ef4444','#22c55e','#f59e0b','#8b5cf6','#ec4899'];
  var ci     = 0;

  document.getElementById('ds-color').addEventListener('click', function () {
    ci = (ci + 1) % colors.length;
    box.style.backgroundColor = colors[ci];
  });

  document.getElementById('ds-round').addEventListener('click', function () {
    box.style.borderRadius = box.style.borderRadius === '50%' ? '6px' : '50%';
  });

  document.getElementById('ds-grow').addEventListener('click', function () {
    var w = parseInt(box.style.width) || 80;
    box.style.width  = (w + 20) + 'px';
    box.style.height = (w + 20) + 'px';
  });

  document.getElementById('ds-reset').addEventListener('click', function () {
    box.style.width           = '80px';
    box.style.height          = '80px';
    box.style.borderRadius    = '6px';
    box.style.backgroundColor = '#3b82f6';
    ci = 0;
  });
})();

// ── TreeWalker ───────────────────────────────────────────────────────────
(function () {
  document.getElementById('tw-btn').addEventListener('click', function () {
    var root   = document.getElementById('tw-tree');
    var walker = document.createTreeWalker(root, 1); // SHOW_ELEMENT
    var out    = document.getElementById('tw-output');
    var names  = [];
    var node   = walker.nextNode();
    while (node !== null) {
      var t = node.textContent;
      if (t.length > 12) t = t.substring(0, 12);
      names.push(t.trim());
      node = walker.nextNode();
    }
    out.textContent = names.join(' > ');
  });
})();
