using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.CopiedSheetAnalysis.BorderConnectedFiltering
{
	public abstract class ShrinkingRegionState
	{
		public Extents3d Bounds { get; set; }

		public List<ObjectId> EntityIds { get; private set; }

		protected ShrinkingRegionState()
		{
			EntityIds = new List<ObjectId>();
		}
	}

	public class KnownExcludedRegion : ShrinkingRegionState
	{
		public string Reason { get; set; }
	}

	public class KnownViewRegion : ShrinkingRegionState
	{
		public string ViewRole { get; set; }
	}

	public class UnknownRegion : ShrinkingRegionState
	{
		public int Depth { get; set; }
	}
}