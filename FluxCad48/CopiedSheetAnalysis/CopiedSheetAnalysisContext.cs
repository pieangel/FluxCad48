using System.Collections.Generic;
using Teigha.DatabaseServices;

namespace FluxCad48.CopiedSheetAnalysis
{
	internal class CopiedSheetAnalysisContext
	{
		public List<ObjectId> GeometryIds { get; private set; }
		public List<ObjectId> DimensionIds { get; private set; }
		public List<ObjectId> TextIds { get; private set; }
		public List<ObjectId> BlockReferenceIds { get; private set; }
		public List<ObjectId> OtherIds { get; private set; }

		public CopiedSheetAnalysisContext()
		{
			GeometryIds = new List<ObjectId>();
			DimensionIds = new List<ObjectId>();
			TextIds = new List<ObjectId>();
			BlockReferenceIds = new List<ObjectId>();
			OtherIds = new List<ObjectId>();
		}
	}
}