<p align="center">
  <img src="assets/preview-512.png" width="128" alt="Icona di Navigator File Manager">
</p>

<h1 align="center">Navigator File Manager</h1>

<p align="center">
  File manager a schede per Windows 10/11, ispirato a <b>CubicExplorer</b> e con l'aspetto di <b>Esplora file di Windows 11</b>.<br>
  Scritto da zero in C# su .NET 10 e WPF.
</p>

<p align="center">
  <a href="../../releases/latest"><b>Scarica</b></a> ·
  <a href="README.md">English</a> ·
  <a href="CHANGELOG.md">Novità</a>
</p>

---

CubicExplorer (Cubic Reality) era un file manager a schede molto amato, fermo dal 2014.
Navigator ne riprende le idee (schede, segnalibri in cartelle, sessioni, filtri, Quick View, Drop Stack,
pannelli disposti a piacere) in un'app moderna che si presenta come Esplora file di Windows 11.
Non contiene codice di CubicExplorer.

## Scaricare

Scarica `Navigator.exe` dall'[ultima versione](../../releases/latest).
È un unico file (circa 75 MB): non serve installarlo né installare .NET.

> L'exe non è ancora firmato digitalmente, quindi al primo avvio Windows SmartScreen può mostrare
> "PC protetto da Windows". Fai clic su **Ulteriori informazioni → Esegui comunque**.

**Modalità portable:** crea un file vuoto `portable.txt` (oppure una cartella `Data`) accanto a `Navigator.exe`.
Impostazioni, segnalibri e sessioni verranno salvati in `Data\` invece che in `%AppData%\Navigator`.

## Funzioni

- **Schede** nella barra del titolo: chiusura con clic centrale, riordino trascinando, file trascinati su una scheda copiati o spostati nella sua cartella.
- **Aspetto di Windows 11**: tema chiaro/scuro e colore d'accento di Windows, aggiornati al volo; controlli Fluent.
- **Barra indirizzi a breadcrumb** (modificabile con Ctrl+L), indietro/avanti/su, cronologia per scheda.
- **Questo PC** con tutte le unità, lo spazio libero e la barra di occupazione (rossa quando il disco è quasi pieno), aggiornato quando colleghi o scolleghi un'unità.
- **Albero delle cartelle** con Accesso rapido e Questo PC, che segue la cartella aperta.
- **Menu contestuale di Windows**, copia/taglia/incolla compatibili con Esplora file, Cestino e annulla.
- **Viste**: Dettagli, Icone medie e Icone grandi con le miniature di Windows (foto, video, PDF…).
- **Pannelli spostabili**: trascina l'intestazione di un pannello nel lato sinistro, nel lato destro o **sotto la lista dei file**; ridimensiona con i divisori.
- **Segnalibri** a cartelle e file, organizzati in **cartelle virtuali**, con trascinamento; una cartella di segnalibri si apre tutta in schede.
- **Sessioni**: insiemi di schede salvati e ripristinati; le ultime schede si riaprono all'avvio.
- **Filtri**: testo, caratteri jolly e caselle per estensione.
- **Quick View**: anteprima di immagini, testo, audio e video; i file di testo si modificano al volo (Ctrl+S per salvare).
- **Badge di stato Git** sulle icone (serve Git per Windows) e **dimensione delle cartelle** calcolata in background.
- **Ricerca** nella cartella corrente e nelle sottocartelle.
- **Drop Stack**: raccogli file da più cartelle, poi copiali o spostali tutti insieme.
- **Editor di testo** con evidenziazione della sintassi (F4).
- **Lingue**: italiano, inglese, tedesco, francese, spagnolo (di default quella di Windows).

## Scorciatoie da tastiera

| Tasti | Azione |
| --- | --- |
| Ctrl+T / Ctrl+W | Nuova scheda / chiudi scheda |
| Ctrl+Tab / Ctrl+Maiusc+Tab | Scheda successiva / precedente |
| Alt+← / Alt+→ / Alt+↑ / Backspace | Indietro / Avanti / Su |
| Ctrl+L, Alt+D | Modifica l'indirizzo |
| Ctrl+F, F3 | Cerca |
| F5 | Aggiorna |
| F2 | Rinomina |
| Canc / Maiusc+Canc | Elimina nel Cestino / definitivamente |
| Ctrl+C / Ctrl+X / Ctrl+V | Copia / taglia / incolla |
| Ctrl+Maiusc+N | Nuova cartella |
| Ctrl+Maiusc+C | Copia percorso |
| Ctrl+D | Aggiungi la cartella corrente ai segnalibri |
| Ctrl+Maiusc+S | Salva sessione |
| F4 | Apri nell'editor |
| Ctrl+Maiusc+6 / 4 / 3 | Dettagli / Icone medie / Icone grandi |
| Alt+Invio | Proprietà |

## Compilare dal codice

Servono Windows 10/11 a 64 bit e il [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
(Visual Studio 2026 o JetBrains Rider sono facoltativi).

```powershell
dotnet build Navigator.slnx
dotnet test Navigator.slnx
dotnet run --project src/Navigator.App
```

Exe in un solo file:

```powershell
dotnet publish src/Navigator.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

GitHub Actions compila e prova ogni push; un tag come `v0.1.0` pubblica l'exe in una release.

### Traduzioni

I testi stanno in `tools/strings.py`, una riga per testo con tutte le lingue.
Dopo una modifica, `python tools/gen_strings.py` rigenera i file `.resx`.
Un test controlla che ogni testo esista in tutte le lingue con gli stessi segnaposto.

## Limiti noti

- Il pulsante Ingrandisci non mostra i layout di Snap di Windows 11.
- Le viste a icone non sono ancora virtualizzate: cartelle con molte migliaia di file sono più lente che in Dettagli.
- La ricerca scansiona il disco; l'uso dell'indice di Windows Search è previsto.
- La rinomina usa una finestra di dialogo, non la modifica direttamente nella lista.

## Licenza

[MIT](LICENSE). Navigator non è affiliato a Cubic Reality né a Microsoft.
Usa [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet), [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) e Microsoft.Extensions.DependencyInjection (tutti MIT).
