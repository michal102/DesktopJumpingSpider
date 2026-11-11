namespace SpiderPet.Core;

/// <summary>Which corner of the screen the spider goes to when it sleeps or watches a video or game.</summary>
public enum RestSpot
{
    /// <summary>A free bottom corner (one no window covers), the nearer one; a top corner if both are covered.</summary>
    Auto,
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,
}
