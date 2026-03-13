# PDFReaderV2 — QR Code + Label Scanner

## Overview

A **Windows Forms** application (.NET 9) that scans QR codes from PDF documents and extracts the numeric label printed below each QR code. Each PDF page may contain up to **56 QR code stickers** arranged in a 7×8 grid. Results are displayed in a DevExpress grid in **real-time** (row-by-row) as scanning progresses.

## Tech Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET | 9.0 (Windows) |
| UI Framework | Windows Forms | - |
| Grid & SplitContainer | DevExpress WinForms (`XtraGrid`, `SplitContainerControl`) | 24.2.6 |
| PDF Viewer | DevExpress `PdfViewer` | 24.2.6 |
| PDF Processing | DevExpress `PdfDocumentProcessor` (Document.Processor) | 24.2.6 |
| QR Decode | ZXing.Net + ZXing.Net.Bindings.Windows.Compatibility | 0.16.x |

## Architecture (Clean Architecture — single project)

```
PDFReaderV2/
??? Models/           ? Domain entities, no dependencies
?   ??? ScanResult.cs       record(Page, QRContent, Label)
?   ??? WordInfo.cs          record(Text, PageNumber, Left, Top, Width, Height)
?   ??? DisplayRow.cs        class with No, Page, QRContent, Label (for grid binding)
?   ??? ScanProgress.cs      class with CurrentPage, TotalPages, StepText, EtaText, AvgPerPageText, FoundCount
?
??? Interfaces/       ? Abstractions / contracts
?   ??? IQrScannerService.cs   ScanAsync(pdfPath, onResultFound, onProgressChanged, pauseEvent, cancellationToken)
?   ??? IPdfTextExtractor.cs   ExtractAllWords(pdfPath), GetPageCount(pdfPath)
?   ??? ILabelFinder.cs        FindLabel(pageWords, cellLeft, cellTop, cellRight, cellBottom)
?   ??? IResultExporter.cs     ExportToCsv(filePath, rows)
?
??? Services/         ? Business logic (no UI knowledge)
?   ??? QrScannerService.cs     Main scan orchestrator — loads PDF, extracts words, scans QR page by page
?   ??? PdfTextExtractor.cs     Uses DevExpress PdfDocumentProcessor.NextWord() to extract all words
?   ??? LabelFinder.cs          Finds numeric text near QR code position (with 20% expanded fallback)
?   ??? CsvResultExporter.cs    Writes DisplayRow list to CSV
?
??? Helpers/          ? Pure utility functions
?   ??? TimeFormatter.cs        Formats seconds ? "1h 23m 45s" / "5m 30s" / "12s"
?
??? Form1.cs          ? UI layer (thin) — controls, event handlers, callbacks to services
??? Form1.Designer.cs
??? Program.cs
```

### Dependency Flow

```
Form1 (UI) ? IQrScannerService ? ILabelFinder
           ? IResultExporter
```

- **Form1** only knows interfaces, creates concrete services in constructor
- **QrScannerService** communicates with UI via `Action<ScanResult>` and `Action<ScanProgress>` callbacks
- **No service references WinForms** — all are testable independently

## Key Behaviors

### Scanning Pipeline (QrScannerService.ScanAsync)

1. **Load PDF** — `PdfDocumentProcessor.LoadDocument(path)`
2. **Extract all words** — single pass via `PdfTextExtractor.ExtractWords(processor)` using `NextWord()` iterator
3. **Pre-process words** — compile `Regex(@"^\d+$")` once, group numeric words into `Dictionary<pageNumber, List<WordInfo>>` for O(1) lookup
4. **Per-page scan loop:**
   - Render page to bitmap: `processor.CreateBitmap(page, 2400)` — **2400px** is required for small QR codes (56 per page); 1200px causes missed detections
   - **Full-page decode**: `reader.DecodeMultiple(bitmap)` — returns all QR codes found in one pass
   - For each QR found: compute label position from `ResultPoints`, call `ILabelFinder.FindLabel()` to match nearby numeric text
   - **Callback immediately**: `onResultFound(scanResult)` — UI adds row to grid in real-time
   - **Grid fallback**: if `foundOnPage < 56`, subdivide image into 8×7 cells and decode each cell individually
   - After each page: update `ScanProgress` (ETA = avgScanTime × remainingPages), call `onProgressChanged`

### Label Detection (LabelFinder)

- QR code position (pixel coords from ZXing) is converted to PDF coords via `scaleX/scaleY`
- Search area: QR bounding box expanded 30% left/right/top and 80% bottom (label is below QR)
- Finds numeric words (`^\d+$`) whose center falls within the search area
- **Fallback**: expands search area by additional 20% if no match found

### Real-time Grid Updates

- `BindingList<DisplayRow>` is bound to `GridControl.DataSource` **once** at scan start
- Each QR found ? `Form1.Invoke()` ? `_gridData.Add(row)` ? grid auto-updates via data binding
- Grid scrolls to last row after each add

### Pause / Resume

- `ManualResetEventSlim` shared between UI and scanner service
- Pause: `_pauseEvent.Reset()` ? scanner blocks at `pauseEvent.Wait()`
- Resume: `_pauseEvent.Set()` ? scanner continues
- `CancellationToken` support for full cancellation

### Status Bar

- **Timer** (1 second interval) updates `Elapsed` time every second
- **ETA / Avg** updated only when a page completes (via `onProgressChanged` callback writing `_statusText`)
- Format: `Scanning page 50 / 1786 (2%) | Found: 2800 QR codes | Avg: 9.5s/page | Elapsed: 7m 55s | ETA: 4h 35m 14s`

## UI Layout

```
????????????????????????????????????????????????????????????
? File  View                                               ?  ? MenuStrip
????????????????????????????????????????????????????????????
? [txtPdfPath____________] [Browse] [Process] [Pause]      ?  ? Top bar
????????????????????????????????????????????????????????????
?                         ?  No ? Page ? QR Content ? Label?
?                         ?  1  ?  1   ? https://...? 1234 ?
?    PDF Viewer           ?  2  ?  1   ? https://...? 5678 ?  ? SplitContainer
?    (Panel1 — left)      ?  ...?      ?            ?      ?    Panel2 = GridView
?                         ?  56 ?  1   ? https://...? 9999 ?
?                         ?  57 ?  2   ? https://...? 0001 ?
????????????????????????????????????????????????????????????
? Scanning page 50/1786 (2%) | Found: 2800 | Avg: 9.5s... ?  ? StatusStrip
????????????????????????????????????????????????????????????
```

- **Panel1 (left)**: DevExpress `PdfViewer` — displays loaded PDF, syncs to clicked row's page
- **Panel2 (right)**: DevExpress `GridControl` — real-time scan results
- **Row click**: navigates PDF viewer to that page and highlights label text via `FindText()`

## Button States

| State | Browse | Process | Pause |
|-------|--------|---------|-------|
| App launched (no file) | ? | ? | ? |
| PDF loaded | ? | ? | ? |
| Scanning in progress | ? | ? | ? |
| Scan complete / error | ? | ? | ? |

## Export

- **File ? Save Results** exports grid data to CSV via `IResultExporter.ExportToCsv()`
- Format: `No,Page,"QRContent","Label"`

## Performance Notes

- **2400px render resolution** is required — 1200px misses QR codes when 56 are packed per page
- **Single-thread scanning** is faster than multi-worker for this use case because `PdfDocumentProcessor.NextWord()` must iterate all words from page 1, and each worker would need its own `LoadDocument()` call (duplicating memory)
- **Pre-grouped `Dictionary<page, words>`** avoids re-filtering the full word list on every page
- **Compiled Regex** avoids re-compilation per word per page
- Typical speed: **~9–11 seconds per page** (depends on page complexity and QR count)

## Important Constraints

- `PdfDocumentProcessor` is **not thread-safe** — one instance per thread if parallelizing
- `NextWord()` is a **forward-only iterator** — cannot skip to a specific page
- QR code sticker layout: **7 columns × 8 rows = 56 QR codes per page** with numeric label printed below each QR
- `CreateBitmap(page, largestEdge)` — the parameter is the largest edge in pixels, not DPI
