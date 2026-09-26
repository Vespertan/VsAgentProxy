# Document save regression verification

Date: 2026-09-26. Visual Studio 2026 Community, separate `/RootSuffix Exp`
instance and disposable solution under `artifacts/document-save-probe`.
The user's main Visual Studio instance was not restarted or upgraded.

## Reproduction

The installed 0.7.1 extension detected the solution's dirty state, but saving
the loaded `.slnx` returned `saved: false`, `allSaved: false` and
`Document does not support saving.` Ordinary text editor documents saved
successfully. The solution's RDT document data did not expose
`IVsPersistDocData`.

## Live verification of the fix

A temporary fixture package, included only in the experimental test build,
added a solution folder through `Solution2.AddSolutionFolder` and edited a
separate text buffer. It was excluded from the final build.

1. `documents` reported both `SaveProbe.slnx` and `Unsaved.txt` as dirty.
   Neither change was present on disk.
2. `saveDocuments --path <SaveProbe.slnx>` returned `saved: true` and
   `allSaved: true`.
3. `documents` reported the solution as clean, but `Unsaved.txt` remained
   dirty. The solution folder appeared in the `.slnx` file; the text file's
   disk contents were unchanged.
4. A request selecting both the already-clean solution and the dirty text
   document returned `allSaved: true`. Both documents became clean and the
   text change appeared on disk.

## Automated regression coverage

`DocumentSaveTests` checks exact-cookie routing and save flags, refreshed
dirty-state verification, cancellation/non-success, save/read errors, nil or
stale cookies, and document replacement during saving.

```powershell
dotnet build src/VsAgentProxy.slnx --no-restore --no-incremental --verbosity minimal
dotnet test src/VsAgentProxy.Tests/VsAgentProxy.Tests.csproj --no-build --no-restore --verbosity minimal
```

Final build: zero warnings/errors. All 53 tests passed. The final VSIX and
experimental deployment were checked to exclude the temporary fixture package.

The live test covers the `.slnx` regression and text documents. It does not
establish compatibility with every third-party project/document provider.
