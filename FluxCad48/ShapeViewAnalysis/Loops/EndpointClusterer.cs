using FluxCad48.Geometry;
using System.Collections.Generic;

namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class EndpointClusterer
	{
		public EndpointClusterResult Cluster(
			IReadOnlyList<Segment2D> segments,
			LoopExtractionOptions options)
		{
			var result = new EndpointClusterResult();

			if (segments == null || segments.Count == 0)
				return result;

			if (options == null)
				options = new LoopExtractionOptions();

			for (int i = 0; i < segments.Count; i++)
			{
				Segment2D segment = segments[i];

				int startNodeId = FindOrCreateNode(result, segment.Start, options.EndpointTolerance);
				int endNodeId = FindOrCreateNode(result, segment.End, options.EndpointTolerance);

				var endpointInfo = new SegmentEndpointInfo();
				endpointInfo.Segment = segment;
				endpointInfo.StartNodeId = startNodeId;
				endpointInfo.EndNodeId = endNodeId;

				result.SegmentEndpoints.Add(endpointInfo);
			}

			return result;
		}

		private int FindOrCreateNode(
			EndpointClusterResult result,
			Point2D point,
			double tolerance)
		{
			for (int i = 0; i < result.Nodes.Count; i++)
			{
				EndpointNode node = result.Nodes[i];

				if (Distance(node.Position, point) <= tolerance)
				{
					MergeNodePosition(node, point);
					return node.Id;
				}
			}

			var newNode = new EndpointNode();
			newNode.Id = result.Nodes.Count + 1;
			newNode.Position = point;
			newNode.MergeCount = 1;

			result.Nodes.Add(newNode);

			return newNode.Id;
		}

		private static void MergeNodePosition(EndpointNode node, Point2D point)
		{
			int oldCount = node.MergeCount;
			int newCount = oldCount + 1;

			double x = (node.Position.X * oldCount + point.X) / newCount;
			double y = (node.Position.Y * oldCount + point.Y) / newCount;

			node.Position = new Point2D(x, y);
			node.MergeCount = newCount;
		}

		private static double Distance(Point2D a, Point2D b)
		{
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return System.Math.Sqrt(dx * dx + dy * dy);
		}
	}
}