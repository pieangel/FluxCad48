using Teigha.DatabaseServices;
using FluxCad48.Geometry;

namespace FluxCad48.Sheets
{
	public sealed class SelectedSheetUnitInfo
	{
		public ObjectId Id { get; set; }

		public string HandleText { get; set; } = "";
		public string TypeName { get; set; } = "";
		public string Layer { get; set; } = "";

		public string BlockName { get; set; } = "";

		public Bounds2D BoundsWcs { get; set; }
		public Point2D CenterWcs { get; set; }

		public string Text { get; set; } = "";
		public string NormalizedText { get; set; } = "";

		public bool IsTextLike { get; set; }
		public bool IsDimensionLike { get; set; }
		public bool IsCenterLineLike { get; set; }
		public bool IsHiddenLineLike { get; set; }
		public bool IsBlockReference { get; set; }
		public bool IsLineLike { get; set; }
	}
}