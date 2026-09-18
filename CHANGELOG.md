# Changelog

All notable changes to this package are documented in this file.

## [0.1.3] - 2026-09-18

- Use Unity PlayerSettings.productName for the default Windows executable and macOS app bundle names.
- Fall back to Game only when the Unity product name is empty.

## [0.1.2] - 2026-09-18

- Added Unity meta files for all package assets and folders so Git-based immutable packages import correctly.

## [0.1.1] - 2026-09-18

- Treat SteamCMD exit code 6 as a successful upload only when its output confirms that the app build completed.
- Wait for redirected stdout and stderr to finish before evaluating the process result.

## [0.1.0] - 2026-09-18

- Initial UPM package release.
- Added Windows and macOS Unity player build support.
- Added SteamPipe VDF generation and upload.
- Added macOS signing and notarization workflow.
