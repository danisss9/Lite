/* Lite testdriver backend. Shadows WPT's empty placeholder: implements the
 * test_driver_internal surface the vendored tests exercise — click, send_keys,
 * action_sequence (bless builds on click) — by driving the engine's real input
 * pipeline through the __lite_testdriver host function the conformance harness
 * registers (see Lite.TestDriverBridge). Capabilities Lite's harness does not
 * provide (bidi, permissions, virtual authenticators, …) are deliberately left
 * undefined, matching how testdriver.js guards each internal method.
 *
 * Gestures run as macrotasks, not synchronously: page scripts execute in
 * position during the parse, and injected input must land on the finished
 * document exactly like real automation input does. */
(function () {
  var pointer = { x: 0, y: 0 };

  function call(op) { __lite_testdriver(JSON.stringify(op)); }

  function defer(gesture) {
    return new Promise(function (resolve, reject) {
      setTimeout(function () {
        try { gesture(); resolve(); }
        catch (e) { reject(e); }
      }, 0);
    });
  }

  function center(element) {
    var r = element.getBoundingClientRect();
    if (r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth)
      element.scrollIntoView({ block: "center", inline: "center" });
    r = element.getBoundingClientRect();
    var x = Math.max(0, Math.min(innerWidth - 1, r.left + r.width / 2));
    var y = Math.max(0, Math.min(innerHeight - 1, r.top + r.height / 2));
    return [Math.round(x), Math.round(y)];
  }

  function typeChar(ch) {
    call({ op: "key", kind: "down", key: ch });
    call({ op: "char", ch: ch.charCodeAt(0) });
    call({ op: "key", kind: "up", key: ch });
  }

  window.test_driver_internal = {
    click: function (element, coords) {
      var x = coords && typeof coords.x === "number" ? Math.round(coords.x) : null;
      var y = coords && typeof coords.y === "number" ? Math.round(coords.y) : null;
      return defer(function () {
        var c = x === null ? center(element) : [x, y];
        pointer.x = c[0]; pointer.y = c[1];
        call({ op: "refresh" });
        call({ op: "click", x: c[0], y: c[1] });
      });
    },

    send_keys: function (element, keys) {
      return defer(function () {
        call({ op: "refresh" });
        if (typeof element.focus === "function") element.focus();
        for (var i = 0; i < keys.length; i++) typeChar(keys[i]);
      });
    },

    action_sequence: function (sources) {
      return defer(function () {
        call({ op: "refresh" });
        var ticks = 0;
        for (var i = 0; i < sources.length; i++)
          if (sources[i].actions) ticks = Math.max(ticks, sources[i].actions.length);
        for (var t = 0; t < ticks; t++) {
          for (var i = 0; i < sources.length; i++) {
            var source = sources[i];
            var action = source.actions && source.actions[t];
            if (!action || action.type === "pause") continue;
            if (source.type === "pointer") {
              if (action.type === "pointerMove") {
                var origin = action.origin || "viewport";
                var x, y;
                if (origin === "viewport") { x = action.x || 0; y = action.y || 0; }
                else if (origin === "pointer") { x = pointer.x + (action.x || 0); y = pointer.y + (action.y || 0); }
                else { var c = center(origin); x = c[0] + (action.x || 0); y = c[1] + (action.y || 0); }
                pointer.x = Math.round(x); pointer.y = Math.round(y);
                call({ op: "pointer", kind: "move", x: pointer.x, y: pointer.y });
              } else if (action.type === "pointerDown") {
                call({ op: "pointer", kind: "down", x: pointer.x, y: pointer.y });
              } else if (action.type === "pointerUp") {
                call({ op: "pointer", kind: "up", x: pointer.x, y: pointer.y });
              }
            } else if (source.type === "key") {
              if (action.type === "keyDown") {
                call({ op: "key", kind: "down", key: action.value });
                // WebDriver keyDown presses the key, so a printable value also types.
                if (action.value.length === 1) {
                  var code = action.value.charCodeAt(0);
                  if (code >= 32 && code < 127) call({ op: "char", ch: code });
                }
              }
              else if (action.type === "keyUp") call({ op: "key", kind: "up", key: action.value });
            }
          }
        }
      });
    },
  };
})();
