namespace FM.App.Models;

/// <summary>
/// Defines the layout arrangements for multi-pane workspace.
/// </summary>
public enum PaneLayoutMode
{
    /// <summary>Single full-width pane.</summary>
    Single,

    /// <summary>Two panes arranged in vertical columns (left | right).</summary>
    DualVertical,

    /// <summary>Two panes arranged in horizontal rows (top / bottom).</summary>
    DualHorizontal,

    /// <summary>Three panes: 1 large left column, 2 stacked right rows.</summary>
    TripleLeft,

    /// <summary>Four panes arranged in a 2x2 grid.</summary>
    QuadGrid
}

