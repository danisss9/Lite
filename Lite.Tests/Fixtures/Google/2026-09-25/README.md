# Google Search structural replay, 2026-09-25

These sanitized fixtures retain the public signed-out journey observed from Portugal:
the homepage GET form, a results redirect to consent, the two POST consent choices,
and a results page with a query field and an ordinary result link. Asset URLs are
local, transient tokens are omitted, and text and scripts are reduced to the
behavior under test. They are not verbatim Google responses or a full copy of
Google Search. The separate live smoke check detects changes to the real site.

The replay uses Portuguese labels and keeps scripts, styles, and PNG as separate
resources to exercise the same loading stages as the live pages. The `references/`
images pair Microsoft Edge and Lite renders of all three replay pages at 1280x800
and 800x600. Regenerate them with
`dotnet run --project Lite.Tests -c Release -- --google-references Lite.Tests/Fixtures/Google/2026-09-25/references`.
The tests enforce geometry and interaction. Run
`dotnet run --project Lite.Tests -c Release -- --google-live` for a separate live
smoke check; Google may change the served variant or return a search challenge.
