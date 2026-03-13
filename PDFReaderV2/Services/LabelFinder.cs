using PDFReaderV2.Interfaces;
using PDFReaderV2.Models;

namespace PDFReaderV2.Services;

public class LabelFinder : ILabelFinder
{
    public string FindLabel(List<WordInfo> pageWords,
        double cellLeft, double cellTop, double cellRight, double cellBottom)
    {
        var candidates = FindCandidates(pageWords, cellLeft, cellTop, cellRight, cellBottom);

        if (candidates.Count == 0)
        {
            double expandX = (cellRight - cellLeft) * 0.2;
            double expandY = (cellBottom - cellTop) * 0.2;
            candidates = FindCandidates(pageWords,
                cellLeft - expandX, cellTop - expandY,
                cellRight + expandX, cellBottom + expandY);
        }

        if (candidates.Count == 0)
            return string.Empty;

        var bottomRow = GetBottomRow(candidates);
        return string.Join("", bottomRow.Select(c => c.Text));
    }

    private static List<WordInfo> FindCandidates(List<WordInfo> pageWords,
        double left, double top, double right, double bottom)
    {
        return pageWords
            .Where(w =>
            {
                double wordCenterX = w.Left + w.Width / 2;
                double wordCenterY = w.Top + w.Height / 2;
                return wordCenterX >= left && wordCenterX <= right &&
                       wordCenterY >= top && wordCenterY <= bottom;
            })
            .OrderBy(w => w.Top)
            .ThenBy(w => w.Left)
            .ToList();
    }

    private static List<WordInfo> GetBottomRow(List<WordInfo> candidates)
    {
        // PdfOrientedRectangle.Top = distance from top edge of page (increases downward).
        // The word with the highest Top value is visually at the bottom of the page.
        var bottomWord = candidates.MaxBy(w => w.Top)!;
        double rowThreshold = bottomWord.Height * 0.5;

        var bottomRow = candidates
            .Where(w => Math.Abs(w.Top - bottomWord.Top) <= rowThreshold)
            .OrderBy(w => w.Left)
            .ToList();

        return bottomRow;
    }
}
