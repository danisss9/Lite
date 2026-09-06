// META: variant=?one
// META: variant=?two
test(function () { assert_true(location.search === '?one' || location.search === '?two'); }, 'required');
