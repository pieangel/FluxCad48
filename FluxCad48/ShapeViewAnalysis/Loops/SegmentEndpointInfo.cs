namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class SegmentEndpointInfo
	{
		public Segment2D Segment { get; set; }

		public int StartNodeId { get; set; }
		public int EndNodeId { get; set; }

		public SegmentEndpointInfo()
		{
			StartNodeId = -1;
			EndNodeId = -1;
		}
	}
}