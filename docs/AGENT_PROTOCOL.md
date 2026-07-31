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

Deskryptor zawiera `pid`, `pipe`, `solution` i `startedUtc`. Pole `solution` jest
informacyjne i może być nieaktualne po zmianie rozwiązania. Do jednoznacznego
wyboru instancji używaj PID.

## 2. Klient CLI

`vscodex` jest globalnym narzędziem .NET i może być uruchamiany z dowolnego
katalogu. Instalacja z repozytorium:

```powershell
dotnet pack .\src\VsCodexProxy.Client\VsCodexProxy.Client.csproj -c Release --no-restore
dotnet tool install --global --configfile .\NuGet.Tool.config VsCodexProxy.Client --version 0.4.1
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

`count` jest ograniczany do 1–200000. Bez `offset` zwracane jest ostatnie `count`
znaków. Odpowiedź zawiera `offset`, faktyczne `count`, `totalChars`,
`hasMoreBefore` i `hasMoreAfter`.

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

## 8. Ograniczenia wersji 0.4.0

- Operacje zmiennych dotyczą bieżącego wątku; nie ma jeszcze wyboru wątku.
- `evaluate` działa w bieżącej ramce VS, nie w dowolnym `frameIndex`.
- Stack trace nie zawiera jeszcze kolumny instrukcji (`column` jest `null`).
- Nie ma zdarzeń push; agent odpytuje `status`.
- Deskryptor rozwiązania nie aktualizuje się po zmianie solution.
- Zmiana kryteriów data/address breakpointu nie jest obsługiwana.
- Domyślny timeout całego wywołania klienta wynosi 5 sekund.

## 9. Budowanie i aktualizacja

```powershell
dotnet build .\src\VsCodexProxy.slnx
```

Wynik:

```text
src\VsCodexProxy\bin\Debug\net472\VsCodexProxy.vsix
```

Po zmianie rozszerzenia zainstaluj nowy VSIX i uruchom Visual Studio ponownie.
Aktualna wersja manifestu: `0.4.0`.
