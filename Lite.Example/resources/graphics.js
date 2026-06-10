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
