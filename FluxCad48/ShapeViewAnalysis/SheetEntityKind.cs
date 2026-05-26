namespace FluxCad48.ShapeViewAnalysis
{
	public enum SheetEntityKind
	{
		Unknown = 0,

		BlockReference,

		Text,
		MText,
		InsertAttribute,

		Dimension,
		Leader,

		Line,
		Polyline,
		Arc,
		Circle,
		Ellipse,
		Spline,

		Hatch,
		Solid,
		Face,
		Region,
		Point
	}
}