# Plan rozwoju VS Codex Proxy

Data: 2026-09-20. Status: etap 1 w 0.5.0; etapy 2–5 rozpatrzone i dostępny zakres
wdrożony w 0.6.0. Profile całej solucji działają w VS 2026 przez wersjonowany
adapter natywnego kontraktu. Ograniczenia integracji są wymienione poniżej, nie są oznaczone
jako działające funkcje. Dalsza część dokumentu zachowuje pierwotne założenia planu.

## Realizacja 0.6.0

| Zakres | Wynik |
|---|---|
| Dokumenty, zapis, reload | RDT z dirty state, odmowa utraty zmian, unload/reload przez SDK. |
| Startup i profile | Walidacja/readback/rollback startup; profile .NET i JS przez COM oraz eksport CPS; live profile całej solucji przez adapter VS 2026, z weryfikacją akcji i celów. |
| Właściwości i konfiguracje | Wartości obliczone przez IDE, m.in. warunkowy BuildCommand i katalog roboczy. |
| Build/start/events | Rejestr operacji, zdarzenia build/debugger/silnika, anulowanie, deduplikacja, waitForState; potwierdzone sukces, błąd, anulowanie i breakpoint. |
| JS/TS | Trafienie main.ts:3, bound oraz historyczne błędy/rozwiązane lokalizacje, observedHits, rozwijanie Module/Global, paginacja, staleReference po continue. |
| Dokument i język | documentDiagnostics oraz snapshot/content type/GUID usługi; brak dowodu wersji aktywnego serwera ALS jest jawny. |
| Terminale | Zbadane publiczne API VS 2026. Wylicza tylko terminale klienta usługi, nie istniejące JSPS; output/history/exit code zwracają unsupported. |
| Ergonomia | Snapshot IDE, threads/processes/selectContext, stopReason, enumeracja Named Pipes i jawna diagnostyka ping/sesji. |

Nieudostępnione przez obecną integrację: pełna mapa source map, wersja aktywnego
ALS, historyczny Output istniejących terminali. Adapter `.slnLaunch` jest związany
z konkretnym wydaniem VS 2026 i zwraca `unsupported` przy niezgodnym kontrakcie.
W profilu testowym błąd szablonu Angular był w Build Output, ale nie jako rekord
HTML Error List; P2 w zakresie Angular Language Service nie ma potwierdzenia odbioru.
Te ograniczenia wymagają dodatkowego kontraktu providera, a nie uznania pustych
danych za poprawne działanie. Funkcje oznaczone pierwotnie „później” lub opcjonalne
(solutionReload, Test Explorer, modules/loadedScripts, HTTP readiness) pozostają
osobnym zakresem. Szczegóły: [weryfikacja 0.6.0](VALIDATION_0_6_0.md).

Historia realizacji: w wersji 0.5.0 wdrożony został etap 1: Output, projects,
diagnostics i launchCheck, wraz z niezbędnym wydzieleniem usług i błędami
strukturalnymi. Test VS 2026 wykazał, że
niezainicjalizowany pusty panel Output wymaga pierwszej aktywacji; implementacja
przywraca później poprzedni panel, widoczność i aktywne okno, zamiast uznawać E_FAIL
za dowód pustego bufora.

Punktem wyjścia jest `C:/Projekty/Vespertan/AngularControls/docs/vscodex-missing-functions.md`, kod proxy i dokumentacja SDK Microsoftu. Plan obejmuje P1, P2 oraz powiązane usprawnienia. Etap 1 zweryfikowano później w osobnej instancji VS 2026; wyniki i ograniczenia opisuje [raport weryfikacji 0.5.0](VALIDATION_0_5_0.md).

## 1. Cel i kolejność

Agent powinien umieć ustalić, dlaczego aplikacja nie startuje, poprawić wybór projektu/profilu, odświeżyć projekt i potwierdzić wynik operacji. Następnie powinien móc wiarygodnie diagnozować działającą aplikację JS/TS.

| Etap | Zakres | Efekt | Złożoność / niepewność |
|---|---|---|---|
| 0 | Kontrakt odpowiedzi, wydzielenie usług, krótkie próby integracji | Wspólne podstawy i rozpoznanie ograniczeń SDK | Średnia |
| 1 | Output, projekty, diagnostyki, kontrola gotowości startu | Wyjaśnienie większości nieudanych startów | Średnia |
| 2 | Bezpieczne przeładowanie, projekty i profile startowe | Naprawa konfiguracji z poziomu agenta | Średnia–wysoka; szczególnie `.esproj` |
| 3 | Build/start z identyfikatorem operacji i zdarzeniami | Potwierdzenie wyniku zamiast samego przyjęcia komendy | Wysoka |
| 4 | Breakpointy, zakresy JS, diagnostyki HTML/usług językowych | Wiarygodna inspekcja Angulara w IDE | Wysoka; zależna od adaptera |
| 5 | Terminale oraz dodatkowa ergonomia | Uzupełnienie obserwacji procesów i ich wyjścia | Wysoka; wymaga potwierdzenia dostępnego API |

Krótki prototyp dostępu do profili `.esproj`, zakresów JS i terminali powinien powstać już w etapie 0. Wynik decyduje o szczegółowym zakresie późniejszych etapów; nie blokuje poprawek Output i podstawowej diagnostyki.

## 2. Ustalenia z obecnego kodu

- `ProxyServer.cs` łączy transport, komendy, odczyt IDE i serializację. Cały dispatch przechodzi na wątek UI. Rozszerzanie go o długie operacje wymaga wydzielenia usług i krótkich wejść na UI.
- `GetOutput` korzysta bezpośrednio z `TextDocument`, następnie odczytuje cały panel i dopiero wycina fragment. Nie rozróżnia pustego bufora od błędu dostępu. Przyczyna konkretnego E_FAIL wymaga reprodukcji.
- `currentHits` używa `SafeRead(..., 0)`: wyjątek wygląda jak prawdziwe zero. To potwierdzona wada raportowania; nie dowodzi, że zgłoszone zero w sesji JS powstało właśnie w ten sposób. Adapter może również sam zwracać zero.
- `locals` opiera się na DTE `Locals` i `DataMembers`. Przy `maxDepth: 0` nie zwraca informacji, czy element można rozwinąć; nie ma odrębnych uchwytów zakresów i zmiennych.
- Start sprawdza tylko `Command.IsAvailable`, a odpowiedź oznacza wysłanie polecenia. Nie ma rejestru operacji ani korelacji ze zdarzeniami build/debug.
- CLI ma wspólny pięciosekundowy limit połączenia i odpowiedzi. Parser obsługuje skalary, ale nie tablice potrzebne do `setStartupProjects(paths)`.
- W AngularControls istnieje `.slnLaunch` z profilem `Demo`. Projekt demo ma warunkowy `BuildCommand`, własny `StartupCommand` oraz wrapper `scripts/npm.ps1`. Odczyt samego `package.json` nie odtworzy całej konfiguracji uruchomienia.

## 3. Etap 0 — kontrakt i infrastruktura

Wydzielić stopniowo `ProjectService`, `LaunchService`, `DiagnosticsService`, `OutputService`, `DebugInspectionService` oraz `OperationRegistry`; pozostawić transport w `ProxyServer`. Pakiet dostarcza wymagane usługi VS i zarządza subskrypcjami zdarzeń.

Zachować istniejące nazwy komend i pola odpowiedzi. Dodawać `errorCode`, `details`, `hresult`, `availability` oraz `unavailableReason`. Rozróżniać brak danych, brak obsługi, błąd odczytu i rzeczywiste wartości `0`/`false`. Ewentualna zmiana typu dotychczasowego pola wymaga jawnej wersji kontraktu i opisu migracji; na początek dodać obok niego pole określające wiarygodność.

Rozbudować `capabilities` o możliwości konkretnego projektu/adaptera i warunki wywołania. Metoda istniejąca w proxy może być niedostępna dla bieżącego projektu. API ma zwracać tę różnicę wprost.

W CLI dodać jawny sposób podawania tablic, np. powtarzalne `--project`, rozdzielić timeout połączenia od oczekiwania na wynik oraz walidować parametry. Długie operacje zwracają szybko identyfikator, a ich oczekiwanie odbywa się poza wątkiem UI. Pierwsza wersja może używać krótkiego pollingu klienta, bez blokującego serwer long pollingu.

**Odbiór:** stare wywołania nadal działają; błędne parametry, niedostępna usługa i timeout mają różne wyniki. Dokumentacja osadzona w DLL, katalog API i pomoc CLI są zgodne.

## 4. Etap 1 — odczyt i wyjaśnienie problemu (P1)

| Proponowane API | Dane i zachowanie |
|---|---|
| `output` | Zachować listę nazw; dodać metadane paneli z GUID i selektor `paneId`. Pusty bufor zwraca pusty tekst. Błąd dostępu zawiera etap odczytu i HRESULT. |
| `projects` | ID projektu w obrębie rozwiązania, nazwa, pełna ścieżka, typ/capabilities, `loadState`, projekt startowy, ostatni dostępny błąd ładowania ze źródłem i czasem. |
| `diagnostics` | Severity, źródło/provider, origin: build/IntelliSense/project-load/unknown, projekt, dokument, linia/kolumna, kod, komunikat, filtry i stronicowanie. |
| `launchCheck` | Bieżąca dostępność startu, wybrane projekty/profile, stan ładowania i budowania oraz wykryte przeszkody. Każda przyczyna ma dowód i oznaczenie, czy jest potwierdzona, czy tylko podejrzewana. |

Dla Output sprawdzić alternatywny dostęp do bufora przez usługi VS, bez aktywowania panelu i zmiany fokusu. Nie zamieniać każdego `E_FAIL` na pusty tekst. Odczyt zakresu powinien ograniczać pracę do potrzebnych danych, jeżeli interfejs bufora to umożliwia.

Projekty wyliczać przez hierarchie rozwiązania, uwzględniając unloaded, zagnieżdżenie i foldery rozwiązania. `failed` wymaga dowodu błędu ładowania; sam brak załadowanego projektu go nie potwierdza. Dodać `loading` i `unknown`. Błędu sprzed uruchomienia proxy może nie dać się odtworzyć.

Dla diagnostyk sprawdzić źródła Error List i ich snapshoty przez Table Manager; nie uzależniać wyniku od aktualnych filtrów okna. Niektóre źródła nie dostarczą kodu, pochodzenia lub pełnej lokalizacji — zachować brak danych. Interfejs zarządza wieloma źródłami i udostępnia ich listę oraz zdarzenia zmian. [Dokumentacja ITableManager](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.tablemanager.itablemanager?view=visualstudiosdk-2022).

`launchCheck` nie powinien obiecywać pełnego wyjaśnienia każdego `IsAvailable == false`: VS może nie udostępnić powodu. W takim przypadku wynik zawiera stan IDE i `reasonUnknown`, bez zgadywania.

**Odbiór:** puste Build/Debug, duży panel, brak panelu, projekt unloaded, faktyczny błąd SDK oraz brak projektu startowego dają rozróżnialne odpowiedzi.

## 5. Etap 2 — bezpieczne odświeżenie i wybór uruchomienia (P1)

- `documents`: lista otwartych dokumentów z flagą niezapisanych zmian i przypisaniem do projektu, o ile jest dostępne.
- `projectReload(path)`: walidacja projektu i stanu IDE; kontrola niezapisanych dokumentów, także współdzielonych; odmowa ryzyka utraty zmian z listą blokujących dokumentów. Ponowne sprawdzenie bezpośrednio przed operacją.
- `startupProjects` / `setStartupProjects(paths)`: pełna walidacja wszystkich ścieżek przed zmianą i odczyt wyniku po zmianie. Przy częściowym błędzie zwrócić stan faktyczny oraz wynik ewentualnego przywrócenia poprzednich ustawień.
- `launchProfiles(project)` / `selectLaunchProfile(project, name)`: nazwa, aktywność, typ celu, URL, katalog roboczy, skonfigurowana i rozwiązana komenda, pochodzenie danych. Efektywne wartości pozostają nieznane, jeśli nie udostępnia ich system projektu.
- `solutionLaunchProfiles` / `selectSolutionLaunchProfile(name, scope?)`: live obsługa profili całej solucji przez wersjonowany adapter VS 2026; weryfikuje akcje, kolejność i cele debugowania oraz nie utożsamia profilu z profilem pojedynczego projektu.

`IVsSolution4.ReloadProject` jest punktem wyjścia dla projektu unloaded. Dla już załadowanego projektu zwraca `S_FALSE`, więc nie zapewnia ogólnego „odświeżenia” — potrzebny jest osobny, kontrolowany przebieg unload/load. `solutionReload` pozostawić jako późniejszą opcję z kontrolą wszystkich niezapisanych dokumentów. [Dokumentacja ReloadProject](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.ivssolution4.reloadproject?view=visualstudiosdk-2022).

Podstawowy wybór projektu można oprzeć na `SolutionBuild.StartupProjects`; współdziałanie z profilami wieloprojektowymi należy zweryfikować. [Dokumentacja StartupProjects](https://learn.microsoft.com/en-us/dotnet/api/envdte.solutionbuild.startupprojects?view=visualstudiosdk-2022).

Integrację profili rozdzielić według systemu projektu. CPS/.NET udostępnia `ILaunchSettingsProvider` i aktywny profil, również profile istniejące tylko w pamięci. Nie zakładać takiej samej obsługi `.esproj`. [Opis profili w .NET Project System](https://github.com/dotnet/project-system/blob/main/docs/launch-profiles.md). JSPS definiuje m.in. `BuildCommand` i `StartupCommand`; ich wartości trzeba zestawić z konfiguracją debuggera i właściwościami obliczonymi przez IDE. [Dokumentacja JSPS](https://learn.microsoft.com/en-us/visualstudio/javascript/javascript-project-system-msbuild-reference?view=visualstudio).

**Odbiór:** naprawa błędnego SDK i reload bez restartu VS; odmowa przy niezapisanym dokumencie; potwierdzona zmiana projektu oraz profilu Demo/Edge/Chrome, o ile dany provider je udostępnia. Sam odczyt pliku konfiguracyjnego nie potwierdza aktywnego wyboru w IDE.

## 6. Etap 3 — wynik build/start i historia zdarzeń (P1)

Wprowadzić `build`, następnie `rebuild`, `clean`, `cancelBuild`, oraz `operationStatus(id)`. `start` zachowuje `accepted`, a dodatkowo zwraca `operationId`. Rejestr ma ograniczony rozmiar i czas przechowywania, identyfikator sesji oraz czasy rozpoczęcia/zakończenia.

Oddzielić `state` operacji (`queued`, `running`, `succeeded`, `failed`, `cancelled`, `unknown`) od fazy startu (`building`, `launching`, `debuggerAttached`). Sukces startu z debugerem oznacza potwierdzony start sesji, także natychmiastowe zatrzymanie na breakpoincie. Nie oznacza gotowości serwera HTTP. Start bez debugowania wymaga osobnego potwierdzenia procesu od providera; jeśli go brak, wynik pozostaje niepotwierdzony.

Build śledzić przez zdarzenia VS; `IVsUpdateSolutionEvents` ma osobne powiadomienia rozpoczęcia, anulowania i zakończenia. [Dokumentacja zdarzeń build](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.interop.ivsupdatesolutionevents?view=visualstudiosdk-2022). Błędy debug adaptera pobierać ze zdarzeń diagnostycznych, jeśli są dostępne; log Output może być dodatkowym dowodem, z oznaczeniem źródła i pewności korelacji.

Dodać `events(afterSequence, limit)` z ograniczonym buforem, znacznikiem utraconej historii i identyfikatorem sesji. Rejestrować build, load, start, stop, zmianę profilu, błąd i trafienie breakpointu. Odróżniać operacje agenta od działań użytkownika; nie przypisywać dowolnego zdarzenia build do ostatniego `start`. Początkowo odrzucać kolidujące operacje, zamiast niejawnie je nakładać.

Timeout klienta oznacza zakończenie oczekiwania, nie anulowanie pracy VS. Dodać klucz deduplikacji dla mutacji, aby ponowienie po utracie odpowiedzi nie uruchomiło aplikacji drugi raz. Anulowanie udostępnić tylko tam, gdzie backend rzeczywiście je obsługuje.

**Odbiór:** udany build, build z błędem, anulowanie, niedostępny start, start odrzucony przez adapter, aplikacja kończąca się natychmiast, zmiana trybu przez użytkownika i utrata połączenia są raportowane poprawnie.

## 7. Etap 4 — JS/TS i diagnostyki szablonów (P2)

`breakpoints` rozszerzyć o `bindingState`, powód błędu, lokalizacje powiązane przez silnik, identyfikator silnika oraz wiarygodność licznika. Rozdzielić licznik silnika od `observedHits`, czyli trafień obserwowanych przez proxy od początku subskrypcji. Brak wiarygodnego licznika nie może wyglądać jak potwierdzone zero.

SDK opisuje zdarzenia bound/error i enumerację związanych lokalizacji. Potrzebny jest prototyp dostępu i korelacji tych danych z breakpointami DTE dla faktycznego adaptera JS. Pełne mapowanie source map może nie być udostępnione: raportować osobno żądaną pozycję, pozycję resolved oraz dostępność danych mapowania. [Dokumentacja wiązania breakpointów](https://learn.microsoft.com/en-us/visualstudio/extensibility/debugger/binding-breakpoints?view=visualstudio).

Dodać `scopes(frameIndex)` i `variables(reference, offset, count)`, z `hasChildren` niezależnym od `isValidValue`. Węzeł `Module`/`Global` może być kontenerem bez wartości skalarnej. Najpierw sprawdzić rozwijanie przez DTE; jeśli niewystarczające, zbadać interfejsy silnika. `IDebugProperty2.EnumChildren` udostępnia enumerację dzieci, lecz sama obecność API nie gwarantuje obsługi przez adapter JS. [Dokumentacja EnumChildren](https://learn.microsoft.com/en-us/visualstudio/extensibility/debugger/reference/idebugproperty2-enumchildren?view=visualstudio).

Uchwyty zmiennych powiązać z sesją i numerem zatrzymania; po continue/step/restart stare uchwyty zwracają `staleReference`. Nie wykonywać niejawnego `evaluate`. Rozwijanie wartości także może wywołać obliczenia silnika/gettery — stosować dostępne flagi bez efektów ubocznych i jawnie raportować ograniczenia.

`documentDiagnostics(path)` powinno korzystać ze wspólnej usługi diagnostyk. Dodać `languageServiceStatus(path)` z providerem, stanem i wersją aktywnej usługi, jeśli jest dostępna. Rozdzielić wersję zainstalowanego rozszerzenia, wersję serwera językowego i potwierdzenie obsługi dokumentu. Brak diagnostyk nie dowodzi, że Angular Language Service działa. Snapshot powinien wskazywać wersję dokumentu lub brak możliwości ustalenia aktualności.

**Odbiór:** trafienie w `main.ts:3`, poprawny stos TS, rozwinięcie zakresu JS, unieważnienie uchwytu po continue; kontrolowany błąd szablonu HTML widoczny w IDE, a następnie usunięty po poprawce.

## 8. Etap 5 — terminale JavaScript (P2)

Proponowane `terminals` i `terminalOutput(id, cursor, count)` zwracają nazwę, komendę, cwd, PID, stan, kod wyjścia i zakres dostępnego wyjścia — wyłącznie dla danych udostępnionych przez integrację.

Najpierw ustalić rodzaj terminala uruchamianego przez JSPS i dostępne publiczne API VS 2026. Nie potwierdzono tu uniwersalnego publicznego API do odczytu dowolnego terminala. PID procesu powłoki nie jest automatycznie PID-em Angular CLI, a kod wyjścia powłoki nie jest kodem zakończonego polecenia.

Jeżeli terminal nie udostępnia historii, zwracać `unsupported` i zakres faktycznie obserwowanego wyjścia. Alternatywą jest osobno uzgodnione przechwytywanie stdout/stderr procesu uruchomionego przez proxy; nie daje ono dostępu do wcześniejszych terminali i zmienia sposób startu aplikacji. Nie jest częścią podstawowej obsługi `start`.

**Odbiór:** rozpoznany proces Angulara, stronicowane wyjście bez duplikacji, wykryte zakończenie lub jawnie nieznany exit code; poprawna obsługa resetu i utraty części historii.

## 9. Dodatkowe funkcje warte realizacji

| Priorytet | Funkcja | Korzyść |
|---|---|---|
| Wysoki, etap 1 | `snapshot` | Zbiera status, projekty, wybór startu, diagnostyki i końcówki Output. Zawiera czasy/wersje poszczególnych części; nie udaje atomowego odczytu całego IDE. |
| Wysoki, etap 1–2 | `projectProperties` oraz `configurations` | Wyjaśnia różnice Debug/Release, warunkowe komendy, SDK, target framework, platformę i build working directory. Odczyt wybranej listy właściwości obliczonych przez IDE. |
| Wysoki, etap 2 | `saveDocuments(paths)` | Umożliwia jawne zapisanie wskazanych dokumentów przed reload; bez automatycznego zapisywania wszystkich zmian. |
| Wysoki, etap 3 | `waitForState` w CLI | Oczekiwanie na build/break/run z limitem czasu i ostatnim stanem; korzysta z krótkich odczytów. |
| Wysoki, etap 4 | `stopReason` i informacje o wyjątku | Wyjaśnia, czy sesję zatrzymał breakpoint, wyjątek, krok czy użytkownik. |
| Średni, etap 4 | `threads`, `processes`, jawny wybór kontekstu | Umożliwia analizę aplikacji wielowątkowych i kilku debugowanych procesów. |
| Średni, po etapie 3 | `applicationReadiness` | Osobno potwierdza gotowość wskazanego lokalnego URL/portu; uruchomiona sesja debug nie oznacza gotowego Angular dev servera. |
| Średni, po etapie 4 | `modules` / `loadedScripts` | Pomaga ustalić, czy debugger załadował właściwy skrypt/moduł i czy są dane symboli lub mapowania. |
| Później | Uruchamianie testów i wyniki Test Explorer | Osobny adapter z identyfikatorami operacji; przydatny po ustabilizowaniu build i diagnostyk. |

Wykrywanie instancji opierać na enumeracji potoków `VsCodexProxy-{PID}`. `ping` pozostawić jako jawną diagnostykę wersji i sesji, bez wysyłania go przed każdą operacją.

## 10. Weryfikacja i podział dostaw

Proponowane osobne zmiany: (1) Output i jawne błędy odczytu, (2) kontrakt/usługi/CLI, (3) projekty i diagnostyki, (4) documents/reload, (5) profile/startup, (6) operacje/events/build, (7) inspekcja JS i language services, (8) terminale. Wstępne prototypy integracyjne poprzedzają zobowiązanie do pełnej obsługi trudnych providerów.

Testy kontraktu obejmują parametry i tablice CLI, paginację, puste dane, brak obsługi, timeout, deduplikację i wygasanie uchwytów. Testy rejestru operacji sprawdzają sekwencje zdarzeń oraz kolizje z działaniami użytkownika. Testy integracyjne wymagają osobnej instancji VS z nowym VSIX: minimalny projekt .NET jako punkt porównania oraz AngularControls `.esproj`, w Debug i Release, z Edge/Chrome według dostępności.

Warunkiem odbioru P1 jest pełny scenariusz: diagnoza błędu ładowania → bezpieczny reload → potwierdzenie projektu/profilu → build/start → sprawdzony wynik lub konkretny błąd z dowodem. Warunkiem odbioru P2 jest rzeczywista inspekcja sesji JS i diagnostyk HTML; sama kompilacja CLI nie wystarcza.

Pierwszy użyteczny zakres do wdrożenia: **Output + projects + diagnostics + launchCheck**. Pozostałe funkcje korzystają z tych samych danych, a ten zestaw od razu pomaga wyjaśniać zgłoszone nieudane uruchomienia.
