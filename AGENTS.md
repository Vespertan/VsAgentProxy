# VS Agent Proxy — instrukcja dla agenta

To repozytorium zawiera rozszerzenie Visual Studio i klienta CLI, przez który agent
może obserwować oraz sterować debugerem. Rozszerzenie musi być zainstalowane w VS,
a Visual Studio musi być uruchomione.

Pełny protokół i wszystkie parametry: [docs/AGENT_PROTOCOL.md](docs/AGENT_PROTOCOL.md).
Po połączeniu źródłem prawdy jest sam dodatek: wywołaj `capabilities`, a następnie
`documentation --level full`, jeśli potrzebujesz pełnej instrukcji osadzonej w DLL.

## Szybki start

Preferuj globalnie zainstalowaną komendę, która działa z dowolnego katalogu:

```powershell
vsagent instances
vsagent status
vsagent capabilities
```

Jeśli narzędzie globalne nie jest zainstalowane, wykonuj polecenia z katalogu
głównego repozytorium przez klienta projektowego:

```powershell
$client = '.\src\VsAgentProxy.Client'
dotnet run --project $client --no-build -- instances
dotnet run --project $client --no-build -- status
dotnet run --project $client --no-build -- capabilities
dotnet run --project $client --no-build -- documentation --level full
```

Jeśli działa kilka Visual Studio, najpierw pobierz `instances`, a potem jawnie używaj
PID właściwego procesu:

```powershell
vsagent --pid 12345 status
```

Bez `--pid` klient wybiera najnowszą instancję VS.

## Najczęstsza sesja debugowania

```powershell
vsagent --pid 12345 start
vsagent --pid 12345 status
vsagent --pid 12345 stackTrace
vsagent --pid 12345 locals --frameIndex 0 --maxDepth 1
vsagent --pid 12345 arguments --frameIndex 0
vsagent --pid 12345 output Debug 20000
vsagent --pid 12345 stepOver
```

Sterowanie: `start`, `startWithoutDebugging`, `restart`, `continue`, `break`,
`stop`, `stepOver`, `stepInto`, `stepOut`.

Odczyt: `status`, `stackTrace`, `locals`, `arguments`, `evaluate`,
`activeDocument`, `output`, `breakpoints`, `projects`, `diagnostics`, `launchCheck`.

Gdy aplikacja nie startuje, zacznij od `launchCheck`, `projects` i
`diagnostics --severity error`. `diagnostics` publikuje `isStable` i `readErrors`;
pusty pierwszy odczyt nie dowodzi braku błędów. Listę paneli Output można odczytać
z GUID-ami (`paneDetails`), a panel wskazać przez `output --paneId <guid>`.

Breakpointy: `breakpointAdd`, `breakpointRemove`, `breakpointSetEnabled`,
`breakpointSetCriteria`.

## Zasady dla agenta

- Przed operacją zmieniającą stan sprawdź `status` i właściwy PID.
- `stepOver`, `stepInto`, `stepOut`, `locals` i `arguments` wymagają zatrzymanego debugera.
- Preferuj `locals` i `arguments`; `evaluate` może uruchomić getter lub metodę i
  zmienić stan programu.
- Duży Output pobieraj porcjami przez `--offset` i `--count`.
- Breakpoint dodany przez proxy wskazuj przez stabilne `id`. Dla ręcznych
  breakpointów używaj `index`, pamiętając, że indeks zmienia się po usunięciu.
- Nie zakładaj, że `accepted: true` oznacza osiągnięcie kolejnego breakpointu.
  Po komendzie sterującej ponownie odpytaj `status`.
- Klient zwraca kod `0` dla odpowiedzi `ok: true`, `1` dla błędu protokołu,
  `2` gdy nie znaleziono instancji VS, `3` dla błędu połączenia i `4` dla
  przekroczonego oczekiwania lub niepotwierdzonej operacji. `--wait true` zwraca
  kod `1` także dla operacji zakończonej jako failed/cancelled.
- Od 0.6 używaj `operationStatus` lub `--wait true` dla build/start. Timeout nie
  anuluje VS. Przy ponowieniu mutacji zachowaj `idempotencyKey` i sesję proxy.
- Przed reload odczytaj `documents`; jawny zapis tylko wybranych plików przez
  `saveDocuments --path <plik>`. Nie zapisuj wszystkich zmian automatycznie.
- `selectLaunchProfile` publikuje asynchronicznie: sprawdź `launchProfiles`.
- Zakresy JS/TS: `scopes`, następnie `variables`. `isValid: false` nie wyklucza
  dzieci. Uchwyty wygasają po kroku/continue/zmianie kontekstu.
- `observedHits` to trafienia zaobserwowane przez proxy, `currentHits` to
  niezweryfikowany licznik adaptera. Sprawdzaj `bindingEvents` i ich czas.

## Budowanie

```powershell
dotnet build .\src\VsAgentProxy.slnx
```

VSIX: `src\VsAgentProxy\bin\Debug\net472\VsAgentProxy.vsix`.
