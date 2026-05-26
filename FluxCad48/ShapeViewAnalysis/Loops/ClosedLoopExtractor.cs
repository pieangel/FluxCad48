using FluxCad48.Geometry;
using FluxCad48.ShapeViewAnalysis.Loops;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCad48.ShapeViewAnalysis.Loop
{
	public sealed class ClosedLoopExtractor
	{
		public ClosedLoopExtractionResult Extract(
			IReadOnlyList<Segment2D> segments,
			LoopExtractionOptions options)
		{
			if (segments == null)
				throw new ArgumentNullException("segments");

			if (options == null)
				options = new LoopExtractionOptions();

			var result = new ClosedLoopExtractionResult();
			result.InputSegments.AddRange(segments);

			if (segments.Count == 0)
			{
				result.AddWarning("No segments.");
				return result;
			}

			var clusterer = new EndpointClusterer();
			EndpointClusterResult cluster = clusterer.Cluster(segments, options);

			Dictionary<int, List<int>> adjacency = BuildAdjacency(cluster);
			var visited = new HashSet<int>();

			for (int i = 0; i < cluster.SegmentEndpoints.Count; i++)
			{
				if (visited.Contains(i))
					continue;

				ClosedLoopCandidate candidate = TraceChain(
					i,
					cluster,
					adjacency,
					visited);

				candidate.FinalizeGeometry();

				if (TryHealClosure(candidate, options))
					candidate.FinalizeGeometry();

				if (candidate.IsClosed)
					result.ClosedLoops.Add(candidate);
				else
					result.OpenChains.Add(candidate);
			}

			ClassifyOuterAndHoleLoops(result.ClosedLoops);

			if (result.ClosedLoops.Count == 0)
				result.AddWarning("No closed loop found.");

			if (result.OpenChains.Count > 0)
				result.AddWarning("Open chains detected: " + result.OpenChains.Count);

			return result;
		}

		private static Dictionary<int, List<int>> BuildAdjacency(
			EndpointClusterResult cluster)
		{
			var map = new Dictionary<int, List<int>>();

			for (int i = 0; i < cluster.SegmentEndpoints.Count; i++)
			{
				SegmentEndpointInfo info = cluster.SegmentEndpoints[i];

				AddAdjacency(map, info.StartNodeId, i);
				AddAdjacency(map, info.EndNodeId, i);
			}

			return map;
		}

		private static void AddAdjacency(
			Dictionary<int, List<int>> map,
			int nodeId,
			int segmentIndex)
		{
			List<int> list;

			if (!map.TryGetValue(nodeId, out list))
			{
				list = new List<int>();
				map[nodeId] = list;
			}

			list.Add(segmentIndex);
		}

		private ClosedLoopCandidate TraceChain(
			int startSegmentIndex,
			EndpointClusterResult cluster,
			Dictionary<int, List<int>> adjacency,
			HashSet<int> visited)
		{
			var candidate = new ClosedLoopCandidate();

			int currentSegmentIndex = startSegmentIndex;
			int? currentNodeId = null;
			int firstNodeId = -1;

			while (true)
			{
				if (visited.Contains(currentSegmentIndex))
					break;

				visited.Add(currentSegmentIndex);

				SegmentEndpointInfo info = cluster.SegmentEndpoints[currentSegmentIndex];
				candidate.Segments.Add(info.Segment);

				int nextNodeId;

				if (currentNodeId == null)
				{
					currentNodeId = info.StartNodeId;
					nextNodeId = info.EndNodeId;
					firstNodeId = info.StartNodeId;

					candidate.NodeIds.Add(info.StartNodeId);
					candidate.NodeIds.Add(info.EndNodeId);

					candidate.Vertices.Add(GetNodePosition(cluster, info.StartNodeId));
					candidate.Vertices.Add(GetNodePosition(cluster, info.EndNodeId));
				}
				else
				{
					if (info.StartNodeId == currentNodeId.Value)
						nextNodeId = info.EndNodeId;
					else
						nextNodeId = info.StartNodeId;

					candidate.NodeIds.Add(nextNodeId);
					candidate.Vertices.Add(GetNodePosition(cluster, nextNodeId));
				}

				if (nextNodeId == firstNodeId)
				{
					candidate.IsClosed = true;
					break;
				}

				int? nextSegmentIndex = FindNextSegment(
					currentSegmentIndex,
					nextNodeId,
					adjacency,
					visited);

				if (!nextSegmentIndex.HasValue)
					break;

				currentNodeId = nextNodeId;
				currentSegmentIndex = nextSegmentIndex.Value;
			}

			return candidate;
		}

		private static int? FindNextSegment(
			int currentSegmentIndex,
			int nodeId,
			Dictionary<int, List<int>> adjacency,
			HashSet<int> visited)
		{
			List<int> connected;

			if (!adjacency.TryGetValue(nodeId, out connected))
				return null;

			for (int i = 0; i < connected.Count; i++)
			{
				int segmentIndex = connected[i];

				if (segmentIndex == currentSegmentIndex)
					continue;

				if (visited.Contains(segmentIndex))
					continue;

				return segmentIndex;
			}

			return null;
		}

		private static Point2D GetNodePosition(
			EndpointClusterResult cluster,
			int nodeId)
		{
			return cluster.Nodes[nodeId - 1].Position;
		}

		private static bool TryHealClosure(
			ClosedLoopCandidate candidate,
			LoopExtractionOptions options)
		{
			if (candidate == null)
				return false;

			if (candidate.IsClosed)
				return false;

			if (!options.EnableGapHealing)
				return false;

			if (candidate.Vertices.Count < 3)
				return false;

			Point2D first = candidate.Vertices[0];
			Point2D last = candidate.Vertices[candidate.Vertices.Count - 1];

			double gap = Distance(first, last);
			double tolerance = Math.Max(options.GapTolerance, options.ClosureTolerance);

			if (gap > tolerance)
				return false;

			candidate.Vertices.Add(first);
			candidate.IsClosed = true;
			candidate.ClosureGap = gap;

			return true;
		}

		private static void ClassifyOuterAndHoleLoops(
			List<ClosedLoopCandidate> loops)
		{
			if (loops == null || loops.Count == 0)
				return;

			for (int i = 0; i < loops.Count; i++)
			{
				loops[i].IsHole = false;
				loops[i].NestingDepth = 0;
				loops[i].ParentLoopIndex = null;
			}

			for (int i = 0; i < loops.Count; i++)
			{
				ClosedLoopCandidate child = loops[i];
				Point2D testPoint = GetStableInteriorTestPoint(child);

				int? bestParent = null;
				double bestParentArea = double.MaxValue;

				for (int j = 0; j < loops.Count; j++)
				{
					if (i == j)
						continue;

					ClosedLoopCandidate parent = loops[j];

					if (parent.AbsArea <= child.AbsArea)
						continue;

					if (!parent.Bounds.Contains(testPoint))
						continue;

					if (!PointInPolygon(testPoint, parent.Vertices))
						continue;

					if (parent.AbsArea < bestParentArea)
					{
						bestParentArea = parent.AbsArea;
						bestParent = j;
					}
				}

				if (bestParent.HasValue)
				{
					child.ParentLoopIndex = bestParent.Value;
					child.NestingDepth = ComputeDepth(loops, bestParent.Value);
				}
			}

			for (int i = 0; i < loops.Count; i++)
			{
				loops[i].IsHole = (loops[i].NestingDepth % 2) == 1;
			}
		}

		private static int ComputeDepth(
			List<ClosedLoopCandidate> loops,
			int parentIndex)
		{
			int depth = 1;
			int? current = loops[parentIndex].ParentLoopIndex;

			while (current.HasValue)
			{
				depth++;
				current = loops[current.Value].ParentLoopIndex;
			}

			return depth;
		}

		private static Point2D GetStableInteriorTestPoint(
			ClosedLoopCandidate loop)
		{
			if (loop == null || loop.Vertices.Count == 0)
				return new Point2D(0, 0);

			Point2D center = loop.Bounds.Center;

			if (PointInPolygon(center, loop.Vertices))
				return center;

			Point2D first = loop.Vertices[0];

			return new Point2D(
				(first.X + center.X) * 0.5,
				(first.Y + center.Y) * 0.5);
		}

		private static bool PointInPolygon(
			Point2D point,
			IReadOnlyList<Point2D> polygon)
		{
			if (polygon == null || polygon.Count < 3)
				return false;

			bool inside = false;

			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
			{
				Point2D pi = polygon[i];
				Point2D pj = polygon[j];

				bool intersect =
					((pi.Y > point.Y) != (pj.Y > point.Y)) &&
					(point.X < (pj.X - pi.X) * (point.Y - pi.Y) /
					((pj.Y - pi.Y) + 1e-12) + pi.X);

				if (intersect)
					inside = !inside;
			}

			return inside;
		}

		private static double Distance(Point2D a, Point2D b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return Math.Sqrt(dx * dx + dy * dy);
		}
	}
}