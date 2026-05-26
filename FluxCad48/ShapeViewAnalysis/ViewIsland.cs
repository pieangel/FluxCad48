using System.Collections.Generic;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis
{
	public sealed class ViewIsland
	{
		public int Index { get; set; }

		public List<SheetEntity> GeometryEntities { get; private set; }
		public List<SheetEntity> MatchedDimensions { get; private set; }
		public List<SheetEntity> MatchedTexts { get; private set; }

		public Bounds2D Bounds { get; set; }

		public double Width { get; set; }
		public double Height { get; set; }

		public bool IsShapeViewCandidate { get; set; }

		public bool IsThinViewCandidate { get; set; }
		public double ThinnessRatio { get; set; }

		public double ShapeViewScore { get; set; }

		public ViewIsland()
		{
			GeometryEntities = new List<SheetEntity>();
			MatchedDimensions = new List<SheetEntity>();
			MatchedTexts = new List<SheetEntity>();
		}
	}
}