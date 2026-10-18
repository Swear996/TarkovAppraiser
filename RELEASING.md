# Releasing a new version

1. Add a new entry at the top of `Changelog.cs` – short, for players (1–5 lines, what is new or better).
2. Set the same version in `Changelog.Current` and in `<Version>` of `TarkovPriceOverlay.csproj`
   (one digit after the point: 1.1, 1.2 … 1.9, then 2.0).
3. Run `test.bat` and `build.bat`, try the new `dist\TarkovAppraiser.exe`.
4. Commit and push, then create the tag:

   ```
   git tag v1.1
   git push origin v1.1
   ```

GitHub Actions builds `TarkovAppraiser.exe` (small, needs .NET 10) and publishes the release
(`.github/workflows/release.yml`). The release notes are made from the changelog entry, plus a short
"How to use" (F8, automatic mode, Shift+F8) and the SHA-256 checksums. The build stops if the tag,
`Changelog.Current` and `<Version>` do not match. The update check in the app finds the new release by itself.

## Before the very first release

- `Updates.cs`: `Repo` is set to `"Swear996/TarkovAppraiser"` – this switches on the update check, the GitHub
  button in the settings and "Report a problem".
- `docs/ingame.gif`: a ~5 second clip from the game (hover an item, price box appears), e.g. with ScreenToGif.
- Add a `LICENSE` file (e.g. MIT) so others know what they may do with the code.
- On GitHub: *Settings › Actions › General › Workflow permissions* → *Read and write permissions*
  (needed to publish the release).
