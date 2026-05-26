using FluxCad48.Geometry;

namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class EndpointNode
	{
		public int Id { get; set; }

		public Point2D Position { get; set; }

		public int MergeCount { get; set; }

		public EndpointNode()
		{
			Id = 0;
			MergeCount = 1;
		}
	}
}