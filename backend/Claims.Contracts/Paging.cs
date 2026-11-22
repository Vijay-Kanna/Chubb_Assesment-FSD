namespace Claims.Contracts;

public readonly record struct PageWindow(int Number, int Size)
{
    public const int DefaultSize = 50;
    public const int MaxPage = 10000;
    public const string TotalHeader = "X-Total-Count";

    public bool IsValid => Number is >= 1 and <= MaxPage && Size is >= 1 and <= 100;
    public int Offset => (Number - 1) * Size;

    public Dictionary<string, string[]> ValidationErrors()
    {
        var errors = new Dictionary<string, string[]>();
        if (Number is < 1 or > MaxPage)
            errors["page"] = ["Enter a page from 1 to 10000."];
        if (Size is < 1 or > 100)
            errors["pageSize"] = ["Enter a page size from 1 to 100."];
        return errors;
    }
}

public sealed record PageItems<T>(IReadOnlyList<T> Items, int? Total = null);
