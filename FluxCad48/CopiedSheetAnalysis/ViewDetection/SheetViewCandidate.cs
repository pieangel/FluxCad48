using System.Collections.Generic;
using Teigha.DatabaseServices;
using FluxCad48.Geometry;

namespace FluxCad48.CopiedSheetAnalysis.ViewDetection
{
	internal class SheetViewCandidate
	{
		public int Index { get; set; }

		public Bounds2D Bounds { get; set; }

		public List<ObjectId> EntityIds { get; private set; }

		public SheetViewRole Role { get; set; }

		public double Width { get; set; }
		public double Height { get; set; }
		public double Area { get; set; }

		public int GeometryCount { get; set; }
		public int DimensionNearCount { get; set; }
		public int TextNearCount { get; set; }

		public int CenterLineHintCount { get; set; }
		public int HiddenLineHintCount { get; set; }
		public int ClosedLoopHintCount { get; set; }

		public double MainViewScore { get; set; }
		public double ThicknessSideViewScore { get; set; }

		public SheetViewCandidate()
		{
			Index = -1;
			EntityIds = new List<ObjectId>();
			Role = SheetViewRole.Unknown;
		}
	}
}