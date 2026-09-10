# KiwiDefender private preview

`KiwiDefender.0.1.0-preview.1.nupkg` is the actual .NET 8 SDK built from
`/root/kiwidefender/sdks/dotnet` on 2026-09-10 using `dotnet pack -c Release`.
It has no runtime package dependencies. Apache-2.0 license: `KiwiDefender.LICENSE`.
Upstream: https://github.com/bradlnz/kiwidefender/tree/main/sdks/dotnet

Defender.cs SHA-256: `1f6f0b959940f7d56dfabed9a57b10ecb6e04d9f8f86ba2f7c258f3dcc34f8bb`. Package checksum: `SHA256SUMS`.
The package is not published on nuget.org. Repository `NuGet.Config` maps its
exact ID to this checked-in feed, including Docker builds. Update the package,
checksum and Host PackageReference together when taking a new SDK version.

Preview 2 adds automatic build-time C# analysis and background publication of the
bundled report. Its source is in `sdks/dotnet/{ScanReporter.cs,scan,buildTransitive}`
in the KiwiDefender repository. Preview 1 is retained for existing restores.

Preview 3 supports bounded source snippets when `KIWIDEFENDER_SCAN_SNIPPETS=true` is set during the build. The LAN release enables this to show source context in KiwiDefender finding details. Runtime scan credentials and request policy remain separate.
