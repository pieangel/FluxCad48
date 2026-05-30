using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.CopiedSheetAnalysis
{
	internal class CopiedSheetAnalysisContext
	{
		public Extents3d? Bounds { get; set; }

		public List<ObjectId> SourceIds { get; private set; }

		public List<ObjectId> GeometryIds { get; private set; }
		public List<ObjectId> DimensionIds { get; private set; }
		public List<ObjectId> TextIds { get; private set; }
		public List<ObjectId> BlockReferenceIds { get; private set; }
		public List<ObjectId> OtherIds { get; private set; }

		public CopiedSheetAnalysisContext()
		{
			SourceIds = new List<ObjectId>();

			GeometryIds = new List<ObjectId>();
			DimensionIds = new List<ObjectId>();
			TextIds = new List<ObjectId>();
			BlockReferenceIds = new List<ObjectId>();
			OtherIds = new List<ObjectId>();
		}
	}
}