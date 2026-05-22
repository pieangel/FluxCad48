using Teigha.DatabaseServices;
using FluxCad48.Geometry;

namespace FluxCad48.Sheets
{
	public sealed class SheetFrameCandidate
	{
		public ObjectId ObjectId { get; set; }

		public string Handle { get; set; } = "";
		public string EntityType { get; set; } = "";
		public string Layer { get; set; } = "";

		public Bounds2D Bounds { get; set; }

		public int InsideEntityCount { get; set; }

		public int DimensionInsideCount { get; set; }
		public int CenterLineInsideCount { get; set; }
		public int HiddenLineInsideCount { get; set; }

		public bool HasEssentialDrawingFeatures { get; set; }

		public double Score { get; set; }
	}
}