using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCad48.CopiedSheetAnalysis.BorderConnectedFiltering
{
	public class OutsideInRingFilter
	{
		public OutsideInRingFilterResult Analyze(
			Extents3d sheetBounds,
			Dictionary<ObjectId, Extents3d> entityBoundsMap,
			int ringCount)
		{
			OutsideInRingFilterResult result = new OutsideInRingFilterResult();

			if (ringCount <= 0)
				ringCount = 5;

			List<RingBand> rings = BuildRings(sheetBounds, ringCount);
			result.Rings.AddRange(rings);

			HashSet<ObjectId> excludedSet = new HashSet<ObjectId>();

			for (int i = 0; i < rings.Count; i++)
			{
				RingBand ring = rings[i];

				foreach (KeyValuePair<ObjectId, Extents3d> pair in entityBoundsMap)
				{
					ObjectId id = pair.Key;

					if (excludedSet.Contains(id))
						continue;

					Extents3d b = pair.Value;

					if (!ring.Intersects(b))
						continue;

					excludedSet.Add(id);
					result.ExcludedEntityIds.Add(id);

					KnownExcludedRegion region = new KnownExcludedRegion();
					region.Bounds = b;
					region.EntityIds.Add(id);
					region.Reason = "RingIntersect_" + i;

					result.KnownExcludedRegions.Add(region);
				}
			}

			foreach (KeyValuePair<ObjectId, Extents3d> pair in entityBoundsMap)
			{
				if (excludedSet.Contains(pair.Key))
					continue;

				result.InnerCandidateIds.Add(pair.Key);
			}

			UnknownRegion unknown = new UnknownRegion();
			unknown.Bounds = GetInnermostBounds(sheetBounds, ringCount);
			unknown.Depth = ringCount;

			for (int i = 0; i < result.InnerCandidateIds.Count; i++)
				unknown.EntityIds.Add(result.InnerCandidateIds[i]);

			result.UnknownRegions.Add(unknown);

			return result;
		}

		private static List<RingBand> BuildRings(Extents3d bounds, int ringCount)
		{
			List<RingBand> rings = new List<RingBand>();

			double width = bounds.MaxPoint.X - bounds.MinPoint.X;
			double height = bounds.MaxPoint.Y - bounds.MinPoint.Y;

			double maxThickness = Math.Min(width, height) / 2.0;
			double ringThickness = maxThickness / ringCount;

			for (int i = 0; i < ringCount; i++)
			{
				double outerOffset = ringThickness * i;
				double innerOffset = ringThickness * (i + 1);

				RingBand band = new RingBand();
				band.RingIndex = i;
				band.Thickness = ringThickness;
				band.OuterBounds = Inflate(bounds, -outerOffset);
				band.InnerBounds = Inflate(bounds, -innerOffset);

				rings.Add(band);
			}

			return rings;
		}

		private static Extents3d GetInnermostBounds(Extents3d bounds, int ringCount)
		{
			double width = bounds.MaxPoint.X - bounds.MinPoint.X;
			double height = bounds.MaxPoint.Y - bounds.MinPoint.Y;

			double maxThickness = Math.Min(width, height) / 2.0;
			double ringThickness = maxThickness / ringCount;

			return Inflate(bounds, -(ringThickness * ringCount));
		}

		private static Extents3d Inflate(Extents3d bounds, double amount)
		{
			Point3d min = bounds.MinPoint;
			Point3d max = bounds.MaxPoint;

			Point3d newMin = new Point3d(
				min.X - amount,
				min.Y - amount,
				min.Z);

			Point3d newMax = new Point3d(
				max.X + amount,
				max.Y + amount,
				max.Z);

			return new Extents3d(newMin, newMax);
		}
	}
}