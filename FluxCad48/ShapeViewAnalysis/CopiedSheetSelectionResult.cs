using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.ShapeViewAnalysis
{
	public sealed class CopiedSheetSelectionResult
	{
		public bool Success { get; set; }
		public string ErrorMessage { get; set; }

		public string SheetCode { get; set; }

		public List<ObjectId> SelectedIds { get; private set; }

		public Extents3d GroupBounds { get; set; }
		public Extents3d FrameBounds { get; set; }

		public bool HasGroupBounds { get; set; }
		public bool HasFrameBounds { get; set; }

		public CopiedSheetSelectionResult()
		{
			ErrorMessage = "";
			SheetCode = "";
			SelectedIds = new List<ObjectId>();
		}
	}
}