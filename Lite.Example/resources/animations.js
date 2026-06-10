// ── requestAnimationFrame ────────────────────────────────────────────────
(function () {
  var ball = document.getElementById('raf-ball');
  var pos = 2;
  var dir = 1;
  var rafId = null;
  var running = false;

  function animate() {
    pos += dir * 3;
    if (pos > 420) dir = -1;
    if (pos < 2) dir = 1;
    ball.style.left = pos + 'px';
    if (running) {
      rafId = requestAnimationFrame(animate);
    }
  }

  document.getElementById('raf-start').addEventListener('click', function () {
    if (running) return;
    running = true;
    rafId = requestAnimationFrame(animate);
  });
  document.getElementById('raf-stop').addEventListener('click', function () {
    running = false;
    if (rafId !== null) {
      cancelAnimationFrame(rafId);
      rafId = null;
    }
  });
  document.getElementById('raf-reset').addEventListener('click', function () {
    running = false;
    if (rafId !== null) {
      cancelAnimationFrame(rafId);
      rafId = null;
    }
    pos = 2;
    dir = 1;
    ball.style.left = '2px';
  });
})();

// ── Animation / Transition events ────────────────────────────────────────
(function () {
  var box = document.getElementById('anim-evt-box');
  var log = document.getElementById('anim-evt-log');
  if (!box || !log) return;

  function append(msg) {
    var ts = (Date.now() % 100000).toString();
    log.textContent = '[' + ts + '] ' + msg;
  }

  box.addEventListener('transitionend', function (e) {
    append('transitionend: ' + e.propertyName);
  });
})();
