# PDFReaderV2 – QR Code + Label Scanner

# PDFReaderV2 – QR Code + Label Scanner

## Overview

A **Windows Forms** application (.NET 9) that scans QR codes from PDF documents and extracts the numeric label printed below each QR code. Each PDF page may contain up to **56 QR code stickers** arranged in a 7×8 grid. Results are displayed in a DevExpress grid in **real-time** (row-by-row) as scanning progresses.

## Tech Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET | 9.0 (Windows) |
| UI Framework | Windows Forms | — |
| Grid & SplitContainer | DevExpress WinForms (`XtraGrid`, `SplitContainerControl`) | 24.2.6 |
| PDF Viewer | DevExpress `PdfViewer` | 24.2.6 |
| PDF Processing | DevExpress `PdfDocumentProcessor` (Document.Processor) | 24.2.6 |
| QR Decode (primary) | **ZXingCpp** (native C++ with .NET wrapper) | 0.5.1 |
| QR Decode (unused/legacy) | ZXing.Net + ZXing.Net.Bindings.Windows.Compatibility | 0.16.x |

## Architecture (Clean Architecture – single project)

```
PDFReaderV2/
??? Models/           — Domain entities, no dependencies
?   ??? ScanResult.cs       record(Page, QRContent, Label)
?   ??? WordInfo.cs          record(Text, PageNumber, Left, Top, Width, Height)
?   ??? DisplayRow.cs        class with No, Page, QRContent, Label (for grid binding)
?   ??? ScanProgress.cs      class with CurrentPage, TotalPages, StepText, EtaText, AvgPerPageText, FoundCount
?
??? Interfaces/       — Abstractions / contracts
?   ??? IQrScannerService.cs   ScanAsync(pdfPath, onResultFound, onProgressChanged, pauseEvent, cancellationToken)
?   ??? IPdfTextExtractor.cs   ExtractAllWords(pdfPath), GetPageCount(pdfPath)
?   ??? ILabelFinder.cs        FindLabel(pageWords, cellLeft, cellTop, cellRight, cellBottom)
?   ??? IResultExporter.cs     ExportToCsv(filePath, rows)
?
??? Services/         — Business logic (no UI knowledge)
?   ??? QrScannerService.cs     Main scan orchestrator – loads PDF, extracts words, scans QR page by page
?   ??? PdfTextExtractor.cs     Uses DevExpress PdfDocumentProcessor.NextWord() to extract all words (with Y-axis conversion)
?   ??? LabelFinder.cs          Finds numeric label below QR code (bottom-row selection + 20% expanded fallback)
?   ??? CsvResultExporter.cs    Writes DisplayRow list to CSV
?
??? Helpers/          — Pure utility functions
?   ??? TimeFormatter.cs        Formats seconds ? "1h 23m 45s" / "5m 30s" / "12s"
?
??? Form1.cs          — UI layer (thin) – controls, event handlers, callbacks to services
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
- **No service references WinForms** – all are testable independently

## Key Behaviors

### Scanning Pipeline (QrScannerService.ScanAsync)

1. **Load PDF** – `PdfDocumentProcessor.LoadDocument(path)`
2. **Extract all words** – single pass via `PdfTextExtractor.ExtractWords(processor)` using `NextWord()` iterator. **Y-axis is converted** from PDF page coordinates (origin bottom-left, Y increases upward) to top-down coordinates (origin top-left, Y increases downward) so that `WordInfo.Top` matches bitmap pixel coordinates directly (see [Coordinate System Conversion](#coordinate-system-conversion))
3. **Pre-process words** – compile `Regex(@"^\d+$")` once, group numeric words into `Dictionary<pageNumber, List<WordInfo>>` for O(1) lookup
4. **Per-page scan loop:**
   - Render page to bitmap: `processor.CreateBitmap(page, 2400)` – **2400px** is required for small QR codes (56 per page); 1200px causes missed detections
   - **Full-page decode** using **ZXingCpp native library**:
     - Convert bitmap to `ImageView` (BGRA format, locked pixel data via `Marshal.Copy`)
     - `BarcodeReader.From(imageView)` with `MaxNumberOfSymbols = 64`, `TryInvert = true`
     - **10-50x faster** than ZXing.Net due to native C++ implementation
     - Uses `barcode.Position` (TopLeft, TopRight, BottomLeft, BottomRight) for accurate QR bounding box ? precise label matching
   - **Validation** before accepting each decoded barcode:
     - `barcode.Text?.Trim()` – normalize whitespace
     - `barcode.IsValid` – reject low-confidence decodes
     - `NumericOnly` filter (`^\d+$`) – reject false positives where label numbers are mistakenly decoded as QR codes (see [False Positive Prevention](#false-positive-prevention))
   - Add found QR codes to `ConcurrentDictionary` for deduplication
   - `foundOnPage` counts only validated barcodes (not raw array length) to accurately determine if grid fallback is needed
   - **Parallel grid fallback** (if `foundOnPage < 56`):
     - Pre-cut bitmap into **overlapping grid cells** (56 standard + ~56 offset by half cell)
     - `Parallel.ForEach` with `MaxDegreeOfParallelism = Environment.ProcessorCount`
     - Each cell decoded independently via ZXingCpp with same validation rules
     - Catches QR codes missed by full-page scan (edge cases, overlapping cells)
   - **Callback immediately**: `onResultFound(scanResult)` – UI adds row to grid in real-time
   - After each page: update `ScanProgress` (ETA = avgScanTime × remainingPages), call `onProgressChanged`

### False Positive Prevention

ZXingCpp may occasionally decode **label text** (numeric digits printed below QR codes) as barcode content, especially during the grid fallback phase when small bitmap cells contain only a label number. This produces false positives — e.g. `"7834"` instead of a real URL.

Three-layer validation prevents this:

| Layer | Check | Purpose |
|-------|-------|---------|
| 1 | `barcode.IsValid` | Reject low-confidence ZXingCpp decodes |
| 2 | `.Trim()` + empty check | Normalize and reject blank content |
| 3 | `NumericOnly.IsMatch(text)` | **Reject digit-only content** — real QR codes are URLs, labels are digit-only |

The `NumericOnly` regex (`^\d+$`) is compiled once as a `static readonly` field and shared across all pages/threads.

### Label Detection

Label detection uses two code paths depending on whether the QR code was found via full-page decode or grid fallback:

#### Full-page path (`FindLabelFromBarcode`)

- QR code position (pixel coords from ZXingCpp `barcode.Position`) is converted to PDF coords via `scaleX/scaleY`
- QR bounding box calculated from min/max of TopLeft, TopRight, BottomLeft, BottomRight points
- **Search area is label-only**: starts at the **bottom edge** of the QR code (`maxY`) and extends **80% of QR height downward** — does **not** expand above the QR code, preventing accidental capture of a neighboring sticker's label
- Left/right expanded by 30% of QR width to account for slight misalignment

#### Grid fallback path

- Uses the grid cell boundaries (`CellInfo.PdfLeft/Top/Right/Bottom`) as the search area
- Cell coordinates are bitmap pixel positions divided by `scaleX/scaleY`

#### Label matching (`LabelFinder.FindLabel`)

- Finds numeric words (`^\d+$`) whose center falls within the search area
- When multiple candidates are found, selects only the **bottom-most row** (`GetBottomRow`) — the word(s) with the highest `Top` value (furthest down on page), since the label is always printed below the QR code
- Multiple words on the same row (within 50% of word height tolerance) are joined together
- **Fallback**: expands search area by additional 20% in all directions if no match found

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
?    (Panel1 – left)      ?  ...?      ?            ?      ?    Panel2 = GridView
?                         ?  56 ?  1   ? https://...? 9999 ?
?                         ?  57 ?  2   ? https://...? 0001 ?
????????????????????????????????????????????????????????????
? Scanning page 50/1786 (2%) | Found: 2800 | Avg: 9.5s... ?  ? StatusStrip
????????????????????????????????????????????????????????????
```

- **Panel1 (left)**: DevExpress `PdfViewer` – displays loaded PDF, syncs to clicked row's page
- **Panel2 (right)**: DevExpress `GridControl` – real-time scan results
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

- **ZXingCpp native library** provides 10-50x performance improvement over ZXing.Net (C++ vs. managed code)
- **2400px render resolution** is required – 1200px misses QR codes when 56 are packed per page
- **Single-thread word extraction** is faster than multi-worker because `PdfDocumentProcessor.NextWord()` must iterate all words from page 1, and each worker would need its own `LoadDocument()` call (duplicating memory)
- **Pre-grouped `Dictionary<page, words>`** avoids re-filtering the full word list on every page
- **Compiled Regex** (`NumericOnly` static field) avoids re-compilation per word per page, shared across threads
- **Parallel grid fallback** uses all CPU cores (`Environment.ProcessorCount`) for cell-based decoding when full-page scan misses QR codes
- Typical speed: **~0.5–1s per page** (ZXingCpp), previously ~9–11s with ZXing.Net

## Coordinate System Conversion

DevExpress `PdfOrientedRectangle` uses the **PDF page coordinate system** where the origin is at the **bottom-left** corner and **Y increases upward**. Bitmap/image coordinates have the origin at the **top-left** corner with **Y increasing downward**.

| System | Origin | Y direction | Word at top of page | Word at bottom of page |
|--------|--------|-------------|--------------------|-----------------------|
| PDF page coords (`rect.Top`) | Bottom-left | ? Upward | High value (e.g. 800) | Low value (e.g. 10) |
| Bitmap / top-down (`WordInfo.Top`) | Top-left | ? Downward | Low value (e.g. 10) | High value (e.g. 800) |

`PdfTextExtractor.ExtractWords()` converts at extraction time:

```csharp
double topDown = pageHeight - rect.Top;
```

This ensures `WordInfo.Top` values can be compared directly with bitmap pixel positions (divided by `scaleY`) without further conversion anywhere else in the codebase.

## Important Constraints

- `PdfDocumentProcessor` is **not thread-safe** – one instance per thread if parallelizing
- `NextWord()` is a **forward-only iterator** – cannot skip to a specific page
- QR code sticker layout: **7 columns × 8 rows = 56 QR codes per page** with numeric label printed below each QR
- `CreateBitmap(page, largestEdge)` – the parameter is the largest edge in pixels, not DPI
- **ZXingCpp requires locked bitmap data** – uses `LockBits()` + `Marshal.Copy()` for zero-copy pixel access
- **Numeric-only QR content is always rejected** – prevents false positives from label text being decoded as barcodes
- **`WordInfo.Top` is in top-down coordinates** (not raw PDF page coordinates) — see [Coordinate System Conversion](#coordinate-system-conversion)

## Technical Details: ZXingCpp Integration

```csharp
// Lock bitmap pixels and copy to managed byte array
var bitmapData = bitmap.LockBits(..., PixelFormat.Format32bppArgb);
var pixels = new byte[bitmapData.Stride * bitmapData.Height];
Marshal.Copy(bitmapData.Scan0, pixels, 0, pixels.Length);

// Create ImageView wrapper (zero-copy view into pixel buffer)
var iv = new ImageView(
    pixels,
    bitmapData.Width,
    bitmapData.Height,
    ZXingCpp.ImageFormat.BGRA,  // 32-bit ARGB ? BGRA byte order
    bitmapData.Stride);

// Configure reader for QR codes
var reader = new BarcodeReader()
{
    Formats = BarcodeFormat.QRCode,
    TryInvert = true,           // Try both normal and inverted colors
    TextMode = TextMode.Plain,  // Return raw text
    MaxNumberOfSymbols = 64     // Scan up to 64 codes per image
};

// Decode all QR codes in one call
var barcodes = reader.From(iv);
```

- **ImageView** is a lightweight wrapper (no pixel copying) over the byte array
- **BGRA format** matches Windows GDI+ bitmap memory layout (B, G, R, A byte order)
- **TryInvert** helps with poor contrast / inverted QR codes
- **Position property** provides accurate corner coordinates for label matching

## Technical Details: Barcode Validation

```csharp
// Static compiled regex shared across all threads
private static readonly Regex NumericOnly = new(@"^\d+$", RegexOptions.Compiled);

// Validation applied to every decoded barcode (full-page and grid fallback)
string qrText = barcode.Text?.Trim()!;
if (string.IsNullOrEmpty(qrText) || !barcode.IsValid
    || NumericOnly.IsMatch(qrText))    // ? reject label false positives
    continue;
```

**Why numeric-only filter is needed:** When grid fallback cuts the page into small cells, some cells contain only the numeric label below a QR code. ZXingCpp may interpret this as a valid barcode, producing false positives like `"7834"`. Since real QR codes in this use case always contain URLs (non-numeric), rejecting digit-only content eliminates these false positives without affecting real results.
