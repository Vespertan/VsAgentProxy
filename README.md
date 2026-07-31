# VS Codex Proxy

Aktualna wersja: `0.4.0`.

Minimalny, lokalny most między Visual Studio a klientem automatyzacji. Rozszerzenie
udostępnia przez Named Pipe wyłącznie jawną listę operacji:

- stan debugera i bieżący proces/wątek,
- call stack bieżącego wątku,
- aktywny dokument, pozycję kursora i zaznaczony tekst,
- locals, arguments oraz kontrolowaną ewaluację wyrażeń,
- listę, dodawanie, usuwanie, włączanie i warunki breakpointów,
- lista, końcówka lub wskazany fragment panelu Output,
- start z debugerem lub bez debugera oraz restart,
- Step Over, Step Into, Step Out, Continue, Break All i Stop Debugging.

Dodatek jest samopisujący: `capabilities` zwraca katalog API, a `documentation`
zwraca skróconą lub pełną dokumentację dla agenta osadzoną w DLL.

Nie ma dowolnego `ExecuteCommand`, ewaluacji wyrażeń ani serwera TCP. Każda instancja
Visual Studio tworzy własny pipe oraz deskryptor w
`%LOCALAPPDATA%\VsCodexProxy\instances`.

## Budowanie i instalacja

```powershell
dotnet build .\src\VsCodexProxy.slnx
```

Zainstaluj wygenerowany plik `.vsix` z katalogu
`src\VsCodexProxy\bin\Debug`. Po ponownym uruchomieniu Visual Studio rozszerzenie
startuje w tle.

## Klient

Klienta można zainstalować globalnie:

```powershell
dotnet pack .\src\VsCodexProxy.Client\VsCodexProxy.Client.csproj -c Release --no-restore
dotnet tool install --global --configfile .\NuGet.Tool.config VsCodexProxy.Client --version 0.4.1
```

Po instalacji preferowana forma wywołania to:

```powershell
vscodex instances
vscodex status
vscodex stackTrace
```

Aktualizacja kolejnej wersji:

```powershell
dotnet tool update --global --configfile .\NuGet.Tool.config VsCodexProxy.Client
```

Bez instalacji globalnej nadal można używać klienta przez `dotnet run`:

```powershell
dotnet run --project .\src\VsCodexProxy.Client -- instances
dotnet run --project .\src\VsCodexProxy.Client -- status
dotnet run --project .\src\VsCodexProxy.Client -- capabilities
dotnet run --project .\src\VsCodexProxy.Client -- documentation --level full
dotnet run --project .\src\VsCodexProxy.Client -- stackTrace
dotnet run --project .\src\VsCodexProxy.Client -- output
dotnet run --project .\src\VsCodexProxy.Client -- output Build 50000
dotnet run --project .\src\VsCodexProxy.Client -- output Build --offset 0 --count 10000
dotnet run --project .\src\VsCodexProxy.Client -- activeDocument
dotnet run --project .\src\VsCodexProxy.Client -- start
dotnet run --project .\src\VsCodexProxy.Client -- startWithoutDebugging
dotnet run --project .\src\VsCodexProxy.Client -- restart
dotnet run --project .\src\VsCodexProxy.Client -- stepOver
dotnet run --project .\src\VsCodexProxy.Client -- --pid 12345 status
dotnet run --project .\src\VsCodexProxy.Client -- locals --frameIndex 0 --maxDepth 2
dotnet run --project .\src\VsCodexProxy.Client -- arguments --frameIndex 0
dotnet run --project .\src\VsCodexProxy.Client -- evaluate --expression "customer.Address.City"
dotnet run --project .\src\VsCodexProxy.Client -- breakpoints
dotnet run --project .\src\VsCodexProxy.Client -- breakpointAdd --file C:\Projekt\Program.cs --line 42 --condition "retryCount > 2" --conditionType whenTrue
dotnet run --project .\src\VsCodexProxy.Client -- breakpointSetEnabled --id 0123456789abcdef --enabled false
dotnet run --project .\src\VsCodexProxy.Client -- breakpointSetCriteria --id 0123456789abcdef --condition "retryCount > 5" --hitCount 3 --hitCountType greaterOrEqual
dotnet run --project .\src\VsCodexProxy.Client -- breakpointRemove --id 0123456789abcdef
```

Odpowiedzi są JSON-em, więc klient nadaje się również do wywoływania z narzędzi
agenta. Jeśli działa kilka instancji VS, klient domyślnie wybiera najnowszą.
Polecenie `instances` pokazuje wszystkie dostępne, a `--pid` pozwala wskazać konkretną.

Parametry `offset` i `count` są indeksami znaków. Odpowiedź `output` zawiera również
`totalChars`, `hasMoreBefore` oraz `hasMoreAfter`, dzięki czemu duży panel można
pobierać porcjami. Bez `offset` zwracana jest końcówka panelu, zgodnie z dawnym
parametrem `maxChars`.

`locals`, `arguments` i `evaluate` ograniczają `maxDepth` do 3 oraz `maxItems` do
500. Ewaluacja wyrażeń może uruchamiać gettery lub metody debugowanego programu,
dlatego `evaluate` jest osobną, jawną operacją.

Breakpoint można wskazać stabilnym `id` (dla breakpointów utworzonych przez proxy)
albo bieżącym, zerowym `index`. Obsługiwane typy warunków to `whenTrue` i
`whenChanged`, a hit count: `none`, `equal`, `greaterOrEqual` i `multiple`.
