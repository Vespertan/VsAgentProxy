# VS Codex Proxy

Aktualna wersja: `0.6.0` (rozszerzenie i klient), kontrakt 2.

Minimalny, lokalny most między Visual Studio a klientem automatyzacji. Rozszerzenie
udostępnia przez Named Pipe wyłącznie jawną listę operacji:

- stan debugera i bieżący proces/wątek,
- projekty załadowane i niezaładowane, błędy ładowania udostępniane przez system projektu oraz wybór projektów startowych,
- diagnostyki Error List z filtrami i stronicowaniem, niezależne od filtrów okna IDE,
- `launchCheck`: dostępność Start i obserwowane przeszkody w uruchomieniu,
- dokumenty, jawny zapis i bezpieczny reload projektu,
- wybór projektów startowych oraz profili .NET/JavaScript z menu IDE,
- build/rebuild/clean z identyfikatorem operacji, anulowaniem, wynikiem i historią zdarzeń,
- zakresy JS/TS, stronicowane zmienne, powód zatrzymania i wybór wątku/ramki,
- call stack bieżącego wątku,
- aktywny dokument, pozycję kursora i zaznaczony tekst,
- locals, arguments oraz kontrolowaną ewaluację wyrażeń,
- listę, dodawanie, usuwanie, włączanie i warunki breakpointów,
- lista, końcówka lub wskazany fragment panelu Output,
- start z debugerem lub bez debugera oraz restart,
- Step Over, Step Into, Step Out, Continue, Break All i Stop Debugging.

Dodatek jest samopisujący: `capabilities` zwraca katalog API, a `documentation`
zwraca skróconą lub pełną dokumentację dla agenta osadzoną w DLL.

Nie ma dowolnego `ExecuteCommand` ani serwera TCP. `evaluate` jest jawne;
enumeracja zmiennych DTE również może wywołać obliczenia adaptera. Każda instancja
Visual Studio tworzy własny pipe oraz deskryptor w
`%LOCALAPPDATA%\VsCodexProxy\instances`.

## Budowanie i instalacja

```powershell
dotnet build .\src\VsCodexProxy.slnx
```

Zainstaluj wygenerowany plik `.vsix` z katalogu
`src\VsCodexProxy\bin\Debug\net472`. Po ponownym uruchomieniu Visual Studio rozszerzenie
startuje w tle.

## Klient

Klienta można zainstalować globalnie:

```powershell
dotnet pack .\src\VsCodexProxy.Client\VsCodexProxy.Client.csproj -c Release --no-restore
dotnet tool install --global --configfile .\NuGet.Tool.config VsCodexProxy.Client --version 0.6.0
```

Po instalacji preferowana forma wywołania to:

```powershell
vscodex instances
vscodex status
vscodex stackTrace
vscodex projects
vscodex launchCheck
vscodex diagnostics --severity error --count 200
vscodex diagnostics --origin project-load
vscodex output --paneId <guid> --offset 0 --count 10000
vscodex --pid 12345 documents
vscodex --pid 12345 projectReload --path C:/Projekt/App.esproj
vscodex --pid 12345 setStartupProjects --path C:/Projekt/App.esproj
vscodex --pid 12345 launchProfiles --project C:/Projekt/App.esproj
vscodex --pid 12345 selectLaunchProfile --project C:/Projekt/App.esproj --name "Demo (Chrome)"
vscodex --pid 12345 solutionLaunchProfiles
vscodex --pid 12345 selectSolutionLaunchProfile --name "Demo" --scope shared
vscodex --pid 12345 build --wait true --idempotencyKey build-001
vscodex --pid 12345 start --wait true --idempotencyKey start-001
vscodex --pid 12345 waitForState --state break --waitTimeoutMs 60000
vscodex --pid 12345 scopes --frameIndex 0
vscodex --pid 12345 variables --reference <id> --offset 0 --count 50
vscodex --pid 12345 events --afterSequence 0
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

Lista Output zachowuje `panes` (nazwy), a dodatkowo zwraca `paneDetails` z GUID-ami.
Odczyt pustego bufora zwraca pusty tekst; rzeczywisty błąd dostępu zawiera
`errorCode`, `hresult` i `details`. Preferowany jest odczyt zakresu z bufora VS;
fallback DTE odczytuje cały tekst i zachowuje wcześniejsze indeksy UTF-16/CRLF.
Niezainicjalizowany panel może wymagać chwilowej aktywacji; proxy przywraca wybór
panelu, jeśli wcześniej istniał, widoczność Output i aktywne okno oraz raportuje
`restorationErrors`. Brak wcześniejszego wyboru oznacza `previousPaneAvailable: false`.

`projects` odróżnia `loaded`, `unloaded` i potwierdzone przez hierarchię `failed`.
Nie każdy system projektu udostępnia błąd ładowania — odpowiedź zawiera dostępność
danych i błędy odczytu. `launchCheck` jest odczytem: nie uruchamia aplikacji ani nie
zmienia ustawień. Aktywny profil odczytuje osobne `launchProfiles`.

`diagnostics` zwraca źródło, severity, origin, projekt, plik, kod i komunikat.
Sprawdzaj `isStable`, `complete` i `readErrors`: dostawcy publikują wyniki
asynchronicznie, więc pierwszy pusty odczyt nie dowodzi braku błędów. Numery linii
i kolumn są liczone od 1. Szczegóły: [protokół agenta](docs/AGENT_PROTOCOL.md).

Testy kontraktu i obsługi snapshotów:

```powershell
dotnet test .\src\VsCodexProxy.Tests\VsCodexProxy.Tests.csproj
```

Weryfikacja w oddzielnej instancji VS 2026 obejmuje .NET oraz kopię AngularControls:
[raport 0.6.0](docs/VALIDATION_0_6_0.md). `currentHits` może być `null`; licznik DTE
bywa niewiarygodny, dlatego dostępne jest osobne `observedHits`. Stare uchwyty
zmiennych po kroku/continue zwracają `staleReference`.

Ograniczenia są jawne: istniejące terminale JSPS nie udostępniają historii przez
sprawdzone publiczne API; pełne source map i wersja aktywnego Angular Language
Service pozostają niedostępne. Profile `.slnLaunch` są obsługiwane w VS 2026 przez
wersjonowany adapter wewnętrznego kontraktu, który może zwrócić `unsupported` po
zmianie wersji VS. Diagnostyki HTML
zależą od publikacji przez Error List; pusty wynik nie potwierdza działania ALS.

`locals`, `arguments` i `evaluate` ograniczają `maxDepth` do 3 oraz `maxItems` do
500. Ewaluacja wyrażeń może uruchamiać gettery lub metody debugowanego programu,
dlatego `evaluate` jest osobną, jawną operacją.

Breakpoint można wskazać stabilnym `id` (dla breakpointów utworzonych przez proxy)
albo bieżącym, zerowym `index`. Obsługiwane typy warunków to `whenTrue` i
`whenChanged`, a hit count: `none`, `equal`, `greaterOrEqual` i `multiple`.
