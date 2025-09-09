namespace OCompiler.Lexer;

/// <summary>
/// Позиция в исходном коде (строка, столбец, файл)
/// </summary>
public readonly struct Position : IEquatable<Position>
{
    public int Line { get; }
    public int Column { get; }
    public string FileName { get; }

    public Position(int line, int column, string fileName = "")
    {
        Line = Math.Max(1, line);
        Column = Math.Max(1, column);
        FileName = fileName ?? string.Empty;
    }

    public override string ToString()
    {
        if (string.IsNullOrEmpty(FileName))
            return $"{Line}:{Column}";
        return $"{Path.GetFileName(FileName)}:{Line}:{Column}";
    }

    public string ToFullString()
    {
        return $"{FileName}:{Line}:{Column}";
    }

    public Position NextColumn()
    {
        return new Position(Line, Column + 1, FileName);
    }

    public Position NextLine()
    {
        return new Position(Line + 1, 1, FileName);
    }

    public Position Advance(int columns)
    {
        return new Position(Line, Column + columns, FileName);
    }

    public bool Equals(Position other)
    {
        return Line == other.Line && 
               Column == other.Column && 
               FileName == other.FileName;
    }

    public override bool Equals(object? obj)
    {
        return obj is Position other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Line, Column, FileName);
    }

    public static bool operator ==(Position left, Position right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Position left, Position right)
    {
        return !left.Equals(right);
    }
}