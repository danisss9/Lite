document.getElementById('btn').addEventListener('click', function () {
  document.getElementById('output').textContent = 'Hello from JavaScript! Clicks work.';
});

document.getElementById('submit').addEventListener('click', function () {
  var name = document.getElementById('name').value.trim();
  var agreed = document.getElementById('check').checked;
  if (!name) {
    document.getElementById('result').textContent = 'Please enter your name.';
    return;
  }
  if (!agreed) {
    document.getElementById('result').textContent = 'Please check the agreement box.';
    return;
  }
  document.getElementById('result').textContent = 'Hello, ' + name + '! Form submitted.';
});
