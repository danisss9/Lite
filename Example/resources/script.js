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
