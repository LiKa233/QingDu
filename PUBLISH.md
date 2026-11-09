# Release procedure

Use only the clean source tree produced by scripts/Package.ps1. Never upload an enclosing workspace, user application data, encrypted credential files, personal voices, logs or diagnosis recordings.

1. Verify the explicit target OWNER/REPO and the authenticated GitHub account. The release is public.
2. Run scripts/Test.ps1, scripts/Package.ps1 and scripts/Scan-Release.ps1 on the staged tree and dist directory. Check SHA256SUMS.txt and the runtime's FILES.sha256 against actual files.
3. Review the complete initial commit for secrets, personal paths and unexpected files. Use GitHub's web uploader with relative paths or push only the clean repository; never add an existing workspace's Git history.
4. Create the v0.2.6 tag at the reviewed source commit and publish the release with the Windows x64 runtime ZIP, project source ZIP, corresponding source ZIP and SHA256SUMS.txt. Keep the complete corresponding source available alongside the binary.
5. If the tag or same-named assets already exist and their contents differ, stop. Never force-push a published tag or replace released files. Fixes need a new version.
6. Download the repository archive and release assets again, verify hashes and repeat the privacy scan. Record any scanner limits honestly; heuristic scanning is not a mathematical guarantee.

Actions runs only synthetic tests, build and packaging. Do not add Fish or MiMo API credentials to workflow files, repository settings or GitHub Actions secrets. Users enter their own credentials in their own installation.
