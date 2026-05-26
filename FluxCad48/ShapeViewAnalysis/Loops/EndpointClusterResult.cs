using System.Collections.Generic;

namespace FluxCad48.ShapeViewAnalysis.Loops
{
	public sealed class EndpointClusterResult
	{
		public List<EndpointNode> Nodes { get; private set; }
		public List<SegmentEndpointInfo> SegmentEndpoints { get; private set; }

		public EndpointClusterResult()
		{
			Nodes = new List<EndpointNode>();
			SegmentEndpoints = new List<SegmentEndpointInfo>();
		}
	}
}