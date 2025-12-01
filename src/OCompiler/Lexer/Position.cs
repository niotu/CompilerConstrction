using System;
using System.IO;

namespace OCompiler.Lexer;

/// <summary>
/// Position in source code (line, column, optional file name)
/// </summary>
public readonly struct Position(int line, int column, string fileName = "") : IEquatable<Position>
{
    public int Line { get; } = Math.Max(1, line);
    public int Column { get; } = Math.Max(1, column);
    public string FileName { get; } = fileName ?? string.Empty;

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