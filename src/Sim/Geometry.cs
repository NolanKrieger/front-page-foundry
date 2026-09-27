namespace FrontPageFoundry.Sim;

/// <summary>A grid cell. +X is east, +Y is south (screen down).</summary>
public readonly record struct Cell(int X, int Y) : IComparable<Cell>
{
    public static Cell operator +(Cell a, Cell b) => new(a.X + b.X, a.Y + b.Y);
    public static Cell operator -(Cell a, Cell b) => new(a.X - b.X, a.Y - b.Y);
    public static Cell operator *(Cell a, int k) => new(a.X * k, a.Y * k);
    public int Manhattan(Cell other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    /// <summary>Row-major order (north to south, then west to east): the sim's stable iteration order.</summary>
    public int CompareTo(Cell other) => Y != other.Y ? Y.CompareTo(other.Y) : X.CompareTo(other.X);

    public override string ToString() => $"({X},{Y})";
}

public enum Dir : byte { North, East, South, West }

public static class DirExtensions
{
    public static Cell Offset(this Dir d) => d switch
    {
        Dir.North => new Cell(0, -1),
        Dir.East => new Cell(1, 0),
        Dir.South => new Cell(0, 1),
        _ => new Cell(-1, 0),
    };

    public static Dir Opposite(this Dir d) => (Dir)(((int)d + 2) & 3);
    public static Dir Clockwise(this Dir d) => (Dir)(((int)d + 1) & 3);
    public static Dir CounterClockwise(this Dir d) => (Dir)(((int)d + 3) & 3);
    public static bool IsHorizontal(this Dir d) => d is Dir.East or Dir.West;

    /// <summary>Direction from a cell to an orthogonally adjacent cell, or null if not adjacent.</summary>
    public static Dir? Between(Cell from, Cell to) => (to.X - from.X, to.Y - from.Y) switch
    {
        (0, -1) => Dir.North,
        (1, 0) => Dir.East,
        (0, 1) => Dir.South,
        (-1, 0) => Dir.West,
        _ => null,
    };
}
