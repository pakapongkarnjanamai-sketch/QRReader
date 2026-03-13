using PDFReaderV2.Interfaces;
using PDFReaderV2.Models;

namespace PDFReaderV2.Services;

public class LabelFinder : ILabelFinder
{
    public string FindLabel(List<WordInfo> pageWords,
        double cellLeft, double cellTop, double cellRight, double cellBottom)
    {
        var candidates = pageWords
            .Where(w =>
            {
                double wordCenterX = w.Left + w.Width / 2;
                double wordCenterY = w.Top + w.Height / 2;
                return wordCenterX >= cellLeft && wordCenterX <= cellRight &&
                       wordCenterY >= cellTop && wordCenterY <= cellBottom;
            })
            .OrderBy(w => w.Top)
            .ThenBy(w => w.Left)
            .ToList();

        if (candidates.Count > 0)
            return string.Join("", candidates.Select(c => c.Text));

        double expandX = (cellRight - cellLeft) * 0.2;
        double expandY = (cellBottom - cellTop) * 0.2;

        var extendedCandidates = pageWords
            .Where(w =>
            {
                double wordCenterX = w.Left + w.Width / 2;
                double wordCenterY = w.Top + w.Height / 2;
                return wordCenterX >= (cellLeft - expandX) && wordCenterX <= (cellRight + expandX) &&
                       wordCenterY >= (cellTop - expandY) && wordCenterY <= (cellBottom + expandY);
            })
            .OrderBy(w => w.Top)
            .ThenBy(w => w.Left)
            .ToList();

        return string.Join("", extendedCandidates.Select(c => c.Text));
    }
}
