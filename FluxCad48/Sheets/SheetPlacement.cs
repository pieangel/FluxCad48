using FluxCad48.Geometry;

namespace FluxCad48.Sheets
{
	public class SheetPlacement
	{
		public SheetRegion SourceSheet { get; set; }

		public Bounds2D SourceBounds { get; set; }

		public CadPoint2D TargetBottomLeft { get; set; }

		public double MoveX { get; set; }

		public double MoveY { get; set; }

		public int RowIndex { get; set; }

		public int ColumnIndex { get; set; }
	}
}