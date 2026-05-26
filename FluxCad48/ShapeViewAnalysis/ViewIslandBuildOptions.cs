namespace FluxCad48.ShapeViewAnalysis
{
	public sealed class ViewIslandBuildOptions
	{
		public double EndpointTolerance { get; set; }
		public double BoundsInflate { get; set; }
		public double NearDistance { get; set; }
		public int MinGeometryCount { get; set; }

		public ViewIslandBuildOptions()
		{
			EndpointTolerance = 0.5;
			BoundsInflate = 1.0;
			NearDistance = 2.0;
			MinGeometryCount = 2;
		}
	}
}