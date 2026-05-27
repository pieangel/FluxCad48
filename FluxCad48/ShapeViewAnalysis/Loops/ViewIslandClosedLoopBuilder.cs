using System;
using System.Collections.Generic;
using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class ViewIslandClosedLoopBuilder
	{
		public ClosedLoopExtractionResult Build(
			ViewIsland island,
			LoopExtractionOptions options)
		{
			if (island == null)
				throw new ArgumentNullException("island");

			if (options == null)
				options = new LoopExtractionOptions();

			var loopEntities = CollectLoopExtractableEntities(island);

			var segmentExtractor = new SegmentExtractor();
			List<Segment2D> segments = segmentExtractor.Extract(loopEntities, options);

			var extractor = new ClosedLoopExtractor();
			ClosedLoopExtractionResult result = extractor.Extract(segments, options);

			return result;
		}

		private static List<SheetEntity> CollectLoopExtractableEntities(ViewIsland island)
		{
			var result = new List<SheetEntity>();

			if (island.GeometryEntities == null)
				return result;

			for (int i = 0; i < island.GeometryEntities.Count; i++)
			{
				SheetEntity e = island.GeometryEntities[i];

				if (e == null)
					continue;

				if (!e.IsLoopExtractableGeometry)
					continue;

				if (e.Layer != null)
				{
					string layer = e.Layer.ToLowerInvariant();

					if (layer == "cl" || layer == "center" || layer.Contains("center"))
						continue;

					if (layer == "hl" || layer == "hidden" || layer.Contains("hidden"))
						continue;
				}

				result.Add(e);
			}

			return result;
		}
	}
}