# VS Codex Proxy — instrukcja dla agenta

To repozytorium zawiera rozszerzenie Visual Studio i klienta CLI, przez który agent
może obserwować oraz sterować debugerem. Rozszerzenie musi być zainstalowane w VS,
a Visual Studio musi być uruchomione.

Pełny protokół i wszystkie parametry: [docs/AGENT_PROTOCOL.md](docs/AGENT_PROTOCOL.md).
Po połączeniu źródłem prawdy jest sam dodatek: wywołaj `capabilities`, a następnie
`documentation --level full`, jeśli potrzebujesz pełnej instrukcji osadzonej w DLL.

## Szybki start

Preferuj globalnie zainstalowaną komendę, która działa z dowolnego katalogu:

```powershell
vscodex instances
vscodex status
vscodex capabilities
```

Jeśli narzędzie globalne nie jest zainstalowane, wykonuj polecenia z katalogu
głównego repozytorium przez klienta projektowego:

```powershell
$client = '.\src\VsCodexProxy.Client'
dotnet run --project $client --no-build -- instances
dotnet run --project $client --no-build -- status
dotnet run --project $client --no-build -- capabilities
dotnet run --project $client --no-build -- documentation --level full
```

Jeśli działa kilka Visual Studio, najpierw pobierz `instances`, a potem jawnie używaj
PID właściwego procesu:

```powershell
vscodex --pid 12345 status
```

Bez `--pid` klient wybiera najnowszą instancję VS.

## Najczęstsza sesja debugowania

```powershell
vscodex --pid 12345 start
vscodex --pid 12345 status
vscodex --pid 12345 stackTrace
vscodex --pid 12345 locals --frameIndex 0 --maxDepth 1
vscodex --pid 12345 arguments --frameIndex 0
vscodex --pid 12345 output Debug 20000
vscodex --pid 12345 stepOver
```

Sterowanie: `start`, `startWithoutDebugging`, `restart`, `continue`, `break`,
`stop`, `stepOver`, `stepInto`, `stepOut`.

Odczyt: `status`, `stackTrace`, `locals`, `arguments`, `evaluate`,
`activeDocument`, `output`, `breakpoints`.

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
  `2` gdy nie znaleziono instancji VS i `3` dla błędu połączenia.

## Budowanie

```powershell
dotnet build .\src\VsCodexProxy.slnx
```

VSIX: `src\VsCodexProxy\bin\Debug\net472\VsCodexProxy.vsix`.
