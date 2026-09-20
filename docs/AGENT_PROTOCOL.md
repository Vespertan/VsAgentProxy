# VS Codex Proxy — pełna dokumentacja dla agenta

## 1. Cel i architektura

VS Codex Proxy jest lokalnym mostem pomiędzy agentem a Visual Studio. Rozszerzenie
VSIX działa wewnątrz procesu `devenv.exe`, korzysta z EnvDTE na głównym wątku VS i
udostępnia zamkniętą listę operacji przez Named Pipe. Nie udostępnia serwera TCP ani
dowolnego `ExecuteCommand`.

Każda instancja tworzy:

```text
pipe:       VsCodexProxy-{PID}
descriptor: %LOCALAPPDATA%\VsCodexProxy\instances\{PID}.json
```

Deskryptor zawiera `pid`, `pipe`, `solution`, `startedUtc` i `sessionId`.
Ścieżka jest aktualizowana po otwarciu/zamknięciu rozwiązania. Klient sprawdza
PID i sesję odpowiedzią `ping`; do jednoznacznego wyboru instancji używaj PID.

## 2. Klient CLI

`vscodex` jest globalnym narzędziem .NET i może być uruchamiany z dowolnego
katalogu. Instalacja z repozytorium:

```powershell
dotnet pack .\src\VsCodexProxy.Client\VsCodexProxy.Client.csproj -c Release --no-restore
dotnet tool install --global --configfile .\NuGet.Tool.config VsCodexProxy.Client --version 0.6.0
```

Fallback bez instalacji globalnej:

```powershell
dotnet run --project .\src\VsCodexProxy.Client --no-build -- --pid 12345 status
```

### Wybór instancji

```powershell
vscodex instances
vscodex --pid 12345 status
```

Bez `--pid` klient wybiera deskryptor z najnowszym `startedUtc`. Nie polegaj na tym,
gdy użytkownik ma więcej niż jedną instancję VS.

### Format odpowiedzi

Sukces:

```json
{
  "id": "request-id",
  "ok": true,
  "result": {}
}
```

Błąd:

```json
{
  "id": "request-id",
  "ok": false,
  "error": "opis błędu"
}
```

Kody wyjścia klienta:

| Kod | Znaczenie |
|---:|---|
| 0 | odpowiedź `ok: true` albo poprawne polecenie lokalne |
| 1 | serwer zwrócił `ok: false` lub brak polecenia |
| 2 | nie znaleziono wybranej instancji VS |
| 3 | błąd połączenia lub timeout klienta |
| 4 | timeout `waitForState` albo niepotwierdzony (`unknown`) wynik operacji |

`--wait true` po komendzie z `operationId` czeka na jej zakończenie; zwraca 1
dla `failed`/`cancelled`, nawet gdy sam odczyt statusu miał `ok: true`.
Limit połączenia: `--connectTimeoutMs 5000`, limit odpowiedzi:
`--responseTimeoutMs 15000`, limit oczekiwania: `--waitTimeoutMs 120000`.
Timeout nigdy nie oznacza anulowania operacji w IDE.

Tablice podawaj jako powtarzalne `--path` dla `saveDocuments`/`setStartupProjects`,
powtarzalne `--name` dla `projectProperties`, albo `--pathsJson '["C:/a.csproj"]'`.
`--paramsJson` przyjmuje cały obiekt parametrów. Wartości opcji są obowiązkowe.

## 3. Zalecany przebieg sesji

1. Wywołaj `instances` i wybierz PID.
2. Wywołaj `status`.
3. Jeśli trzeba, ustaw breakpointy przez `breakpointAdd`.
4. Uruchom aplikację przez `start`.
5. Po zatrzymaniu pobierz `stackTrace`, `locals`, `arguments` i Output.
6. Steruj przez `stepOver`, `stepInto`, `stepOut` lub `continue`.
7. Po każdej komendzie sterującej ponownie sprawdź `status`.
8. Na końcu opcjonalnie usuń breakpointy proxy i wywołaj `stop`.

Tryby zwracane przez `status`:

- `dbgDesignMode` — brak aktywnego debugowania,
- `dbgRunMode` — debugowany program działa,
- `dbgBreakMode` — program jest zatrzymany.

## 4. Operacje odczytu

### `capabilities` i `documentation`

```powershell
vscodex --pid 12345 capabilities
vscodex --pid 12345 documentation --level short
vscodex --pid 12345 documentation --level full
```

`capabilities` zwraca maszynowy katalog metod, parametrów, efektów ubocznych i
ostrzeżeń. `documentation` zwraca Markdown osadzony w DLL rozszerzenia. Wersja
`full` jest kopią tego dokumentu z chwili budowania VSIX i stanowi źródło prawdy
dla aktualnie zainstalowanej wersji dodatku.

### `ping`

Bez parametrów. Zwraca wersję proxy i PID procesu VS.

### `status`

Bez parametrów. Zwraca:

```json
{
  "mode": "dbgBreakMode",
  "currentProcess": "Application.exe",
  "currentThread": "Main Thread",
  "solution": "C:\\Projekt\\Application.sln"
}
```

### `stackTrace`

Bez parametrów. Zwraca ramki bieżącego wątku: `index`, `depth`, `isCurrent`,
`function`, `module`, `moduleName`, `language`, `file`, `line`, `column`,
`userCode`, `threadId` i `threadName`. `column` ma obecnie wartość `null`, ponieważ
interfejs DTE stosu nie udostępnia kolumny instrukcji.

### `projects` (od 0.5.0)

```powershell
vscodex --pid 12345 projects
```

Zwraca `solution`, `isOpen`, `isFullyLoaded`, tablicę `projects`, wybór DTE
`startupProjects`, `startupProjectsAvailable` i `readErrors`. Każdy projekt ma
`id` (GUID w rozwiązaniu), `name`, `path`, `uniqueName`, `typeGuid` (typ projektu
z DTE), `hierarchyTypeGuid` (typ elementu hierarchii), `type`
(rozszerzenie pliku lub `solutionFolder`), `isSolutionFolder`, `isStartup`
i `loadState`: `loaded`, `unloaded` albo `failed`.

Stan `failed` wymaga `IVsHierarchy.IsFaulted == true`. `loadError` pochodzi
z `FaultMessage`; pola `loadErrorAvailability`, `loadErrorSource` oraz
`loadErrorUnavailableReason` informują o ograniczeniach providera.
Niezaładowany projekt nie jest automatycznie uznawany za uszkodzony.
`isStartup: null` oznacza niedostępny odczyt wyboru DTE.

### `diagnostics` (od 0.5.0)

```powershell
vscodex --pid 12345 diagnostics --severity error --count 200
vscodex --pid 12345 diagnostics --origin build --offset 0 --count 100
vscodex --pid 12345 diagnostics --origin project-load
vscodex --pid 12345 diagnostics --file C:\Projekt\app.html
```

Filtry: `severity` = `error|warning|message|unknown`, `origin` =
`build|intellisense|project-load|unknown`, `project` = dokładna nazwa lub GUID,
`file` = pełna ścieżka. Porównania projektu/pliku ignorują wielkość liter.
`offset` >= 0, `count` = 1–2000 (domyślnie 200). Filtry łączone są przez AND.

Odpowiedź: `items`, `offset`, `count`, `totalCount`, `hasMore`, `isStable`,
`complete`, `providers`, `readErrors`, `capturedUtc`, `scope`, `limitations`.
Element zawiera `severity`, `origin`, `source`, `sourceType`, `provider`,
`project`, `projectId`, `file`, `line`, `column`, `code`, `message`; wpisy Error
List dodatkowo `rawOrigin` i `buildTool`. Współrzędne są liczone od 1;
niedostępne dane mają wartość null albo `unknown`.

Odczyt obejmuje źródła Error List niezależnie od filtrów widocznego okna oraz
aktualne błędy hierarchii projektów. Kategoria SDK `ErrorSource.Other` oznacza
diagnostyki kompilacji wywołanej w tle i jest mapowana na `intellisense`;
nie potwierdza to działania konkretnej usługi językowej. Błędy `project-load`
nie mają odgadywanych kodów ani pozycji w pliku. Wpisy z różnych źródeł mogą
opisywać ten sam problem; nie są automatycznie usuwane jako duplikaty.

Subskrypcje są utrzymywane między wywołaniami. Pierwsze wyniki mogą dotrzeć po
odpowiedzi na pierwsze żądanie: sprawdzaj `isStable` i powtórz odczyt. `complete`
oznacza brak wykrytych błędów odczytu, a nie ukończenie analizy IDE.
Stronicowanie dotyczy bieżącego odczytu; dane mogą zmienić się między stronami.
Odczyt nie otwiera dokumentów i nie wymusza ponownej analizy.

### `launchCheck` (od 0.5.0)

```powershell
vscodex --pid 12345 launchCheck
```

Zwraca `canExecuteStartCommand` (true/false/null), `startAction`, `mode`,
`buildState`, `configuration`, `lastBuildFailedProjects` (gdy build został
ukończony), dostępność obu komend w `commands`, `reasons`
z kodem, dowodem i pewnością, `reasonUnknown`, `projectState` i `readErrors`.
Nie uruchamia aplikacji, nie buduje i nie zmienia projektu startowego.

W break mode `Debug.Start` kontynuuje istniejącą sesję. `IsAvailable` nie
gwarantuje powodzenia startu i nie udostępnia wewnętrznego powodu odmowy VS.
`reasonUnknown` pozostaje true dla niedostępnej/nieodczytanej komendy nawet,
jeśli wykryto możliwe przeszkody. Brak projektu w DTE ma pewność `suspected`,
ponieważ inny provider uruchomienia może stosować własny wybór.
Od 0.6.0 `launchProfilesAvailability: perProject` wskazuje osobny odczyt
`launchProfiles(project)`. Dostępność i aktywny profil zależą od providera projektu.

### `locals` i `arguments`

```powershell
vscodex --pid 12345 locals --frameIndex 0 --maxDepth 2 --maxItems 200
vscodex --pid 12345 arguments --frameIndex 0 --maxDepth 1 --maxItems 100
```

Parametry:

| Parametr | Domyślnie | Zakres | Znaczenie |
|---|---:|---:|---|
| `frameIndex` | 0 | >= 0 | zerowy indeks ramki stosu |
| `maxDepth` | 1 | 0–3 | głębokość `DataMembers` |
| `maxItems` | 100 | 1–500 | globalny limit zwracanych wyrażeń |

Element zawiera `name`, `type`, `value`, `isValid` i opcjonalne `children` albo
`childrenError`. `truncated: true` oznacza osiągnięcie limitu.

### `evaluate`

```powershell
vscodex --pid 12345 evaluate --expression "customer.Address.City" --timeoutMs 1000 --maxDepth 1 --maxItems 100
```

Parametry: wymagane `expression`; opcjonalne `timeoutMs` (100–10000), `maxDepth`
i `maxItems`. Ewaluacja odbywa się w bieżącej ramce debugera. Może uruchamiać
gettery, przeciążenia debug display lub metody. Używaj jej tylko jawnie; do zwykłej
inspekcji preferuj `locals` i `arguments`.

### `activeDocument`

Bez parametrów. Zwraca `available`, `name`, `path`, `line`, `column`,
`selectedText` i `selectionTruncated`. Zaznaczenie jest ograniczone do 100000 znaków.

### `output`

Lista paneli:

```powershell
vscodex --pid 12345 output
```

Końcówka panelu (zgodność wsteczna):

```powershell
vscodex --pid 12345 output Debug 20000
```

Zakres znaków:

```powershell
vscodex --pid 12345 output Debug --offset 10000 --count 5000
```

`count` musi mieścić się w zakresie 1–200000. Bez `offset` zwracane jest ostatnie `count`
znaków. Odpowiedź zawiera `offset`, faktyczne `count`, `totalChars`,
`hasMoreBefore` i `hasMoreAfter`.

Od 0.5.0 lista dodatkowo zawiera `paneDetails: [{ name, id }]` z GUID-em panelu.
Można użyć `--paneId <guid>` zamiast nazwy; podanie obu selektorów jest błędem.
Odpowiedź odczytu zawiera `paneId` i `source` (`textBuffer` lub fallback `dte`).
Pusty bufor zwraca `text: ""`, `count: 0`, `totalChars: 0`; nie jest odczytywany
przez operację tekstową wymagającą niepustego zakresu. Błędu E_FAIL nie traktuje
się automatycznie jako pustego bufora. `outputReadFailed` zawiera próbę odczytu
bufora/DTE oraz HRESULT w `details.attempts`. `outputPaneNotFound` jest osobnym
błędem. Jeśli VS nie utworzył jeszcze bufora i DTE zwraca E_FAIL, odczyt może
przejściowo aktywować panel, po czym przywraca poprzedni panel, widoczność Output
i aktywne okno. Odpowiedź oznacza to przez `paneInitialized: true`; ewentualne
błędy przywracania trafiają do `restorationErrors`. Jeżeli wcześniej żaden panel
nie był wybrany, nie ma wyboru do przywrócenia: `previousPaneAvailable` i
`paneSelectionRestored` są false; pozostaje wybór odczytanego panelu, przy
zachowanej widoczności Output i aktywnym oknie. Niepoprawne limity od 0.5.0 są odrzucane
przez `invalidParameters`, zamiast niejawnego przycinania.

### `breakpoints`

Bez parametrów. Zwraca m.in. `id`, `index`, `enabled`, `file`, `line`, `column`,
`function`, `condition`, `conditionType`, `currentHits`, `hitCount`,
`hitCountType` i `locationType`.

`id` jest dostępne dla breakpointów utworzonych przez proxy. Breakpoint ręczny
może mieć tylko `index`.

## 5. Sterowanie uruchomieniem

Operacje bez parametrów:

| Operacja | Działanie |
|---|---|
| `start` | uruchamia skonfigurowany projekt startowy z debugerem |
| `startWithoutDebugging` | uruchamia projekt bez debugera |
| `restart` | restartuje sesję debugowania |
| `continue` | kontynuuje; w design mode zachowuje się jak start |
| `break` | Break All |
| `stop` | kończy debugowanie |
| `stepOver` | Step Over |
| `stepInto` | Step Into |
| `stepOut` | Step Out |

Proxy przed wykonaniem sprawdza `Command.IsAvailable`. Niedostępna komenda zwraca
`ok: false`. Odpowiedź `accepted: true` potwierdza wysłanie komendy do VS, a nie
osiągnięcie nowego stanu — stan potwierdzaj przez `status`.

Od 0.5.0 niedostępna komenda zwraca dodatkowo `errorCode: commandUnavailable`
oraz sugestię `launchCheck`. Obsłużone wyjątki zachowują tekst `error` i dodają
`errorCode`, `hresult`, `details`. Nie każda starsza odpowiedź błędu ma te pola.

## 6. Zarządzanie breakpointami

### Dodawanie — `breakpointAdd`

Należy podać dokładnie jeden typ lokalizacji:

- `file` oraz opcjonalne `line` (domyślnie 1), `column` (domyślnie 1),
- `function`,
- `data` oraz opcjonalne `dataCount`,
- `address`.

Wspólne parametry:

- `condition`: tekst warunku,
- `conditionType`: `whenTrue` lub `whenChanged`,
- `language`: opcjonalna nazwa języka,
- `hitCount`: liczba trafień,
- `hitCountType`: `none`, `equal`, `greaterOrEqual` lub `multiple`,
- `enabled`: domyślnie `true`.

```powershell
vscodex --pid 12345 breakpointAdd --file C:\Projekt\Program.cs --line 42 --condition "retryCount > 2" --conditionType whenTrue
```

Odpowiedź jest tablicą, ponieważ VS może utworzyć kilka breakpointów, np. dla
przeciążonej funkcji. Każdy otrzymuje osobne stabilne `id`.

### Włączanie i wyłączanie — `breakpointSetEnabled`

```powershell
vscodex --pid 12345 breakpointSetEnabled --id <id> --enabled false
vscodex --pid 12345 breakpointSetEnabled --index 0 --enabled true
```

### Kryteria — `breakpointSetCriteria`

```powershell
vscodex --pid 12345 breakpointSetCriteria --id <id> --condition "retryCount > 5" --conditionType whenTrue --hitCount 3 --hitCountType greaterOrEqual
```

Wszystkie parametry kryteriów są opcjonalne; niepodane zachowują dotychczasową
wartość. Pusty `condition` usuwa warunek. Zmiana kryteriów jest obsługiwana dla
breakpointów plikowych i funkcyjnych. DTE nie pozwala zmieniać tych pól bezpośrednio,
więc proxy usuwa i odtwarza breakpoint, zachowując jego stan oraz ID.

### Usuwanie — `breakpointRemove`

```powershell
vscodex --pid 12345 breakpointRemove --id <id>
vscodex --pid 12345 breakpointRemove --index 0
```

Preferuj `id`. `index` jest zerowy i niestabilny po dodaniu lub usunięciu elementu.

## 7. Surowy protokół Named Pipe

Klient wysyła jeden obiekt JSON w jednej linii UTF-8:

```json
{"id":"abc","method":"locals","params":{"frameIndex":0,"maxDepth":1}}
```

Serwer odpowiada jedną linią JSON. Nazwy metod i parametrów są rozróżniane
wielkością liter. Serwer obsługuje jednego podłączonego klienta naraz i po
rozłączeniu przyjmuje kolejnego.

## 8. Projekty, operacje i inspekcja od wersji 0.6.0

### Dokumenty i konfiguracja uruchamiania

| Metoda | Parametry | Zachowanie |
|---|---|---|
| `documents` | brak | Dokumenty RDT, ścieżka, projekt i `dirty: true/false/null`. `null` nie oznacza czystego dokumentu. |
| `saveDocuments` | `paths: string[]` | Jawny zapis wybranych otwartych dokumentów, bez Save As; wyniki per plik i `allSaved`. Zapis wielu plików nie jest transakcją. |
| `projectReload` | `path` | Design mode, brak build, w pełni załadowana solucja. Blokuje każdy brudny lub nieodczytany dokument, także współdzielony. Loaded: unload/load, unloaded/failed: reload. `loaded` opisuje odczyt wyniku. |
| `startupProjects` | brak | `paths`, `uniqueNames`, dostępność i źródło DTE. |
| `setStartupProjects` | `paths: string[]` | Waliduje całą listę i loaded, ustawia kolejność, sprawdza readback. Przy błędzie próbuje rollback i zwraca stan faktyczny. |
| `launchProfiles` | `project: path\|guid` | Profile i aktywny wybór z menu IDE przez `IVsProjectCfgDebugTargetSelection`, także eksport CPS. `configuredProfiles` pochodzi osobno z plików; nie dowodzi aktywności. |
| `selectLaunchProfile` | `project`, `name` | Tylko istniejący, jednoznaczny profil, w stanie idle. `accepted` potwierdza przyjęcie, `applied` readback. CPS publikuje asynchronicznie: ponów odczyt `launchProfiles`. |
| `solutionLaunchProfiles` | brak | Profile `.slnLaunch` shared/user z aktywną nazwą, akcjami, kolejnością i celami debugowania. Odczyt live przez wersjonowany adapter VS 2026; zgodność jest jawna. |
| `selectSolutionLaunchProfile` | `name`, `scope?` | Ustawia natywny profil i weryfikuje wszystkie akcje, kolejność oraz cele. `scope` rozstrzyga profile shared/user o tej samej nazwie; przy błędzie wykonywany jest rollback. |
| `projectProperties` | `project`, `names: string[]` | Właściwości aktywnej konfiguracji przez `IVsBuildPropertyStorage`; błędy per właściwość. |
| `configurations` | brak | Konfiguracje i platformy solucji, aktualny wybór. |

`launchProfiles` rozróżnia właściwości obliczone przez MSBuild, konfigurację
zapisaną na dysku i finalną komendę procesu. Nie udaje znajomości końcowych
podstawień debug adaptera. Nie zwraca zmiennych środowiskowych profili.

### Operacje i zdarzenia

`build`, `rebuild`, `clean`, `start`, `startWithoutDebugging`, `restart` zwracają
`accepted` i `operationId`. `operationStatus --id <id>` zwraca stan `queued`,
`running`, `succeeded`, `failed`, `cancelled` albo `unknown`; faza jest osobnym
polem. Build potwierdzają zdarzenia `IVsUpdateSolutionEvents`, a start z debugerem
zdarzenie run/break. To nie jest test gotowości HTTP. Dla Ctrl+F5 brak potwierdzenia
procesu daje `unknown`. Brak zdarzenia kończącego przez 2 minuty daje `unknown`,
bez anulowania pracy VS. `cancelBuild` korzysta z rzeczywistej obsługi anulowania.

Jednocześnie śledzona jest jedna operacja. Korelacja `exclusiveCommandWindow`
oznacza powiązanie w oknie wysłanej komendy, nie identyfikator transakcji VS.
Build bez aktywnej operacji ma źródło `external`. Ręczne działania użytkownika
w trakcie oczekiwania mogą utrudnić korelację. Rejestr zachowuje maksymalnie 128
operacji, przez godzinę, w ramach jednej sesji proxy.

`events --afterSequence 0 --limit 100 [--sessionId <id>]` zwraca do 500 zdarzeń
z bufora 512. Następny kursor to `nextSequence`. Sprawdzaj `historyLost` oraz
`sessionChanged`. Historia obejmuje build, debugger, wyjątki, zmiany wykonane
przez proxy oraz dostępne błędy/powiązania breakpointów z silnika. Są to zdarzenia
obserwowane od subskrypcji; nie ma odtwarzania wcześniejszej historii ani push.

Przy ponawianiu mutacji używaj tego samego `idempotencyKey`. Proxy pamięta do 256
odpowiedzi przez godzinę; klucz należy do sesji proxy. Ten sam klucz i inne
parametry dają `idempotencyConflict`. `replayed: true` zwraca oryginalną odpowiedź,
więc aktualny wynik operacji odczytaj przez `operationStatus`. Po restarcie proxy
lub wygaśnięciu klucza najpierw sprawdź stan IDE — deduplikacja nie jest trwała.

```powershell
vscodex --pid 12345 build --wait true --idempotencyKey build-001
vscodex --pid 12345 start --wait true --idempotencyKey start-001
vscodex --pid 12345 waitForState --state break --waitTimeoutMs 60000
vscodex --pid 12345 waitForState --operationId <id> --waitTimeoutMs 120000
```

### Zmienne, breakpointy i kontekst

`scopes --frameIndex 0` zwraca uchwyty Locals/Arguments. `variables --reference
<id> --offset 0 --count 100` stronicuje dzieci (limit 500). `hasChildren` jest
niezależne od `isValid`: zakresy JS Module/Global często nie mają wartości
skalarnej. Uchwyty wygasają po kroku, continue, restart, zakończeniu sesji lub
zmianie kontekstu; użycie starego daje `staleReference`. Nie ma niejawnego
`GetExpression`, ale enumeracja DTE może wywołać obliczenia po stronie adaptera.

`processes` zwraca procesy debugowane, `threads` wątki bieżącego programu w break
mode, a `selectContext --threadId <id> --frameIndex 0` zmienia bieżący wątek/ramkę.
`stopReason` podaje zaobserwowany powód zatrzymania i dostępne informacje wyjątku.

Kontrakt 2: `breakpoints.currentHits` może być `null`, jeżeli odczyt się nie udał.
Nawet niepusta wartość ma `currentHitsReliability: unverifiedAdapterCounter`;
JS może zwracać zero po trafieniu. `observedHits` liczy zatrzymania zaobserwowane
przez `DTE.AllBreakpointsLastHit` od `observedSinceUtc`, nie trafienia sprzed
uruchomienia proxy ani tracepointy bez zatrzymania. `bindingState` i
`boundLocations` pochodzą z DTE. Historyczne `bindingEvents` zawierają błędy
i rozwiązane lokalizacje z silnika; są dopasowane po pliku/linii, nie tożsamości
breakpointu. Starszy błąd może poprzedzać udane powiązanie. Pełna mapa source map
pozostaje `unavailable`; rozwiązana lokalizacja TS nie jest całym mapowaniem.

### Diagnostyki dokumentu, snapshot i terminale

`documentDiagnostics --path <plik>` filtruje Error List i dołącza stan edytora.
`languageServiceStatus --path <plik>` zwraca content type, GUID usługi językowej,
dirty i wersję snapshotu otwartego dokumentu. Nie otwiera dokumentu za użytkownika.
Wersja aktywnego serwera i powiązanie wersji diagnostyk ze snapshotem są niedostępne.
Content type HTML ani brak błędów nie dowodzą działania Angular Language Service.

`snapshot` zbiera status, projekty, launchCheck, dokumenty, 200 diagnostyk oraz
końcówki Build/Debug po 4000 znaków. Sekcje mają czasy pobrania i błędy; odczyt
całego IDE nie jest atomowy.

`terminals` zwraca jawną dostępność `unsupported` dla istniejących terminali JSPS,
a `terminalOutput` błąd `unsupported`. Zweryfikowane API VS 2026
`ITerminalService.GetTerminalGuidsAsync` wylicza jedynie terminale utworzone przez
danego klienta usługi. Nie udostępnia historii, komendy ani exit code istniejącego
terminala Angulara. Proxy nie zastępuje tych danych wyjściem panelu Output ani
nie przechwytuje procesów uruchomionych inaczej.

Pozostałe ograniczenia: `evaluate` działa w bieżącej ramce, stos DTE nie podaje
kolumny, a zmiana kryteriów breakpointów data/address nadal nie jest obsługiwana.
Dostępność fault, profili i danych silnika zależy od providera. Pełne przeładowanie
solucji, wybór procesu, Test Explorer i kontrola gotowości HTTP nie należą do tej wersji.

## 9. Budowanie i aktualizacja

```powershell
dotnet build .\src\VsCodexProxy.slnx
```

Wynik:

```text
src\VsCodexProxy\bin\Debug\net472\VsCodexProxy.vsix
```

Po zmianie rozszerzenia zainstaluj nowy VSIX i uruchom Visual Studio ponownie.
Aktualna wersja manifestu: `0.6.0`.
