using PDFReaderV2.Models;

namespace PDFReaderV2.Interfaces;

public interface ILabelFinder
{
    string FindLabel(List<WordInfo> pageWords,
        double cellLeft, double cellTop, double cellRight, double cellBottom);
}
