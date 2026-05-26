using System.Collections.Generic;

namespace FluxCad48.ShapeViewAnalysis
{
	public static class ViewIslandRoleClassifier
	{
		public static void ClassifyAll(IList<ViewIsland> islands)
		{
			if (islands == null)
				return;

			for (int i = 0; i < islands.Count; i++)
			{
				Classify(islands[i], i);
			}
		}

		private static void Classify(ViewIsland island, int index)
		{
			if (island == null)
				return;

			if (island.IsThinViewCandidate)
			{
				island.Role = ViewIslandRole.ThinReference;
			}
			else if (island.IsShapeViewCandidate)
			{
				island.Role = ViewIslandRole.ShapeView;
			}
			else if (island.MatchedDimensions != null && island.MatchedDimensions.Count > 0)
			{
				island.Role = ViewIslandRole.DimensionGroup;
			}
			else if (island.MatchedTexts != null && island.MatchedTexts.Count > 0)
			{
				island.Role = ViewIslandRole.TextGroup;
			}
			else
			{
				island.Role = ViewIslandRole.Unknown;
			}

			int geomCount = island.GeometryEntities != null
				? island.GeometryEntities.Count
				: 0;

			island.DebugLabel =
				"Island " + index +
				" / " + island.Role +
				" / Geom=" + geomCount +
				" / Thin=" + island.IsThinViewCandidate;
		}
	}
}