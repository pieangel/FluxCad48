using System.Collections.Generic;
using Teigha.DatabaseServices;

namespace FluxCad48.CopiedSheetAnalysis.BorderConnectedFiltering
{
	public class OutsideInRingFilterResult
	{
		public List<RingBand> Rings { get; private set; }

		public List<ObjectId> ExcludedEntityIds { get; private set; }

		public List<ObjectId> InnerCandidateIds { get; private set; }

		public List<KnownExcludedRegion> KnownExcludedRegions { get; private set; }

		public List<UnknownRegion> UnknownRegions { get; private set; }

		public OutsideInRingFilterResult()
		{
			Rings = new List<RingBand>();
			ExcludedEntityIds = new List<ObjectId>();
			InnerCandidateIds = new List<ObjectId>();
			KnownExcludedRegions = new List<KnownExcludedRegion>();
			UnknownRegions = new List<UnknownRegion>();
		}
	}
}