namespace Uniquer.Models;

public enum ImagesDifferenceType
{
    Identical,
    Similar,
    LeftBetter,
    RightBetter,
}

public class ImagesDifference(string path1, int width1, int height1, string path2, int width2, int height2, ImagesDifferenceType type)
{
    public string Path1 { get; } = path1;
    public int Width1 { get; } = width1;
    public int Height1 { get; } = height1;

    public string Path2 { get; } = path2;
    public int Width2 { get; } = width2;
    public int Height2 { get; } = height2;

    public ImagesDifferenceType Type { get; } = type;
}
