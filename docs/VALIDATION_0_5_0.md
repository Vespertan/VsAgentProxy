# Weryfikacja 0.5.0 — etap 1

Data: 2026-09-20. Środowisko: Windows, Visual Studio Professional 2026
18.10.12210.168, profil testowy `ProxyInspection`. Główne instancje VS pozostały
na zainstalowanej wcześniej wersji 0.4.0. Testy nie wymagały zatrzymania ich sesji.

## Automatycznie

```powershell
dotnet build .\src\VsCodexProxy.slnx
dotnet test .\src\VsCodexProxy.Tests\VsCodexProxy.Tests.csproj
```

21 testów: zakresy Output (pusty bufor, końcówka, koniec i duże indeksy), walidacja
parametrów, brakujące metadane diagnostyk, współrzędne, kategorie pochodzenia,
asynchroniczne publikowanie wpisów, zastępowanie/usuwanie snapshotów i fabryk,
zwalnianie zasobów, filtrowanie przed stronicowaniem, błędy pojedynczego dostawcy,
diagnostyki ładowania, dopasowanie projektów startowych oraz warunki launchCheck.

## W działającym VS 2026

Użyto osobnej solucji testowej z projektem .NET 8, celowym błędem `CS0103`,
folderem rozwiązania i projektem `.esproj` z brakującym SDK. Fixture, pomocniczy
sterownik DTE oraz logi znajdują się w ignorowanym `artifacts/`. Sterownik DTE
służył wyłącznie do przygotowania stanu testowej instancji; odczyty wykonywano
przez Named Pipe i klienta `vscodex` ze wskazanym PID.

| Scenariusz | Wynik |
|---|---|
| `capabilities`, `ping` | Dostępna wersja 0.5.0 i nowe komendy. |
| Pusty Build/Debug przed pierwszą aktywacją | Odtworzono E_FAIL; po poprawce pusty tekst, count/totalChars = 0. |
| Przywrócenie UI po inicjalizacji panelu | Zachowany istniejący wybór panelu, widoczność Output oraz aktywny dokument. |
| Brak wcześniejszego wyboru panelu | VS zachowuje pierwszy zainicjalizowany panel; ten przypadek jest jawnie opisany w protokole. |
| Odczyt po GUID i stronicowanie | Odtworzenie tekstu zawierającego CRLF, polskie litery i emoji w porcjach po 4 jednostki UTF-16. |
| Brak panelu / ujemny offset diagnostyk | Rozróżnialne `outputPaneNotFound` i `invalidParameters`. |
| `projects`, projekt .NET | Poprawna ścieżka, GUID, `loaded`, projekt startowy. |
| Folder rozwiązania | Rozpoznany jako `solutionFolder`, z osobnym GUID-em typu hierarchii. |
| `.esproj` z brakującym SDK | `failed` na podstawie IsFaulted; komunikat FaultMessage i wpis `project-load`. |
| Ten sam projekt wyłączony filtrem `.slnf` | `unloaded`, bez przypisania mu błędu ładowania. |
| Diagnostyka otwartego dokumentu | CS0103, severity error, origin intellisense, plik, linia 1, kolumna 38. |
| Diagnostyka po buildzie w IDE | CS0103, origin build oraz odpowiadający mu tekst w Output. |
| `launchCheck` po nieudanym buildzie | LastBuildInfo = 1 i przyczyna previousBuildFailed. |
| `launchCheck` bez otwartego rozwiązania | Start niedostępny; wskazany brak rozwiązania. |

## Ograniczenia potwierdzone podczas testów

- Pusty panel nie zawsze ma utworzony bufor. `OutputString("")` go nie inicjalizuje.
  Potrzebna jest pierwsza aktywacja i późniejsze odtworzenie stanu UI. E_FAIL nie
  jest samodzielnie interpretowany jako pusty tekst.
- `FaultMessage` może zawierać tylko etykietę, np. `Failed (unloaded)`, zamiast
  szczegółowego błędu resolvera SDK. Proxy zwraca dokładnie dane providera;
  dodatkowe szczegóły mogą znajdować się w panelu Solution.
- Nie wszystkie źródła Error List potwierdzają stabilność. `isStable: false`
  nie unieważnia już zwróconych wpisów, lecz nie pozwala traktować pustego wyniku
  jako potwierdzenia braku błędów.
- Nie przeprowadzono jeszcze pełnej sesji Angular/Edge/Chrome z nową wersją
  w głównej instancji. Profile startowe, reload, identyfikatory operacji i P2
  pozostają kolejnymi etapami planu.
