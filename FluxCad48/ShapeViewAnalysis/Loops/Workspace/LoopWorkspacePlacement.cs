using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis.Loops.Workspace
{
	public sealed class LoopWorkspacePlacement
	{
		public double TargetMinX { get; set; }
		public double TargetMaxY { get; set; }

		public int RowIndex { get; set; }
		public int ColumnIndex { get; set; }

		public Bounds2D ExistingBounds { get; set; }
	}
}