# Weryfikacja VS Codex Proxy 0.6.0

Data: 2026-09-20. Windows, Visual Studio 2026 Professional 18.10.12210.168,
osobny profil `/RootSuffix ProxyInspection`. Testy wykonano na małym projekcie
.NET 8 i kopii rozwiązania AngularControls z projektami `.esproj`
(Microsoft.VisualStudio.JavaScript.Sdk 1.0.6578810). Kopia używała portu 4307.
Główne sesje użytkownika nie były restartowane ani aktualizowane podczas testów.

## Wyniki automatyczne

```powershell
dotnet build src/VsCodexProxy.slnx --no-restore --verbosity minimal
dotnet test src/VsCodexProxy.Tests/VsCodexProxy.Tests.csproj --no-build --no-restore --verbosity minimal
```

Build bez ostrzeżeń i błędów; 44 przypadki testowe. Testy obejmują granice
stronicowania Output, cykl życia snapshotów Error List, filtrowanie, błędy
providerów, diagnostykę startu, walidację parametrów i ścieżek, rejestr operacji,
timeout bez fikcyjnego anulowania, kursory zdarzeń i deduplikację żądań.
Nie zastępują testów COM i debuggera w uruchomionym IDE.

## Testy w uruchomionym IDE

| Obszar | Obserwowany wynik |
|---|---|
| Dokumenty i zapis | RDT zwracał dirty state plików, projektów i rozwiązania. Niezapisana zmiana w Program.cs blokowała reload (`unsavedDocuments`). Zapis wskazanego pliku kończył się `allSaved: true`. |
| Reload .NET | Załadowany projekt został wyładowany i ponownie załadowany; wynik potwierdzony odczytem stanu. |
| Reload po błędzie SDK | W kopii `.esproj` ustawiono nieistniejący SDK. Reload zwrócił `projectReloadFailed` i rzeczywisty stan `failed`. Po przywróceniu pliku ponowny reload w tej samej instancji przywrócił `loaded`. |
| Startup projects | Ustawienie zweryfikowanego projektu .NET i Angular Demo potwierdzono odczytem IDE. |
| Profile projektu | IDE ujawniło profile .NET First/Second/WSL i JS Edge/Chrome. Zmianę JS na Chrome potwierdził kolejny odczyt `activeProfile`; profil .NET Second pozostał aktywny po restarcie instancji testowej. Publikacja CPS jest asynchroniczna: pierwsza odpowiedź może mieć `accepted: true, applied: false`. |
| Profile rozwiązania | Adapter zgodności VS 2026 odczytał live profile `Demo` i `NowyProfilSolucji`, rozpoznał aktywny `NowyProfilSolucji` po akcjach projektu i celu Chrome oraz przełączył na `Demo`/Edge i z powrotem. Odpowiedź kończyła się `accepted: true, applied: true`. |
| Właściwości projektu | Odczyt obliczonego StartupCommand oraz warunkowego BuildCommand projektu JS. Dane są właściwościami projektu, nie przechwyconą końcową linią procesu. |
| Build | Poprawny build: `succeeded`; błąd kompilacji: `failed`; rebuild przerwany przez cancel: `cancelled`, potwierdzony przez natywne zdarzenia IVsUpdateSolutionEvents. |
| Deduplikacja | Powtórzone żądanie z tym samym idempotencyKey zwróciło ten sam operationId i `replayed: true`, bez drugiego builda. |
| Start .NET | Operacja startu potwierdzona zdarzeniem Run, potem rzeczywiste zatrzymanie na Program.cs:7. |
| Start Angular | Build i start zakończone sukcesem; zatrzymanie w main.ts:3. Stos wskazywał TypeScript, poprawny plik i `userCode: true`. Serwer kopii na porcie 4307 odpowiedział HTTP 200. |
| Breakpointy | DTE zwracało `currentHits: 0` mimo trafienia; niezależne `observedHits: 1` zgadzało się ze zdarzeniem zatrzymania. Odczytano boundLocations oraz zdarzenia silnika: początkowy błąd wiązania, następnie potwierdzone związanie. |
| Zmienne .NET | Locals/Arguments, obiekt anonimowy i tablica były dostępne przez scopes/variables. Po stepOver wcześniejszy uchwyt zwracał `staleReference`. |
| Zakresy JS | Module/Global z `isValid: false` nadal miały dzieci (653/1237 w badanym zatrzymaniu). Strona od offsetu 20 zwróciła 3 rzeczywiste elementy i `hasMore: true`. Po continue stary uchwyt zwracał `staleReference`. |
| Kontekst debuggera | Odczyt rzeczywistych wątków, bieżącego wątku i procesu .NET/JS. |
| Dokument i język | Odczyt identyfikatora usługi językowej, content type CSharp/HTML i wersji bufora. Brak wersji serwera językowego pozostał jawnie niedostępny. |
| Snapshot | Sekcje status/projects/launchCheck/documents/Output/diagnostics odczytane wspólnie z czasami pobrania; `atomic: false`. |
| Timeout CLI | Oczekiwanie na break przy debuggerze w design zakończyło się `waitTimedOut`, kodem wyjścia 4 i `operationCancelled: false`. |

Weryfikację pustych paneli Output, przywracania aktywnego panelu, stronicowania
UTF-16, projektu unloaded/failed oraz diagnostyk CS0103 opisuje również
[raport 0.5.0](VALIDATION_0_5_0.md).

## Ograniczenia i niepotwierdzone kryteria

- **Angular Language Service:** celowy błąd szablonu HTML (nieistniejące pole)
  powodował TS2339 w Build Output i stan operacji `failed`, ale nie pojawił się
  jako diagnostyka dokumentu HTML w Error List badanej instancji.
  Pusta lista nie potwierdza działania ALS. Wersja aktywnego serwera i świeżość
  analizy pozostają nieznane; ten fragment P2 nie ma potwierdzonego odbioru.
- **Source maps:** dostępne są lokalizacje żądane/rozwiązane i zdarzenia wiązania.
  Pełny łańcuch/mapa przekształceń nie jest dostępny. Historia wiązania dopasowana
  po pliku i linii nie stanowi dowodu tożsamości breakpointu.
- **Terminale JSPS:** zbadano publiczne API ITerminalService w zainstalowanym VS.
  Lista dotyczy terminali tworzonych przez klienta tej usługi, nie wszystkich
  istniejących terminali IDE. Odczyt istniejącego terminala JSPS, jego historii
  i kodu wyjścia zwraca `unsupported`; pusty Output nie zastępuje tych danych.
- **Profile rozwiązania:** działają w VS 2026 przez wersjonowany adapter
  wewnętrznego kontraktu IDE. Adapter odmawia działania przy niezgodnej wersji
  `Microsoft.VisualStudio.CommonIDE`; na innych wersjach VS funkcja może zwrócić
  `unsupported` i wymagać osobnego adaptera. Nie edytuje pliku `.suo` ręcznie.
- **Cykl operacji:** korelacja zdarzeń korzysta z pojedynczego okna aktywnej
  komendy, a nie identyfikatora transakcji VS. Ctrl+F5 nie zapewnia potwierdzenia
  gotowości procesu. Timeout oznacza brak potwierdzenia, nie anulowanie.
- **Zakres testów:** sprawdzono VS 2026 na tym komputerze. Nie wykonano pełnej
  macierzy wersji VS, wszystkich adapterów i zewnętrznych providerów projektów.

## Artefakty

- VSIX: `src/VsCodexProxy/bin/Debug/net472/VsCodexProxy.vsix`.
- Pakiet CLI: `artifacts/VsCodexProxy.Client.0.6.0.nupkg`.
- Protokół osadzany w DLL: [AGENT_PROTOCOL.md](AGENT_PROTOCOL.md).
- Stan planu, wraz z funkcjami odłożonymi i ograniczeniami:
  [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md).

Sprawdzono manifest wewnątrz VSIX (0.6.0) oraz zgodność SHA-256 spakowanej DLL
z końcowym buildem. W BuildTools 17.14.2120 wykryto pomijanie aktualizacji
manifestu przy zmianie tekstu bez zmiany długości; target projektu regeneruje
plik pośredni przed detokenizacją. Dokumentacja pobrana z działającej DLL
zawierała wersję 0.6.0 i końcowy kontrakt profili.

Globalny klient `vscodex` zaktualizowano do 0.6.0. Odczyty nim z istniejących
sesji z dodatkiem 0.4.0 działały. Po testach zamknięto profil eksperymentalny;
testowy serwer na porcie 4307 nie nasłuchuje. Sesja użytkownika na porcie 4200
pozostała uruchomiona.

Aktualizacja DLL w profilu testowym nie aktualizuje dodatku w głównych profilach
VS. Instalacja VSIX i ponowne uruchomienie docelowej instancji są osobnym krokiem
wdrożenia, wymagającym zakończenia jej bieżącej sesji debugowania.
